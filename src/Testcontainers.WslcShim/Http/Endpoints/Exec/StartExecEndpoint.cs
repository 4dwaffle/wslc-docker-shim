using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;

namespace Testcontainers.WslcShim.Http.Endpoints.Exec;

internal static class StartExecEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/exec/{id}/start", HandleAsync);
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

        var request = await context.Request.ReadFromJsonAsync<DockerExecStartRequest>(
            cancellationToken: cancellationToken) ?? new DockerExecStartRequest();
        var response = await backend.StartExecAsync(id, request, cancellationToken);
        if (response is null)
        {
            return Results.NotFound();
        }

        var bytes = DockerRawStream.FromStdout(response.Output);
        var upgradeFeature = context.Features.Get<IHttpUpgradeFeature>();
        if (upgradeFeature?.IsUpgradableRequest == true)
        {
            await using var stream = await upgradeFeature.UpgradeAsync();
            await stream.WriteAsync(bytes, cancellationToken);
            return Results.Empty;
        }

        context.Response.StatusCode = StatusCodes.Status101SwitchingProtocols;
        context.Response.Headers.Connection = "Upgrade";
        context.Response.Headers.Upgrade = "tcp";
        context.Response.ContentType = "application/vnd.docker.raw-stream";
        await context.Response.Body.WriteAsync(bytes, cancellationToken);
        return Results.Empty;
    }
}
