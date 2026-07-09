using System.Text.Json;
using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker;

public sealed record DockerUlimit
{
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("Soft")]
    public long Soft { get; init; }

    [JsonPropertyName("Hard")]
    public long Hard { get; init; }
}

public sealed record DockerDeviceRequest
{
    [JsonPropertyName("Driver")]
    public string? Driver { get; init; }

    [JsonPropertyName("Count")]
    public long Count { get; init; }

    [JsonPropertyName("DeviceIDs")]
    public IReadOnlyList<string> DeviceIds { get; init; } = [];

    [JsonPropertyName("Capabilities")]
    public IReadOnlyList<IReadOnlyList<string>> Capabilities { get; init; } = [];

    [JsonPropertyName("Options")]
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();
}

public sealed record DockerMount
{
    [JsonPropertyName("Type")]
    public string? Type { get; init; }

    [JsonPropertyName("Source")]
    public string? Source { get; init; }

    [JsonPropertyName("Target")]
    public string? Target { get; init; }

    [JsonPropertyName("ReadOnly")]
    public bool? ReadOnly { get; init; }

    [JsonPropertyName("TmpfsOptions")]
    public DockerTmpfsOptions? TmpfsOptions { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement> AdditionalProperties { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

public sealed record DockerTmpfsOptions
{
    [JsonPropertyName("SizeBytes")]
    public long? SizeBytes { get; init; }

    [JsonPropertyName("Mode")]
    public uint? Mode { get; init; }

    [JsonPropertyName("Options")]
    public IReadOnlyList<IReadOnlyList<string>> Options { get; init; } = [];

    [JsonExtensionData]
    public IDictionary<string, JsonElement> AdditionalProperties { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
