using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.Http.Endpoints.Networks;

internal static class CreateNetworkEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/networks/create", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (EndpointListenerAccess.IsRyuk(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var request = await context.Request.ReadFromJsonAsync<DockerResourceCreateRequest>(
            cancellationToken: cancellationToken) ?? new DockerResourceCreateRequest();
        var resource = await backend.CreateResourceAsync(DockerResourceKind.Network, request, cancellationToken);
        return Results.Json(
            new { resource.Id, Warning = string.Empty },
            statusCode: StatusCodes.Status201Created);
    }
}
