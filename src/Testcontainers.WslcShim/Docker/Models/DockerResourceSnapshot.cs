namespace Testcontainers.WslcShim.Docker.Models;

public sealed record DockerResourceSnapshot(
    string Id,
    IReadOnlyDictionary<string, string> Labels,
    string? Name = null,
    DateTimeOffset? CreatedAt = null);
