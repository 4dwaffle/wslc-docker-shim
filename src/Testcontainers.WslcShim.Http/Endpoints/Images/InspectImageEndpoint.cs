using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;

namespace Testcontainers.WslcShim.Http.Endpoints.Images;

internal static class InspectImageEndpoint
{
    private const string InspectSuffix = "/json";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/images/{**id}", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string id,
        HttpContext context,
        IDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (!id.EndsWith(InspectSuffix, StringComparison.Ordinal) ||
            EndpointListenerAccess.IsRyuk(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var imageId = id[..^InspectSuffix.Length];
        var json = await backend.InspectResourceJsonAsync(DockerResourceKind.Image, imageId, cancellationToken);
        return json is null
            ? Results.NotFound()
            : Results.Text(json, "application/json");
    }
}
