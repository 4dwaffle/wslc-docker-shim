using System.Text.Json;
using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker.Models;

public sealed record DockerContainerCreateRequest
{
    private IReadOnlyList<string> env = [];
    private IReadOnlyDictionary<string, string> labels = new Dictionary<string, string>();
    private IReadOnlyList<string> cmd = [];
    private IReadOnlyList<string> entrypoint = [];
    private IReadOnlyDictionary<string, object?> exposedPorts = new Dictionary<string, object?>();
    private IReadOnlyDictionary<string, object?> volumes = new Dictionary<string, object?>();
    private DockerHostConfig hostConfig = new();
    private DockerNetworkingConfig networkingConfig = new();
    private IDictionary<string, JsonElement> additionalProperties =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    [JsonPropertyName("Image")]
    public string? Image { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("Hostname")]
    public string? Hostname { get; init; }

    [JsonPropertyName("Domainname")]
    public string? Domainname { get; init; }

    [JsonPropertyName("User")]
    public string? User { get; init; }

    // These control whether Docker attaches the create/start HTTP connection to
    // a stream. The shim exposes detached create/start endpoints, so they do not
    // change the WSLc container configuration.
    [JsonPropertyName("AttachStdin")]
    public bool AttachStdin { get; init; }

    [JsonPropertyName("AttachStdout")]
    public bool AttachStdout { get; init; }

    [JsonPropertyName("AttachStderr")]
    public bool AttachStderr { get; init; }

    [JsonPropertyName("Tty")]
    public bool Tty { get; init; }

    [JsonPropertyName("OpenStdin")]
    public bool OpenStdin { get; init; }

    [JsonPropertyName("StdinOnce")]
    public bool StdinOnce { get; init; }

    [JsonPropertyName("Env")]
    public IReadOnlyList<string> Env
    {
        get => env;
        init => env = value ?? [];
    }

    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string> Labels
    {
        get => labels;
        init => labels = value ?? new Dictionary<string, string>();
    }

    [JsonPropertyName("Cmd")]
    public IReadOnlyList<string> Cmd
    {
        get => cmd;
        init => cmd = value ?? [];
    }

    [JsonPropertyName("WorkingDir")]
    public string? WorkingDir { get; init; }

    [JsonPropertyName("Entrypoint")]
    public IReadOnlyList<string> Entrypoint
    {
        get => entrypoint;
        init => entrypoint = value ?? [];
    }

    [JsonPropertyName("NetworkDisabled")]
    public bool? NetworkDisabled { get; init; }

    [JsonPropertyName("StopSignal")]
    public string? StopSignal { get; init; }

    // ExposedPorts is Docker metadata rather than a listening-socket setting.
    // Compatibility validation accepts it only when WSLc also receives the
    // corresponding publish binding (or --publish-all).
    [JsonPropertyName("ExposedPorts")]
    public IReadOnlyDictionary<string, object?> ExposedPorts
    {
        get => exposedPorts;
        init => exposedPorts = value ?? new Dictionary<string, object?>();
    }

    [JsonPropertyName("Volumes")]
    public IReadOnlyDictionary<string, object?> Volumes
    {
        get => volumes;
        init => volumes = value ?? new Dictionary<string, object?>();
    }

    [JsonPropertyName("HostConfig")]
    public DockerHostConfig HostConfig
    {
        get => hostConfig;
        init => hostConfig = value ?? new DockerHostConfig();
    }

    [JsonPropertyName("NetworkingConfig")]
    public DockerNetworkingConfig NetworkingConfig
    {
        get => networkingConfig;
        init => networkingConfig = value ?? new DockerNetworkingConfig();
    }

    // Capturing unknown fields lets the compatibility validator reject values
    // that WSLc cannot represent instead of silently discarding them.
    [JsonExtensionData]
    public IDictionary<string, JsonElement> AdditionalProperties
    {
        get => additionalProperties;
        init => additionalProperties = value ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }
}
