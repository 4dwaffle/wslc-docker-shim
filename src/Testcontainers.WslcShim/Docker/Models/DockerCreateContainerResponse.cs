using System.Text.Json.Serialization;

namespace Testcontainers.WslcShim.Docker.Models;

public sealed record DockerCreateContainerResponse(
    [property: JsonPropertyName("Id")] string Id,
    [property: JsonPropertyName("Warnings")] IReadOnlyList<string>? Warnings = null);
