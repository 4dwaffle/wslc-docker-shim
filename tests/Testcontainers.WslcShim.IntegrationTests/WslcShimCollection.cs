namespace Testcontainers.WslcShim.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class WslcShimCollection : ICollectionFixture<WslcShimFixture>
{
    public const string Name = "WSLc shim";
}
