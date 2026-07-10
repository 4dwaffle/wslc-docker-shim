using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Tests;

public sealed class RestrictedRyukCleanupPolicyTests
{
    private const string SessionA = "11111111-1111-1111-1111-111111111111";
    private const string SessionB = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public void CanList_allows_resource_reaper_session_filter_without_legacy_labels()
    {
        var filters = DockerLabelFilters.FromDockerFiltersQuery(
            $$$"""{"label":{"org.testcontainers.resource-reaper-session={{{SessionA}}}":true}}""");

        Assert.True(RestrictedRyukCleanupPolicy.CanList(filters));
    }

    [Fact]
    public void CanDelete_allows_matching_resource_reaper_session_resource()
    {
        var resource = new DockerResourceSnapshot(
            "container-1",
            new Dictionary<string, string>
            {
                ["org.testcontainers.resource-reaper-session"] = SessionA
            });

        Assert.True(RestrictedRyukCleanupPolicy.CanDelete(resource, SessionA));
    }

    [Fact]
    public void CanDelete_refuses_unlabelled_resources()
    {
        var resource = new DockerResourceSnapshot("container-1", new Dictionary<string, string>());
        Assert.False(RestrictedRyukCleanupPolicy.CanDelete(resource, SessionA));
    }

    [Fact]
    public void CanDelete_refuses_resources_from_other_sessions()
    {
        var resource = new DockerResourceSnapshot(
            "container-1",
            new Dictionary<string, string>
            {
                ["org.testcontainers.resource-reaper-session"] = SessionB
            });

        Assert.False(RestrictedRyukCleanupPolicy.CanDelete(resource, SessionA));
    }
}
