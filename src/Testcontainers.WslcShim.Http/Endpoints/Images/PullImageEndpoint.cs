using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using Testcontainers.WslcShim.Docker;

namespace Testcontainers.WslcShim.Http.Endpoints.Images;

internal static class PullImageEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/images/create", HandleAsync);
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

        var fromImage = context.Request.Query["fromImage"].ToString();
        if (string.IsNullOrWhiteSpace(fromImage))
        {
            return Results.BadRequest();
        }

        var tag = context.Request.Query["tag"].ToString();
        var image = string.IsNullOrWhiteSpace(tag) ? fromImage : $"{fromImage}:{tag}";
        await backend.PullImageAsync(image, cancellationToken);
        var status = JsonSerializer.Serialize(new { status = $"Pulled {image}" }) + "\n";
        return Results.Text(status, "application/json");
    }
}
