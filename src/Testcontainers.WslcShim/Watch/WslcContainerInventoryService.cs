using System.Globalization;
using System.Text.Json;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Wslc;

namespace Testcontainers.WslcShim.Watch;

internal sealed class WslcContainerInventoryService(
    IWslcProcessRunner processRunner,
    WatchDashboardState dashboard) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                dashboard.ClearContainerStats();
            }

            await Task.Delay(RefreshInterval, stoppingToken);
        }
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var targets = dashboard.GetPollTargets();
        if (targets.Count == 0)
        {
            return;
        }

        var listCommand = WslcCommandBuilder.BuildListResourcesCommand(DockerResourceKind.Container);
        var listResult = await processRunner.RunAsync(listCommand, cancellationToken);
        if (listResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"wslc list exited with code {listResult.ExitCode}.");
        }

        var observations = ParseList(listResult.StandardOutput);
        var stats = await ReadStatsAsync(cancellationToken);
        foreach (var target in targets)
        {
            if (!observations.TryGetValue(target.Id, out var observation))
            {
                dashboard.ObserveMissing(target.Id);
                continue;
            }

            dashboard.ObserveContainerStats(
                target.Id,
                stats.TryGetValue(target.Id, out var usage)
                    ? usage
                    : WatchContainerStats.Unavailable);

            if (!dashboard.ObserveListEntry(observation))
            {
                continue;
            }

            var inspectCommand = WslcCommandBuilder.BuildInspectResourceCommand(DockerResourceKind.Container, target.Id);
            var inspectResult = await processRunner.RunAsync(inspectCommand, cancellationToken);
            if (inspectResult.ExitCode == 0 && TryParseDetails(inspectResult.StandardOutput, out var details))
            {
                dashboard.ObserveContainerDetails(target.Id, details);
            }
        }
    }

    private async Task<IReadOnlyDictionary<string, WatchContainerStats>> ReadStatsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var command = WslcCommandBuilder.BuildContainerStatsCommand();
            var result = await processRunner.RunAsync(command, cancellationToken);
            if (result.ExitCode != 0)
            {
                return new Dictionary<string, WatchContainerStats>(StringComparer.Ordinal);
            }

            return ParseStats(result.StandardOutput);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new Dictionary<string, WatchContainerStats>(StringComparer.Ordinal);
        }
    }

    internal static IReadOnlyDictionary<string, WatchContainerListObservation> ParseList(string json)
    {
        using var document = JsonDocument.Parse(json);
        var observations = new Dictionary<string, WatchContainerListObservation>(StringComparer.Ordinal);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return observations;
        }

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = GetString(item, "Id") ?? GetString(item, "ID");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var stateToken = item.TryGetProperty("State", out var state)
                ? state.GetRawText()
                : string.Empty;
            if (item.TryGetProperty("StateChangedAt", out var changedAt))
            {
                stateToken += "|" + changedAt.GetRawText();
            }

            observations[id] = new WatchContainerListObservation(
                id,
                GetString(item, "Name"),
                GetString(item, "Image"),
                GetTimestamp(item, "CreatedAt"),
                FormatPorts(item),
                stateToken);
        }

        return observations;
    }

    internal static bool TryParseDetails(string json, out WatchContainerDetails details)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            root = root.EnumerateArray().FirstOrDefault();
        }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("State", out var state) ||
            state.ValueKind != JsonValueKind.Object)
        {
            details = new WatchContainerDetails("unknown", null);
            return false;
        }

        var status = GetString(state, "Status") ??
                     (state.TryGetProperty("Running", out var running) && running.ValueKind == JsonValueKind.True
                         ? "running"
                         : "unknown");
        long? exitCode = state.TryGetProperty("ExitCode", out var exitCodeElement) &&
                         exitCodeElement.ValueKind == JsonValueKind.Number &&
                         exitCodeElement.TryGetInt64(out var value)
            ? value
            : null;
        details = new WatchContainerDetails(status, exitCode);
        return true;
    }

    internal static IReadOnlyDictionary<string, WatchContainerStats> ParseStats(string json)
    {
        using var document = JsonDocument.Parse(json);
        var observations = new Dictionary<string, WatchContainerStats>(StringComparer.Ordinal);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return observations;
        }

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = GetString(item, "ID") ?? GetString(item, "Id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            observations[id] = new WatchContainerStats(
                NormalizeMetric(GetString(item, "CPUPerc")),
                CurrentMemoryUsage(GetString(item, "MemUsage")));
        }

        return observations;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string NormalizeMetric(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static string CurrentMemoryUsage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "—";
        }

        var separator = value.IndexOf('/', StringComparison.Ordinal);
        return NormalizeMetric(separator < 0 ? value : value[..separator]);
    }

    private static DateTimeOffset? GetTimestamp(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var unixSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        return property.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
    }

    private static string FormatPorts(JsonElement item)
    {
        if (!item.TryGetProperty("Ports", out var ports) || ports.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var values = new List<string>();
        foreach (var port in ports.EnumerateArray())
        {
            if (port.ValueKind == JsonValueKind.String)
            {
                values.Add(port.GetString() ?? string.Empty);
                continue;
            }

            if (port.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var hostIp = GetString(port, "HostIp") ?? GetString(port, "IP");
            var hostPort = GetString(port, "HostPort") ?? NumberAsString(port, "PublicPort");
            var containerPort = GetString(port, "ContainerPort") ?? NumberAsString(port, "PrivatePort");
            var type = GetString(port, "Type") ?? "tcp";
            if (containerPort is not null)
            {
                values.Add(hostPort is null
                    ? $"{containerPort}/{type}"
                    : $"{hostIp ?? "0.0.0.0"}:{hostPort}->{containerPort}/{type}");
            }
        }

        return string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string? NumberAsString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetRawText()
            : null;
}
