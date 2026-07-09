using Testcontainers.WslcShim.Docker;

namespace Testcontainers.WslcShim.Tests;

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
    public void FromDockerFiltersQuery_returns_empty_filters_for_missing_value()
    {
        var filters = DockerLabelFilters.FromDockerFiltersQuery(null);

        Assert.Empty(filters.RequiredLabels);
    }
}
