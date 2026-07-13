using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;

namespace Testcontainers.WslcShim.Http.Endpoints.Volumes;

internal static class InspectVolumeEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/volumes/{id}", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string id,
        HttpContext context,
        IDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (EndpointListenerAccess.IsRyuk(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var json = await backend.InspectResourceJsonAsync(DockerResourceKind.Volume, id, cancellationToken);
        return json is null
            ? Results.Json(
                new { message = $"get {id}: no such volume" },
                statusCode: StatusCodes.Status404NotFound)
            : Results.Text(json, "application/json");
    }
}
