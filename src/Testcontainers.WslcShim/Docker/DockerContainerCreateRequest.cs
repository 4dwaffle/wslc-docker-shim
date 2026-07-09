using System.Text.Json;
using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker;

public sealed record DockerContainerCreateRequest
{
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
    public IReadOnlyList<string> Env { get; init; } = [];

    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("Cmd")]
    public IReadOnlyList<string> Cmd { get; init; } = [];

    [JsonPropertyName("WorkingDir")]
    public string? WorkingDir { get; init; }

    [JsonPropertyName("Entrypoint")]
    public IReadOnlyList<string> Entrypoint { get; init; } = [];

    [JsonPropertyName("NetworkDisabled")]
    public bool? NetworkDisabled { get; init; }

    [JsonPropertyName("StopSignal")]
    public string? StopSignal { get; init; }

    // ExposedPorts is Docker metadata rather than a listening-socket setting.
    // Compatibility validation accepts it only when WSLc also receives the
    // corresponding publish binding (or --publish-all).
    [JsonPropertyName("ExposedPorts")]
    public IReadOnlyDictionary<string, object?> ExposedPorts { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("Volumes")]
    public IReadOnlyDictionary<string, object?> Volumes { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("HostConfig")]
    public DockerHostConfig HostConfig { get; init; } = new();

    [JsonPropertyName("NetworkingConfig")]
    public DockerNetworkingConfig NetworkingConfig { get; init; } = new();

    // Capturing unknown fields lets the compatibility validator reject values
    // that WSLc cannot represent instead of silently discarding them.
    [JsonExtensionData]
    public IDictionary<string, JsonElement> AdditionalProperties { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
