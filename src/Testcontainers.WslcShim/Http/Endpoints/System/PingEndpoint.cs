using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Testcontainers.WslcShim.Http.Endpoints.System;

internal static class PingEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/_ping", Handle);
    }

    private static IResult Handle()
    {
        return Results.Text("OK");
    }
}
