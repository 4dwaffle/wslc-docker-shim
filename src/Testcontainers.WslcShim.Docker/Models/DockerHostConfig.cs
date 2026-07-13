using System.Text.Json;
using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker.Models;

public sealed record DockerHostConfig
{
    private IReadOnlyList<string> binds = [];
    private IReadOnlyDictionary<string, IReadOnlyList<DockerPortBinding>> portBindings =
        new Dictionary<string, IReadOnlyList<DockerPortBinding>>();
    private IReadOnlyList<string> dns = [];
    private IReadOnlyList<string> dnsOptions = [];
    private IReadOnlyList<string> dnsSearch = [];
    private IReadOnlyDictionary<string, string> tmpfs = new Dictionary<string, string>();
    private IReadOnlyList<DockerUlimit> ulimits = [];
    private IReadOnlyList<DockerDeviceRequest?> deviceRequests = [];
    private IReadOnlyList<DockerMount> mounts = [];
    private IDictionary<string, JsonElement> additionalProperties =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    [JsonPropertyName("Binds")]
    public IReadOnlyList<string> Binds
    {
        get => binds;
        init => binds = value ?? [];
    }

    [JsonPropertyName("PortBindings")]
    public IReadOnlyDictionary<string, IReadOnlyList<DockerPortBinding>> PortBindings
    {
        get => portBindings;
        init => portBindings = value ?? new Dictionary<string, IReadOnlyList<DockerPortBinding>>();
    }

    [JsonPropertyName("PublishAllPorts")]
    public bool PublishAllPorts { get; init; }

    [JsonPropertyName("AutoRemove")]
    public bool AutoRemove { get; init; }

    [JsonPropertyName("Privileged")]
    public bool Privileged { get; init; }

    [JsonPropertyName("NetworkMode")]
    public string? NetworkMode { get; init; }

    [JsonPropertyName("Dns")]
    public IReadOnlyList<string> Dns
    {
        get => dns;
        init => dns = value ?? [];
    }

    [JsonPropertyName("DnsOptions")]
    public IReadOnlyList<string> DnsOptions
    {
        get => dnsOptions;
        init => dnsOptions = value ?? [];
    }

    [JsonPropertyName("DnsSearch")]
    public IReadOnlyList<string> DnsSearch
    {
        get => dnsSearch;
        init => dnsSearch = value ?? [];
    }

    [JsonPropertyName("Memory")]
    public long Memory { get; init; }

    [JsonPropertyName("NanoCpus")]
    public long NanoCpus { get; init; }

    [JsonPropertyName("CpuCount")]
    public long CpuCount { get; init; }

    [JsonPropertyName("ShmSize")]
    public long ShmSize { get; init; }

    [JsonPropertyName("Tmpfs")]
    public IReadOnlyDictionary<string, string> Tmpfs
    {
        get => tmpfs;
        init => tmpfs = value ?? new Dictionary<string, string>();
    }

    [JsonPropertyName("Ulimits")]
    public IReadOnlyList<DockerUlimit> Ulimits
    {
        get => ulimits;
        init => ulimits = value ?? [];
    }

    [JsonPropertyName("DeviceRequests")]
    public IReadOnlyList<DockerDeviceRequest?> DeviceRequests
    {
        get => deviceRequests;
        init => deviceRequests = value ?? [];
    }

    [JsonPropertyName("Mounts")]
    public IReadOnlyList<DockerMount> Mounts
    {
        get => mounts;
        init => mounts = value ?? [];
    }

    [JsonExtensionData]
    public IDictionary<string, JsonElement> AdditionalProperties
    {
        get => additionalProperties;
        init => additionalProperties = value ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }
}
