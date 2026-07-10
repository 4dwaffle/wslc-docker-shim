using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker.Models;

public sealed class DockerResourceCreateRequest
{
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("Driver")]
    public string? Driver { get; init; }

    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
}
