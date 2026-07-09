using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker;

public sealed class DockerHostConfig
{
    [JsonPropertyName("Binds")]
    public IReadOnlyList<string> Binds { get; init; } = [];

    [JsonPropertyName("PortBindings")]
    public IReadOnlyDictionary<string, IReadOnlyList<DockerPortBinding>> PortBindings { get; init; } =
        new Dictionary<string, IReadOnlyList<DockerPortBinding>>();

    [JsonPropertyName("PublishAllPorts")]
    public bool PublishAllPorts { get; init; }
}
