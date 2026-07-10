using Microsoft.AspNetCore.Http;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Http.Endpoints.Enums;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Http.Endpoints;

internal static class RyukCleanupEndpointAuthorization
{
    public static bool CanList(
        HttpContext context,
        DockerLabelFilters filters,
        IShimListenerClassifier listenerClassifier,
        RyukCleanupSessionRegistry cleanupSessions)
    {
        return !EndpointListenerAccess.IsRyuk(context, listenerClassifier) ||
               RestrictedRyukCleanupPolicy.CanList(filters) && cleanupSessions.TryActivate(context, filters);
    }

    public static async Task<RyukDeleteAuthorization> AuthorizeDeleteAsync(
        DockerResourceKind kind,
        string id,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        RyukCleanupSessionRegistry cleanupSessions,
        CancellationToken cancellationToken)
    {
        if (!EndpointListenerAccess.IsRyuk(context, listenerClassifier))
        {
            return RyukDeleteAuthorization.Allowed;
        }

        var resource = await backend.InspectResourceAsync(kind, id, cancellationToken);
        if (resource is null)
        {
            return RyukDeleteAuthorization.NotFound;
        }

        return cleanupSessions.TryGetActiveSession(context, out var activeSession) &&
               RestrictedRyukCleanupPolicy.CanDelete(resource, activeSession)
            ? RyukDeleteAuthorization.Allowed
            : RyukDeleteAuthorization.Forbidden;
    }
}
