using System.Text.Json;
using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker.Models;

public sealed record DockerNetworkingConfig
{
    [JsonPropertyName("EndpointsConfig")]
    public IReadOnlyDictionary<string, DockerEndpointSettings> EndpointsConfig { get; init; } =
        new Dictionary<string, DockerEndpointSettings>();

    [JsonExtensionData]
    public IDictionary<string, JsonElement> AdditionalProperties { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

public sealed record DockerEndpointSettings
{
    [JsonPropertyName("Aliases")]
    public IReadOnlyList<string> Aliases { get; init; } = [];

    [JsonPropertyName("DNSNames")]
    public IReadOnlyList<string> DnsNames { get; init; } = [];

    [JsonExtensionData]
    public IDictionary<string, JsonElement> AdditionalProperties { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
