using System.Text.Json;
using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker.Models;

public sealed record DockerNetworkingConfig
{
    private IReadOnlyDictionary<string, DockerEndpointSettings> endpointsConfig =
        new Dictionary<string, DockerEndpointSettings>();
    private IDictionary<string, JsonElement> additionalProperties =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    [JsonPropertyName("EndpointsConfig")]
    public IReadOnlyDictionary<string, DockerEndpointSettings> EndpointsConfig
    {
        get => endpointsConfig;
        init => endpointsConfig = value ?? new Dictionary<string, DockerEndpointSettings>();
    }

    [JsonExtensionData]
    public IDictionary<string, JsonElement> AdditionalProperties
    {
        get => additionalProperties;
        init => additionalProperties = value ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }
}

public sealed record DockerEndpointSettings
{
    private IReadOnlyList<string> aliases = [];
    private IReadOnlyList<string> dnsNames = [];
    private IDictionary<string, JsonElement> additionalProperties =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    [JsonPropertyName("Aliases")]
    public IReadOnlyList<string> Aliases
    {
        get => aliases;
        init => aliases = value ?? [];
    }

    [JsonPropertyName("DNSNames")]
    public IReadOnlyList<string> DnsNames
    {
        get => dnsNames;
        init => dnsNames = value ?? [];
    }

    [JsonExtensionData]
    public IDictionary<string, JsonElement> AdditionalProperties
    {
        get => additionalProperties;
        init => additionalProperties = value ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }
}
