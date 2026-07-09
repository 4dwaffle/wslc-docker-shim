using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Http.Endpoints.Networks;

internal static class DeleteNetworkEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/networks/{id}", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string id,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        RyukCleanupSessionRegistry cleanupSessions,
        CancellationToken cancellationToken)
    {
        var authorization = await RyukCleanupEndpointAuthorization.AuthorizeDeleteAsync(
            DockerResourceKind.Network,
            id,
            context,
            backend,
            listenerClassifier,
            cleanupSessions,
            cancellationToken);
        if (authorization == RyukDeleteAuthorization.NotFound)
        {
            return Results.NotFound();
        }

        if (authorization == RyukDeleteAuthorization.Forbidden)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        await backend.DeleteResourceAsync(DockerResourceKind.Network, id, cancellationToken);
        return Results.NoContent();
    }
}
