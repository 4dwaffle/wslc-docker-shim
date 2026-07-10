using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Http.Endpoints.Enums;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Http.Endpoints.Images;

internal static class DeleteImageEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/images/{**id}", HandleAsync);
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
            DockerResourceKind.Image,
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

        await backend.DeleteResourceAsync(DockerResourceKind.Image, id, cancellationToken);
        return Results.Json(Array.Empty<object>());
    }
}
