using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Http.Endpoints.Containers;

internal static class ListContainersEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/containers/json", HandleAsync);
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

        var resources = await backend.ListResourcesAsync(DockerResourceKind.Container, filters, cancellationToken);
        return Results.Json(resources.Select(resource => new
        {
            resource.Id,
            Names = new[] { "/" + (resource.Name ?? resource.Id) },
            Created = DockerEndpointTimestamp.GetCreationTime(resource).ToUnixTimeSeconds(),
            resource.Labels
        }));
    }
}
