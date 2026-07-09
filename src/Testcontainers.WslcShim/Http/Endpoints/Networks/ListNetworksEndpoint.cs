using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Http.Endpoints.Networks;

internal static class ListNetworksEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/networks", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        RyukCleanupSessionRegistry cleanupSessions,
        CancellationToken cancellationToken)
    {
        var filters = DockerLabelFilters.FromDockerFiltersQuery(context.Request.Query["filters"]);
        if (!RyukCleanupEndpointAuthorization.CanList(context, filters, listenerClassifier, cleanupSessions))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var resources = await backend.ListResourcesAsync(DockerResourceKind.Network, filters, cancellationToken);
        return Results.Json(resources.Select(resource => new
        {
            resource.Id,
            Name = resource.Name ?? resource.Id,
            Created = DockerEndpointTimestamp.Format(DockerEndpointTimestamp.GetCreationTime(resource)),
            resource.Labels
        }));
    }
}
