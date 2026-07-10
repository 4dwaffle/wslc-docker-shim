using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.Ryuk;

public static class RestrictedRyukCleanupPolicy
{
    public const string ResourceReaperSessionLabel = "org.testcontainers.resource-reaper-session";

    public const string TestcontainersSessionLabel = "org.testcontainers.session-id";

    public static bool CanList(DockerLabelFilters filters)
    {
        return TryGetRequestedSession(filters, out _);
    }

    public static bool TryGetRequestedSession(DockerLabelFilters filters, out string session)
    {
        return TryNormalizeSession(filters.GetRequiredLabelValue(ResourceReaperSessionLabel), out session);
    }

    public static bool CanDelete(DockerResourceSnapshot resource, string? activeSession)
    {
        if (!TryNormalizeSession(activeSession, out var normalizedActiveSession) ||
            !resource.Labels.TryGetValue(ResourceReaperSessionLabel, out var resourceSession) ||
            !TryNormalizeSession(resourceSession, out var normalizedResourceSession))
        {
            return false;
        }

        return string.Equals(normalizedResourceSession, normalizedActiveSession, StringComparison.Ordinal);
    }

    public static bool TryNormalizeSession(string? value, out string session)
    {
        if (Guid.TryParse(value, out var sessionId) && sessionId != Guid.Empty)
        {
            session = sessionId.ToString("D");
            return true;
        }

        session = string.Empty;
        return false;
    }
}
