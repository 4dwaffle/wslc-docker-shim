namespace Testcontainers.WslcShim.Wslc;

internal sealed record WslcExecCacheOptions
{
    // Docker clients commonly inspect an exec immediately after its buffered start response.
    // Five minutes leaves ample compatibility headroom without retaining request payloads forever.
    public TimeSpan CompletedExecRetention { get; init; } = TimeSpan.FromMinutes(5);

    public int MaxCompletedExecs { get; init; } = 1024;
}
