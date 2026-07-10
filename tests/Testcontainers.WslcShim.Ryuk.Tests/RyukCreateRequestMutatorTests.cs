using System.Text.Json;
using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Ryuk;
using Testcontainers.WslcShim.Ryuk.Models;
using Testcontainers.WslcShim.Wslc;

namespace Testcontainers.WslcShim.Ryuk.Tests;

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
                Privileged = true,
                Binds =
                [
                    "/var/run/docker.sock:/var/run/docker.sock:ro",
                    "named-volume:/data"
                ],
                PortBindings = new Dictionary<string, IReadOnlyList<DockerPortBinding>>
                {
                    ["8080/tcp"] = [new DockerPortBinding("127.0.0.1", "49153")]
                },
                Mounts =
                [
                    new DockerMount
                    {
                        Type = "bind",
                        Source = "/var/run/docker.sock",
                        Target = "/var/run/docker.sock"
                    }
                ]
            }
        };

        var result = RyukCreateRequestMutator.MutateIfRyuk(
            request,
            new RyukListenerEndpoint("172.28.48.1", 49152));

        Assert.True(result.IsRyuk);
        Assert.DoesNotContain(result.Request.HostConfig.Binds, bind => bind.Contains("/var/run/docker.sock"));
        Assert.Contains("named-volume:/data", result.Request.HostConfig.Binds);
        Assert.Empty(result.Request.HostConfig.Mounts);
        Assert.False(result.Request.HostConfig.Privileged);
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

    [Fact]
    public void MutateIfRyuk_normalizes_privileged_socket_mount_from_testcontainers_payload()
    {
        var request = JsonSerializer.Deserialize<DockerContainerCreateRequest>(
            """
            {
              "Image": "testcontainers/ryuk:0.12.0",
              "HostConfig": {
                "Privileged": true,
                "Mounts": [
                  {
                    "Type": "bind",
                    "Source": "/var/run/docker.sock",
                    "Target": "/var/run/docker.sock",
                    "ReadOnly": false,
                    "BindOptions": {
                      "Propagation": ""
                    }
                  },
                  {
                    "Type": "volume",
                    "Source": "ryuk-data",
                    "Target": "/data",
                    "ReadOnly": true
                  }
                ]
              }
            }
            """)!;

        var result = RyukCreateRequestMutator.MutateIfRyuk(
            request,
            new RyukListenerEndpoint("172.28.48.1", 49152));

        Assert.True(result.IsRyuk);
        Assert.False(result.Request.HostConfig.Privileged);
        var mount = Assert.Single(result.Request.HostConfig.Mounts);
        Assert.Equal("ryuk-data", mount.Source);
        Assert.Equal("/data", mount.Target);
        WslcCreateRequestCompatibility.Validate(result.Request);
    }
}
