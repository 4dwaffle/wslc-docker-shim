using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Http.Endpoints.Volumes;

internal static class ListVolumesEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/volumes", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        RyukCleanupSessionRegistry cleanupSessions,
        CancellationToken cancellationToken)
    {
        var filters = DockerLabelFilters.FromDockerFiltersQuery(context.Request.Query["filters"]);
        if (!RyukCleanupEndpointAuthorization.CanList(context, filters, listenerClassifier, cleanupSessions))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var resources = await backend.ListResourcesAsync(DockerResourceKind.Volume, filters, cancellationToken);
        return Results.Json(new
        {
            Volumes = resources.Select(resource => new
            {
                Name = resource.Name ?? resource.Id,
                CreatedAt = DockerEndpointTimestamp.Format(DockerEndpointTimestamp.GetCreationTime(resource)),
                resource.Labels
            }),
            Warnings = Array.Empty<string>()
        });
    }
}
