using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.Docker.Tests;

public sealed class DockerLabelFilterTests
{
    [Fact]
    public void FromDockerFiltersQuery_parses_label_filters()
    {
        var filters = DockerLabelFilters.FromDockerFiltersQuery(
            """{"label":["org.testcontainers=true","org.testcontainers.session-id=abc123","keep"]}""");

        Assert.True(filters.RequiresLabel("org.testcontainers", "true"));
        Assert.Equal("abc123", filters.GetRequiredLabelValue("org.testcontainers.session-id"));
        Assert.True(filters.RequiresLabel("keep"));
    }

    [Fact]
    public void FromDockerFiltersQuery_parses_modern_docker_filter_set()
    {
        var filters = DockerLabelFilters.FromDockerFiltersQuery(
            """{"label":{"org.testcontainers.resource-reaper-session=abc123":true,"keep":true,"ignored":false}}""");

        Assert.Equal(
            "abc123",
            filters.GetRequiredLabelValue("org.testcontainers.resource-reaper-session"));
        Assert.True(filters.RequiresLabel("keep"));
        Assert.False(filters.RequiresLabel("ignored"));
    }

    [Fact]
    public void FromDockerFiltersQuery_returns_empty_filters_for_missing_value()
    {
        var filters = DockerLabelFilters.FromDockerFiltersQuery(null);

        Assert.Empty(filters.RequiredLabels);
    }
}
