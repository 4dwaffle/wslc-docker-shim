namespace Testcontainers.WslcShim.Watch;

internal sealed record WatchContainerSnapshot(
    string Key,
    string? Id,
    string Name,
    string Image,
    DateTimeOffset CreatedAt,
    string Status,
    string CpuUsage,
    string MemoryUsage,
    string Ports);

internal sealed record WatchDashboardSnapshot(
    long Version,
    IReadOnlyList<WatchContainerSnapshot> Containers);

internal readonly record struct WatchContainerOperation(
    string ContainerKey,
    string? PreviousStatus = null);

internal sealed record WatchContainerPollTarget(string Id, string? StateToken, bool Observed);

internal sealed record WatchContainerListObservation(
    string Id,
    string? Name,
    string? Image,
    DateTimeOffset? CreatedAt,
    string Ports,
    string StateToken);

internal sealed record WatchContainerDetails(string Status, long? ExitCode);

internal sealed record WatchContainerStats(string CpuUsage, string MemoryUsage)
{
    public static WatchContainerStats Unavailable { get; } = new("—", "—");
}
