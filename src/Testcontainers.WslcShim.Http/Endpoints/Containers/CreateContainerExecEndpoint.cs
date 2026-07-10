using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.Http.Endpoints.Containers;

internal static class CreateContainerExecEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/containers/{id}/exec", HandleAsync);
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

        var request = await context.Request.ReadFromJsonAsync<DockerExecCreateRequest>(
            cancellationToken: cancellationToken);
        if (request is null)
        {
            return Results.BadRequest();
        }

        var response = await backend.CreateContainerExecAsync(id, request, cancellationToken);
        return Results.Json(response, statusCode: StatusCodes.Status201Created);
    }
}
