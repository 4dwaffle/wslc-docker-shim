using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker.Models;

public sealed record DockerNetworkConnectRequest
{
    [JsonPropertyName("Container")]
    public string? Container { get; init; }

    [JsonPropertyName("EndpointConfig")]
    public DockerEndpointSettings? EndpointConfig { get; init; }
}

public sealed record DockerNetworkDisconnectRequest
{
    [JsonPropertyName("Container")]
    public string? Container { get; init; }

    [JsonPropertyName("Force")]
    public bool Force { get; init; }
}
