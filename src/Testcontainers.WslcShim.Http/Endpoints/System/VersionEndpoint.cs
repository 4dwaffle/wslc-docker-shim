using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Testcontainers.WslcShim.Http.Endpoints.System;

internal static class VersionEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/version", Handle);
    }

    private static IResult Handle()
    {
        return Results.Json(new
        {
            Version = "wslc-docker-shim",
            ApiVersion = "1.43",
            MinAPIVersion = "1.24",
            Os = "linux",
            Arch = "amd64"
        }, DockerEndpointJson.Options);
    }
}
