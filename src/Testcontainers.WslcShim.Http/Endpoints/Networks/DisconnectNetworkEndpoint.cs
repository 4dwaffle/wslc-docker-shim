using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.Http.Endpoints.Networks;

internal static class DisconnectNetworkEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/networks/{id}/disconnect", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string id,
        HttpContext context,
        IDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        DockerNetworkAttachmentStore attachments,
        CancellationToken cancellationToken)
    {
        if (EndpointListenerAccess.IsRyuk(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var request = await context.Request.ReadFromJsonAsync<DockerNetworkDisconnectRequest>(
            cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(request?.Container))
        {
            return Results.BadRequest();
        }

        var container = await backend.InspectResourceAsync(
            DockerResourceKind.Container,
            request.Container,
            cancellationToken);
        if (container is null)
        {
            return Results.NotFound();
        }

        attachments.Disconnect(container, id);
        return Results.Ok();
    }
}
