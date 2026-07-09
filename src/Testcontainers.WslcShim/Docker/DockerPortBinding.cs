using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker;

public sealed record DockerPortBinding(
    [property: JsonPropertyName("HostIp")] string? HostIp,
    [property: JsonPropertyName("HostPort")] string? HostPort);
