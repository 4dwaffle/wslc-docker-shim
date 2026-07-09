using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker;

public sealed class DockerExecCreateRequest
{
    [JsonPropertyName("Cmd")]
    public IReadOnlyList<string> Cmd { get; init; } = [];

    [JsonPropertyName("Env")]
    public IReadOnlyList<string> Env { get; init; } = [];

    [JsonPropertyName("User")]
    public string? User { get; init; }

    [JsonPropertyName("WorkingDir")]
    public string? WorkingDir { get; init; }

    [JsonPropertyName("Tty")]
    public bool Tty { get; init; }

    [JsonPropertyName("AttachStdin")]
    public bool AttachStdin { get; init; }
}

public sealed record DockerExecCreateResponse(
    [property: JsonPropertyName("Id")] string Id);

public sealed class DockerExecStartRequest
{
    [JsonPropertyName("Detach")]
    public bool Detach { get; init; }

    [JsonPropertyName("Tty")]
    public bool Tty { get; init; }
}

public sealed record DockerExecStartResponse(string Output, long ExitCode);

public sealed record DockerExecInspectResponse(
    [property: JsonPropertyName("ID")] string Id,
    [property: JsonPropertyName("Running")] bool Running,
    [property: JsonPropertyName("ExitCode")] long? ExitCode);
