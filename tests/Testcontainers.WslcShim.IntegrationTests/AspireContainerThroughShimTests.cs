namespace Testcontainers.WslcShim.IntegrationTests;

[Collection(WslcShimCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AspireContainerThroughShimTests(WslcShimFixture shim)
{
    [Fact]
    public async Task Aspire_shaped_container_pulls_starts_and_stops_through_the_shim()
    {
        var result = await shim.RunAspireLifecycleScenarioAsync();

        Assert.True(result.BridgeHidden);
        Assert.True(result.AspireNetworkVisible);
        Assert.True(result.NetworkListsContainer);
        Assert.True(result.ContainerStopped);
    }
}
