using Testcontainers.WslcShim.Docker;

namespace Testcontainers.WslcShim.Ryuk;

public static class RestrictedRyukCleanupPolicy
{
    private const string TestcontainersLabel = "org.testcontainers";
    private const string TestcontainersSessionLabel = "org.testcontainers.session-id";

    public static bool CanList(DockerLabelFilters filters)
    {
        return filters.RequiresLabel(TestcontainersLabel, "true") &&
               !string.IsNullOrWhiteSpace(filters.GetRequiredLabelValue(TestcontainersSessionLabel));
    }

    public static bool CanDelete(DockerResourceSnapshot resource, DockerLabelFilters filters)
    {
        if (!CanList(filters))
        {
            return false;
        }

        var requestedSession = filters.GetRequiredLabelValue(TestcontainersSessionLabel);
        return resource.Labels.TryGetValue(TestcontainersLabel, out var testcontainers) &&
               string.Equals(testcontainers, "true", StringComparison.Ordinal) &&
               resource.Labels.TryGetValue(TestcontainersSessionLabel, out var resourceSession) &&
               string.Equals(resourceSession, requestedSession, StringComparison.Ordinal);
    }
}
