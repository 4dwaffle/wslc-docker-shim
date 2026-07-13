namespace Testcontainers.WslcShim.Watch;

internal sealed class WatchDashboardState(TimeProvider? timeProvider = null)
{
    private readonly object sync = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, ContainerEntry> containers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> aliases = new(StringComparer.Ordinal);
    private long pendingSequence;
    private long version;

    public WatchContainerOperation BeginContainerCreate(string? name, string? image, string ports)
    {
        lock (sync)
        {
            var key = $"pending-{++pendingSequence}";
            var entry = new ContainerEntry(
                key,
                name ?? "<unnamed>",
                image ?? "<unknown>",
                clock.GetUtcNow(),
                "creating",
                ports);
            containers.Add(key, entry);
            AddAlias(entry.Name, key);
            Touch();
            return new WatchContainerOperation(key);
        }
    }

    public void CompleteContainerCreate(WatchContainerOperation operation, string id)
    {
        lock (sync)
        {
            if (!containers.TryGetValue(operation.ContainerKey, out var entry))
            {
                return;
            }

            entry.Id = id;
            entry.Status = "created";
            AddAlias(id, entry.Key);
            Touch();
        }
    }

    public void FailContainerCreate(WatchContainerOperation operation)
    {
        lock (sync)
        {
            if (!containers.TryGetValue(operation.ContainerKey, out var entry))
            {
                return;
            }

            entry.Status = "failed";
            Touch();
        }
    }

    public WatchContainerOperation? BeginContainerOperation(string reference, string? pendingStatus = null)
    {
        lock (sync)
        {
            var entry = Resolve(reference);
            if (entry is null)
            {
                return null;
            }

            var operation = new WatchContainerOperation(entry.Key, entry.Status);
            if (pendingStatus is not null && entry.Status != pendingStatus)
            {
                entry.Status = pendingStatus;
                Touch();
            }

            return operation;
        }
    }

    public void CompleteContainerOperation(WatchContainerOperation? operation, string? status = null)
    {
        if (operation is not { } value || status is null)
        {
            return;
        }

        lock (sync)
        {
            if (!containers.TryGetValue(value.ContainerKey, out var entry) || entry.Status == status)
            {
                return;
            }

            entry.Status = status;
            Touch();
        }
    }

    public void FailContainerOperation(WatchContainerOperation? operation)
    {
        if (operation is not { PreviousStatus: { } previousStatus } value)
        {
            return;
        }

        lock (sync)
        {
            if (!containers.TryGetValue(value.ContainerKey, out var entry) || entry.Status == previousStatus)
            {
                return;
            }

            entry.Status = previousStatus;
            Touch();
        }
    }

    public void SetContainerStatus(string reference, string status)
    {
        lock (sync)
        {
            var entry = Resolve(reference);
            if (entry is null || entry.Status == status)
            {
                return;
            }

            entry.Status = status;
            Touch();
        }
    }

    public IReadOnlyList<WatchContainerPollTarget> GetPollTargets()
    {
        lock (sync)
        {
            return containers.Values
                .Where(entry => entry.Id is not null && entry.Status is not ("removed" or "failed"))
                .Select(entry => new WatchContainerPollTarget(entry.Id!, entry.StateToken, entry.Observed))
                .ToArray();
        }
    }

    public bool ObserveListEntry(WatchContainerListObservation observation)
    {
        lock (sync)
        {
            var entry = Resolve(observation.Id);
            if (entry is null)
            {
                return false;
            }

            var displayChanged =
                observation.Name is not null && observation.Name != entry.Name ||
                observation.Image is not null && observation.Image != entry.Image ||
                observation.CreatedAt is not null && observation.CreatedAt != entry.CreatedAt ||
                !string.IsNullOrWhiteSpace(observation.Ports) && observation.Ports != entry.Ports;
            entry.MissingPolls = 0;
            entry.Name = observation.Name ?? entry.Name;
            entry.Image = observation.Image ?? entry.Image;
            entry.CreatedAt = observation.CreatedAt ?? entry.CreatedAt;
            entry.Ports = string.IsNullOrWhiteSpace(observation.Ports) ? entry.Ports : observation.Ports;
            AddAlias(entry.Name, entry.Key);
            var inspectNeeded = !entry.Observed || entry.StateToken != observation.StateToken;
            entry.Observed = true;
            entry.StateToken = observation.StateToken;
            if (displayChanged)
            {
                Touch();
            }

            return inspectNeeded;
        }
    }

    public void ObserveContainerDetails(string id, WatchContainerDetails details)
    {
        var normalizedStatus = string.Equals(details.Status, "exited", StringComparison.OrdinalIgnoreCase)
            ? details.ExitCode is long exitCode ? $"exited ({exitCode})" : "exited"
            : details.Status.ToLowerInvariant();
        SetContainerStatus(id, normalizedStatus);
    }

    public void ObserveContainerStats(string id, WatchContainerStats stats)
    {
        lock (sync)
        {
            var entry = Resolve(id);
            if (entry is null ||
                entry.CpuUsage == stats.CpuUsage && entry.MemoryUsage == stats.MemoryUsage)
            {
                return;
            }

            entry.CpuUsage = stats.CpuUsage;
            entry.MemoryUsage = stats.MemoryUsage;
            Touch();
        }
    }

    public void ClearContainerStats()
    {
        lock (sync)
        {
            var changed = false;
            foreach (var entry in containers.Values)
            {
                if (entry.CpuUsage == WatchContainerStats.Unavailable.CpuUsage &&
                    entry.MemoryUsage == WatchContainerStats.Unavailable.MemoryUsage)
                {
                    continue;
                }

                entry.CpuUsage = WatchContainerStats.Unavailable.CpuUsage;
                entry.MemoryUsage = WatchContainerStats.Unavailable.MemoryUsage;
                changed = true;
            }

            if (changed)
            {
                Touch();
            }
        }
    }

    public void ObserveMissing(string id)
    {
        lock (sync)
        {
            var entry = Resolve(id);
            if (entry is null || entry.Status == "removed")
            {
                return;
            }

            entry.MissingPolls++;
            if (entry.MissingPolls < 3)
            {
                return;
            }

            entry.Status = "removed";
            Touch();
        }
    }

    public WatchDashboardSnapshot Snapshot()
    {
        lock (sync)
        {
            var snapshots = containers.Values
                .Where(entry => entry.Status != "removed")
                .OrderBy(entry => entry.Id is null ? 0 : 1)
                .ThenByDescending(entry => entry.CreatedAt)
                .Select(entry => new WatchContainerSnapshot(
                    entry.Key,
                    entry.Id,
                    entry.Name,
                    entry.Image,
                    entry.CreatedAt,
                    entry.Status,
                    entry.CpuUsage,
                    entry.MemoryUsage,
                    entry.Ports))
                .ToArray();
            return new WatchDashboardSnapshot(version, snapshots);
        }
    }

    private ContainerEntry? Resolve(string reference)
    {
        if (aliases.TryGetValue(reference, out var key) && containers.TryGetValue(key, out var exact))
        {
            return exact;
        }

        return containers.Values.FirstOrDefault(entry =>
            entry.Id is not null && entry.Id.StartsWith(reference, StringComparison.Ordinal));
    }

    private void AddAlias(string? alias, string key)
    {
        if (!string.IsNullOrWhiteSpace(alias))
        {
            aliases[alias] = key;
        }
    }

    private void Touch() => version++;

    private sealed class ContainerEntry(
        string key,
        string name,
        string image,
        DateTimeOffset createdAt,
        string status,
        string ports)
    {
        public string Key { get; } = key;
        public string? Id { get; set; }
        public string Name { get; set; } = name;
        public string Image { get; set; } = image;
        public DateTimeOffset CreatedAt { get; set; } = createdAt;
        public string Status { get; set; } = status;
        public string CpuUsage { get; set; } = WatchContainerStats.Unavailable.CpuUsage;
        public string MemoryUsage { get; set; } = WatchContainerStats.Unavailable.MemoryUsage;
        public string Ports { get; set; } = ports;
        public string? StateToken { get; set; }
        public bool Observed { get; set; }
        public int MissingPolls { get; set; }
    }
}
