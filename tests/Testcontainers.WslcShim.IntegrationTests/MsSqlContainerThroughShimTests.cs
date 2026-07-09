using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Testcontainers.WslcShim.IntegrationTests;

[Collection(WslcShimCollection.Name)]
public sealed class MsSqlContainerThroughShimTests(WslcShimFixture shim)
{
    [Fact]
    public async Task MsSqlContainer_starts_and_accepts_queries_through_the_shim()
    {
        await using (var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                         .WithPassword("Strong_password_123!")
                         .WithDockerEndpoint(shim.DockerEndpoint)
                         .Build())
        {
            await container.StartAsync();

            await using var connection = new SqlConnection(container.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 42";

            var result = await command.ExecuteScalarAsync();

            Assert.Equal(42, Convert.ToInt32(result));
        }

        var cleanup = await shim.RunRyukCleanupScenarioAsync();

        Assert.True(cleanup.RyukWasRunning);
        Assert.True(cleanup.MatchingResourceRemoved);
        Assert.True(cleanup.OtherSessionResourceSurvived);
        Assert.True(cleanup.UnlabelledResourceSurvived);
        Assert.True(cleanup.RyukRemoved);
    }
}
