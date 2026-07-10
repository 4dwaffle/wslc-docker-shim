using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker.Models;

public sealed record DockerWaitContainerResponse(
    [property: JsonPropertyName("StatusCode")] long StatusCode,
    [property: JsonPropertyName("Error")] DockerWaitContainerError? Error = null);

public sealed record DockerWaitContainerError(
    [property: JsonPropertyName("Message")] string Message);
