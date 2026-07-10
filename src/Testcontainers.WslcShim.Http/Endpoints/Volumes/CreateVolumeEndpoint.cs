using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.Http.Endpoints.Volumes;

internal static class CreateVolumeEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/volumes/create", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (EndpointListenerAccess.IsRyuk(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var request = await context.Request.ReadFromJsonAsync<DockerResourceCreateRequest>(
            cancellationToken: cancellationToken) ?? new DockerResourceCreateRequest();
        var resource = await backend.CreateResourceAsync(DockerResourceKind.Volume, request, cancellationToken);
        return Results.Json(
            new { Name = resource.Name ?? resource.Id },
            statusCode: StatusCodes.Status201Created);
    }
}
