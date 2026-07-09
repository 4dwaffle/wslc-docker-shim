namespace Testcontainers.WslcShim.Docker;

public sealed record DockerLogRequest(
    bool Follow,
    bool Timestamps,
    string? Tail);
