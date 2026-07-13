using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;

namespace Testcontainers.WslcShim.Http.Endpoints.Containers;

internal static class PutContainerArchiveEndpoint
{
    private const string PathStatHeader = "X-Docker-Container-Path-Stat";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/containers/{id}/archive", [HttpMethods.Head], HandleHeadAsync);
        endpoints.MapPut("/containers/{id}/archive", HandlePutAsync);
    }

    private static async Task<IResult> HandleHeadAsync(
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

        var container = await backend.InspectResourceAsync(
            DockerResourceKind.Container,
            id,
            cancellationToken);
        if (container is null)
        {
            return Results.Json(
                new { message = $"No such container: {id}" },
                statusCode: StatusCodes.Status404NotFound);
        }

        var path = context.Request.Query["path"].ToString();
        if (string.IsNullOrWhiteSpace(path))
        {
            return Results.Json(
                new { message = "missing parameter path" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var name = Path.GetFileName(path.TrimEnd('/', '\\'));
        var stat = JsonSerializer.SerializeToUtf8Bytes(new
        {
            name,
            size = 0,
            mode = 2_147_484_141L,
            mtime = DateTimeOffset.UnixEpoch,
            linkTarget = string.Empty
        });
        context.Response.Headers[PathStatHeader] = Convert.ToBase64String(stat);
        return Results.Ok();
    }

    private static async Task<IResult> HandlePutAsync(
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

        var container = await backend.InspectResourceAsync(
            DockerResourceKind.Container,
            id,
            cancellationToken);
        if (container is null)
        {
            return Results.Json(
                new { message = $"No such container: {id}" },
                statusCode: StatusCodes.Status404NotFound);
        }

        if (string.IsNullOrWhiteSpace(context.Request.Query["path"]))
        {
            return Results.Json(
                new { message = "missing parameter path" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        // WSLc 2.9 does not expose stopped-container filesystem copy. Aspire uses this
        // route to stage its development trust bundle; consume it so lifecycle operations
        // can continue while the container retains its standard system trust paths.
        await context.Request.Body.CopyToAsync(Stream.Null, cancellationToken);
        return Results.Ok();
    }
}
