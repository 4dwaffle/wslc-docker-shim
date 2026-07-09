using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Tests;

public sealed class RestrictedRyukCleanupPolicyTests
{
    [Fact]
    public void CanDelete_allows_matching_testcontainers_session_resource()
    {
        var resource = new DockerResourceSnapshot(
            "container-1",
            new Dictionary<string, string>
            {
                ["org.testcontainers"] = "true",
                ["org.testcontainers.session-id"] = "session-a"
            });
        var filters = DockerLabelFilters.FromDockerFiltersQuery(
            """{"label":["org.testcontainers=true","org.testcontainers.session-id=session-a"]}""");

        Assert.True(RestrictedRyukCleanupPolicy.CanDelete(resource, filters));
    }

    [Fact]
    public void CanDelete_refuses_unlabelled_resources()
    {
        var resource = new DockerResourceSnapshot("container-1", new Dictionary<string, string>());
        var filters = DockerLabelFilters.FromDockerFiltersQuery(
            """{"label":["org.testcontainers=true","org.testcontainers.session-id=session-a"]}""");

        Assert.False(RestrictedRyukCleanupPolicy.CanDelete(resource, filters));
    }

    [Fact]
    public void CanDelete_refuses_resources_from_other_sessions()
    {
        var resource = new DockerResourceSnapshot(
            "container-1",
            new Dictionary<string, string>
            {
                ["org.testcontainers"] = "true",
                ["org.testcontainers.session-id"] = "session-b"
            });
        var filters = DockerLabelFilters.FromDockerFiltersQuery(
            """{"label":["org.testcontainers=true","org.testcontainers.session-id=session-a"]}""");

        Assert.False(RestrictedRyukCleanupPolicy.CanDelete(resource, filters));
    }
}
