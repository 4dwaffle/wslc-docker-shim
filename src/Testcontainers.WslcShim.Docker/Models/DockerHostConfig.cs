using System.Text.Json;
using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker.Models;

public sealed record DockerHostConfig
{
    [JsonPropertyName("Binds")]
    public IReadOnlyList<string> Binds { get; init; } = [];

    [JsonPropertyName("PortBindings")]
    public IReadOnlyDictionary<string, IReadOnlyList<DockerPortBinding>> PortBindings { get; init; } =
        new Dictionary<string, IReadOnlyList<DockerPortBinding>>();

    [JsonPropertyName("PublishAllPorts")]
    public bool PublishAllPorts { get; init; }

    [JsonPropertyName("AutoRemove")]
    public bool AutoRemove { get; init; }

    [JsonPropertyName("Privileged")]
    public bool Privileged { get; init; }

    [JsonPropertyName("NetworkMode")]
    public string? NetworkMode { get; init; }

    [JsonPropertyName("Dns")]
    public IReadOnlyList<string> Dns { get; init; } = [];

    [JsonPropertyName("DnsOptions")]
    public IReadOnlyList<string> DnsOptions { get; init; } = [];

    [JsonPropertyName("DnsSearch")]
    public IReadOnlyList<string> DnsSearch { get; init; } = [];

    [JsonPropertyName("Memory")]
    public long Memory { get; init; }

    [JsonPropertyName("NanoCpus")]
    public long NanoCpus { get; init; }

    [JsonPropertyName("CpuCount")]
    public long CpuCount { get; init; }

    [JsonPropertyName("ShmSize")]
    public long ShmSize { get; init; }

    [JsonPropertyName("Tmpfs")]
    public IReadOnlyDictionary<string, string> Tmpfs { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("Ulimits")]
    public IReadOnlyList<DockerUlimit> Ulimits { get; init; } = [];

    [JsonPropertyName("DeviceRequests")]
    public IReadOnlyList<DockerDeviceRequest> DeviceRequests { get; init; } = [];

    [JsonPropertyName("Mounts")]
    public IReadOnlyList<DockerMount> Mounts { get; init; } = [];

    [JsonExtensionData]
    public IDictionary<string, JsonElement> AdditionalProperties { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
