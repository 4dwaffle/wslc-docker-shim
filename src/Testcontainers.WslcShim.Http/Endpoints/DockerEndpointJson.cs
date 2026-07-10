using System.Text.Json;

namespace Testcontainers.WslcShim.Http.Endpoints;

internal static class DockerEndpointJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null
    };
}
