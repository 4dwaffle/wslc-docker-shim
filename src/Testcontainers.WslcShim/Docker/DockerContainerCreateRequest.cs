using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker;

public sealed class DockerContainerCreateRequest
{
    [JsonPropertyName("Image")]
    public string? Image { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("Env")]
    public IReadOnlyList<string> Env { get; init; } = [];

    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("Cmd")]
    public IReadOnlyList<string> Cmd { get; init; } = [];

    [JsonPropertyName("ExposedPorts")]
    public IReadOnlyDictionary<string, object?> ExposedPorts { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("HostConfig")]
    public DockerHostConfig HostConfig { get; init; } = new();
}
