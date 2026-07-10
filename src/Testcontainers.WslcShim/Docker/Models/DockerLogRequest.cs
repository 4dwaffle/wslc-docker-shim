namespace Testcontainers.WslcShim.Docker.Models;

public sealed record DockerLogRequest(
    bool Follow,
    bool Timestamps,
    string? Tail);
