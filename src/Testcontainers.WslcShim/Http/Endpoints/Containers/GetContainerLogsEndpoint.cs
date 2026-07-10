using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.Http.Endpoints.Containers;

internal static class GetContainerLogsEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/containers/{id}/logs", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string id,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (EndpointListenerAccess.IsRyuk(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var request = new DockerLogRequest(
            Follow: string.Equals(context.Request.Query["follow"], "1", StringComparison.Ordinal) ||
                    string.Equals(context.Request.Query["follow"], "true", StringComparison.OrdinalIgnoreCase),
            Timestamps: string.Equals(context.Request.Query["timestamps"], "1", StringComparison.Ordinal) ||
                        string.Equals(context.Request.Query["timestamps"], "true", StringComparison.OrdinalIgnoreCase),
            Tail: context.Request.Query["tail"]);
        var logs = await backend.GetContainerLogsAsync(id, request, cancellationToken);
        var bytes = DockerRawStream.FromStdout(logs);
        context.Response.ContentType = "application/vnd.docker.raw-stream";
        await context.Response.Body.WriteAsync(bytes, cancellationToken);
        return Results.Empty;
    }
}
