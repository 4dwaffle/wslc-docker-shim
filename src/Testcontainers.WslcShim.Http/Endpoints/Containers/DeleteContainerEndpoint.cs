using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Http.Endpoints.Enums;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Http.Endpoints.Containers;

internal static class DeleteContainerEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/containers/{id}", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string id,
        HttpContext context,
        IDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        RyukCleanupSessionRegistry cleanupSessions,
        CancellationToken cancellationToken)
    {
        var authorization = await RyukCleanupEndpointAuthorization.AuthorizeDeleteAsync(
            DockerResourceKind.Container,
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

        await backend.DeleteResourceAsync(DockerResourceKind.Container, id, cancellationToken);
        return Results.NoContent();
    }
}
