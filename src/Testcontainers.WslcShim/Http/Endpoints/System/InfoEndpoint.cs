using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Testcontainers.WslcShim.Http.Endpoints.System;

internal static class InfoEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/info", Handle);
    }

    private static IResult Handle(
        HttpContext context,
        IShimListenerClassifier listenerClassifier)
    {
        if (EndpointListenerAccess.IsRyuk(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        return Results.Json(new
        {
            ID = "wslc-docker-shim",
            OSType = "linux",
            Architecture = "x86_64",
            OperatingSystem = "WSLc",
            ServerVersion = "wslc-docker-shim"
        }, DockerEndpointJson.Options);
    }
}
