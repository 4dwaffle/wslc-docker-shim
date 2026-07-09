namespace Testcontainers.WslcShim.Docker;

public sealed record DockerResourceSnapshot(
    string Id,
    IReadOnlyDictionary<string, string> Labels,
    string? Name = null,
    DateTimeOffset? CreatedAt = null);
