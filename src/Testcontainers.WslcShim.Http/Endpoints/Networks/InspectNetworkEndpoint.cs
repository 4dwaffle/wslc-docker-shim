using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;

namespace Testcontainers.WslcShim.Http.Endpoints.Networks;

internal static class InspectNetworkEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/networks/{id}", HandleAsync);
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

        var json = await backend.InspectResourceJsonAsync(DockerResourceKind.Network, id, cancellationToken);
        return json is null
            ? Results.NotFound()
            : Results.Text(json, "application/json");
    }
}
