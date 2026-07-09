using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Tests;

public sealed class RyukCreateRequestMutatorTests
{
    [Fact]
    public void MutateIfRyuk_rewrites_socket_bind_and_injects_docker_host()
    {
        var request = new DockerContainerCreateRequest
        {
            Image = "testcontainers/ryuk:0.12.0",
            Name = "testcontainers-ryuk-session-a",
            Env = ["RYUK_CONNECTION_TIMEOUT=30s"],
            ExposedPorts = new Dictionary<string, object?> { ["8080/tcp"] = new() },
            HostConfig = new DockerHostConfig
            {
                Binds =
                [
                    "/var/run/docker.sock:/var/run/docker.sock:ro",
                    "named-volume:/data"
                ],
                PortBindings = new Dictionary<string, IReadOnlyList<DockerPortBinding>>
                {
                    ["8080/tcp"] = [new DockerPortBinding("127.0.0.1", "49153")]
                }
            }
        };

        var result = RyukCreateRequestMutator.MutateIfRyuk(
            request,
            new RyukListenerEndpoint("172.28.48.1", 49152));

        Assert.True(result.IsRyuk);
        Assert.DoesNotContain(result.Request.HostConfig.Binds, bind => bind.Contains("/var/run/docker.sock"));
        Assert.Contains("named-volume:/data", result.Request.HostConfig.Binds);
        Assert.Contains("RYUK_CONNECTION_TIMEOUT=30s", result.Request.Env);
        Assert.Contains("DOCKER_HOST=tcp://172.28.48.1:49152", result.Request.Env);
        Assert.Contains("8080/tcp", result.Request.ExposedPorts.Keys);
        Assert.Contains("8080/tcp", result.Request.HostConfig.PortBindings.Keys);
    }

    [Fact]
    public void MutateIfRyuk_leaves_non_ryuk_requests_unchanged()
    {
        var request = new DockerContainerCreateRequest
        {
            Image = "redis:7",
            Name = "redis",
            Env = ["A=B"],
            HostConfig = new DockerHostConfig
            {
                Binds = ["/var/run/docker.sock:/var/run/docker.sock:ro"]
            }
        };

        var result = RyukCreateRequestMutator.MutateIfRyuk(
            request,
            new RyukListenerEndpoint("172.28.48.1", 49152));

        Assert.False(result.IsRyuk);
        Assert.Same(request, result.Request);
        Assert.Contains("/var/run/docker.sock:/var/run/docker.sock:ro", result.Request.HostConfig.Binds);
        Assert.DoesNotContain(result.Request.Env, value => value.StartsWith("DOCKER_HOST=", StringComparison.Ordinal));
    }
}
