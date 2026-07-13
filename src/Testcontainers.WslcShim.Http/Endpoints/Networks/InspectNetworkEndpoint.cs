using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;

namespace Testcontainers.WslcShim.Http.Endpoints.Networks;

internal static class InspectNetworkEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/networks/{id}", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string id,
        HttpContext context,
        IDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        DockerNetworkAttachmentStore attachments,
        CancellationToken cancellationToken)
    {
        if (EndpointListenerAccess.IsRyuk(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        if (string.Equals(id, "bridge", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(id, "aspire-container-network", StringComparison.OrdinalIgnoreCase))
        {
            var networkJson =
                $$"""
                {
                  "Name": "{{id}}",
                  "Id": "{{id}}",
                  "Created": "1970-01-01T00:00:00Z",
                  "Scope": "local",
                  "Driver": "bridge",
                  "EnableIPv6": false,
                  "IPAM": {
                    "Driver": "default",
                    "Options": null,
                    "Config": []
                  },
                  "Internal": false,
                  "Attachable": false,
                  "Ingress": false,
                  "ConfigFrom": {
                    "Network": ""
                  },
                  "ConfigOnly": false,
                  "Containers": {},
                  "Options": {},
                  "Labels": {}
                }
                """;
            return Results.Text(
                attachments.ApplyToNetworkInspect(id, networkJson),
                "application/json");
        }

        var json = await backend.InspectResourceJsonAsync(DockerResourceKind.Network, id, cancellationToken);
        return json is null
            ? Results.Json(
                new { message = $"network {id} not found" },
                statusCode: StatusCodes.Status404NotFound)
            : Results.Text(attachments.ApplyToNetworkInspect(id, json), "application/json");
    }
}
