using System.Text.Json;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Docker.Exceptions;
using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Wslc;

namespace Testcontainers.WslcShim.Wslc.Tests;

public sealed class WslcCommandBuilderTests
{
    [Fact]
    public void BuildCreateContainerCommand_maps_docker_create_request_to_wslc_arguments()
    {
        var request = new DockerContainerCreateRequest
        {
            Image = "redis:7",
            Name = "redis-test",
            Env = ["A=B", "C=D"],
            Labels = new Dictionary<string, string>
            {
                ["org.testcontainers"] = "true",
                ["org.testcontainers.session-id"] = "session-a"
            },
            Cmd = ["redis-server", "--appendonly", "yes"],
            HostConfig = new DockerHostConfig
            {
                Binds = ["named-volume:/data"],
                PortBindings = new Dictionary<string, IReadOnlyList<DockerPortBinding>>
                {
                    ["6379/tcp"] = [new DockerPortBinding("127.0.0.1", "49154")]
                }
            }
        };

        var command = WslcCommandBuilder.BuildCreateContainerCommand(request, "A:\\temp\\cid.txt");

        Assert.Equal("wslc", command.FileName);
        Assert.Equal(
            [
                "create",
                "--cidfile",
                "A:\\temp\\cid.txt",
                "--name",
                "redis-test",
                "--env",
                "A=B",
                "--env",
                "C=D",
                "--label",
                "org.testcontainers=true",
                "--label",
                "org.testcontainers.session-id=session-a",
                "--volume",
                "named-volume:/data",
                "--publish",
                "127.0.0.1:49154:6379/tcp",
                "redis:7",
                "redis-server",
                "--appendonly",
                "yes"
            ],
            command.Arguments);
    }

    [Fact]
    public void BuildCreateContainerCommand_maps_publish_all_ports()
    {
        var request = new DockerContainerCreateRequest
        {
            Image = "testcontainers/ryuk:0.14.0",
            HostConfig = new DockerHostConfig
            {
                PublishAllPorts = true
            }
        };

        var command = WslcCommandBuilder.BuildCreateContainerCommand(request, "A:\\temp\\cid.txt");

        Assert.Contains("--publish-all", command.Arguments);
    }

    [Fact]
    public void BuildCreateContainerCommand_maps_empty_host_port_to_random_publish()
    {
        var request = new DockerContainerCreateRequest
        {
            Image = "testcontainers/ryuk:0.14.0",
            HostConfig = new DockerHostConfig
            {
                PortBindings = new Dictionary<string, IReadOnlyList<DockerPortBinding>>
                {
                    ["8080/tcp"] = [new DockerPortBinding(string.Empty, string.Empty)]
                }
            }
        };

        var command = WslcCommandBuilder.BuildCreateContainerCommand(request, "A:\\temp\\cid.txt");

        Assert.Contains("--publish", command.Arguments);
        Assert.Contains("8080/tcp", command.Arguments);
    }

    [Fact]
    public void BuildCreateContainerCommand_maps_auto_remove_network_and_aliases()
    {
        var request = new DockerContainerCreateRequest
        {
            Image = "redis:7",
            NetworkingConfig = new DockerNetworkingConfig
            {
                EndpointsConfig = new Dictionary<string, DockerEndpointSettings>
                {
                    ["test-network"] = new()
                    {
                        Aliases = ["cache", "redis"]
                    }
                }
            },
            HostConfig = new DockerHostConfig
            {
                AutoRemove = true,
                NetworkMode = "test-network"
            }
        };

        var command = WslcCommandBuilder.BuildCreateContainerCommand(request, "A:\\temp\\cid.txt");

        Assert.Equal(
            [
                "create",
                "--cidfile",
                "A:\\temp\\cid.txt",
                "--network",
                "test-network",
                "--network-alias",
                "cache",
                "--network-alias",
                "redis",
                "--rm",
                "redis:7"
            ],
            command.Arguments);
    }

    [Fact]
    public void BuildCreateContainerCommand_maps_entrypoint_identity_terminal_and_resource_settings()
    {
        var request = new DockerContainerCreateRequest
        {
            Image = "alpine:3.20",
            Hostname = "worker-1",
            Domainname = "example.test",
            User = "1000:1000",
            WorkingDir = "/workspace",
            Entrypoint = ["/bin/sh", "-c"],
            Cmd = ["echo ready"],
            Tty = true,
            OpenStdin = true,
            StopSignal = "SIGTERM",
            HostConfig = new DockerHostConfig
            {
                Memory = 512L * 1024 * 1024,
                ShmSize = 64L * 1024 * 1024,
                Tmpfs = new Dictionary<string, string>
                {
                    ["/tmp"] = "rw,noexec"
                }
            }
        };

        var command = WslcCommandBuilder.BuildCreateContainerCommand(request, "cid.txt");

        Assert.Equal(
            [
                "create", "--cidfile", "cid.txt",
                "--hostname", "worker-1",
                "--domainname", "example.test",
                "--user", "1000:1000",
                "--workdir", "/workspace",
                "--entrypoint", "/bin/sh",
                "--memory", "512M",
                "--shm-size", "64M",
                "--tmpfs", "/tmp:rw,noexec",
                "--interactive",
                "--tty",
                "--stop-signal", "SIGTERM",
                "alpine:3.20", "-c", "echo ready"
            ],
            command.Arguments);
    }

    [Fact]
    public void BuildCreateContainerCommand_maps_remaining_wslc_create_options()
    {
        var request = new DockerContainerCreateRequest
        {
            Image = "nvidia/cuda:latest",
            Volumes = new Dictionary<string, object?> { ["/cache"] = new() },
            HostConfig = new DockerHostConfig
            {
                Dns = ["1.1.1.1"],
                DnsOptions = ["ndots:1"],
                DnsSearch = ["example.test"],
                NanoCpus = 500_000_000,
                Ulimits = [new DockerUlimit { Name = "nofile", Soft = 1024, Hard = 2048 }],
                DeviceRequests =
                [
                    new DockerDeviceRequest
                    {
                        Driver = "nvidia",
                        Count = -1,
                        Capabilities = [["gpu"]]
                    }
                ]
            }
        };

        var command = WslcCommandBuilder.BuildCreateContainerCommand(request, "cid.txt");

        Assert.Equal(
            [
                "create", "--cidfile", "cid.txt",
                "--volume", "/cache",
                "--dns", "1.1.1.1",
                "--dns-option", "ndots:1",
                "--dns-search", "example.test",
                "--cpus", "0.5",
                "--ulimit", "nofile=1024:2048",
                "--gpus", "all",
                "nvidia/cuda:latest"
            ],
            command.Arguments);
    }

    [Fact]
    public void BuildCreateContainerCommand_maps_structured_bind_volume_and_tmpfs_mounts()
    {
        var request = new DockerContainerCreateRequest
        {
            Image = "alpine:3.20",
            HostConfig = new DockerHostConfig
            {
                Mounts =
                [
                    new DockerMount
                    {
                        Type = "bind",
                        Source = "C:\\work",
                        Target = "/workspace",
                        ReadOnly = true
                    },
                    new DockerMount
                    {
                        Type = "volume",
                        Source = "cache",
                        Target = "/cache"
                    },
                    new DockerMount
                    {
                        Type = "tmpfs",
                        Target = "/tmp",
                        ReadOnly = true,
                        TmpfsOptions = new DockerTmpfsOptions
                        {
                            SizeBytes = 64L * 1024 * 1024,
                            Mode = 493
                        }
                    }
                ]
            }
        };

        var command = WslcCommandBuilder.BuildCreateContainerCommand(request, "cid.txt");

        Assert.Equal(
            [
                "create", "--cidfile", "cid.txt",
                "--volume", "C:\\work:/workspace:ro",
                "--volume", "cache:/cache",
                "--tmpfs", "/tmp:ro,size=64M,mode=755",
                "alpine:3.20"
            ],
            command.Arguments);
    }

    [Fact]
    public void BuildCreateContainerCommand_rejects_docker_network_namespace_modes()
    {
        var request = new DockerContainerCreateRequest
        {
            Image = "alpine:3.20",
            HostConfig = new DockerHostConfig { NetworkMode = "host" }
        };

        var exception = Assert.Throws<UnsupportedDockerCreateOptionException>(
            () => WslcCommandBuilder.BuildCreateContainerCommand(request, "cid.txt"));

        Assert.Contains("HostConfig.NetworkMode (Docker namespace mode)", exception.OptionPaths);
    }

    [Fact]
    public void BuildCreateContainerCommand_treats_docker_bridge_as_wslc_default_network()
    {
        var command = WslcCommandBuilder.BuildCreateContainerCommand(
            new DockerContainerCreateRequest
            {
                Image = "alpine:3.20",
                HostConfig = new DockerHostConfig { NetworkMode = "bridge" }
            },
            "cid.txt");

        Assert.Equal(
            ["create", "--cidfile", "cid.txt", "alpine:3.20"],
            command.Arguments);
    }

    [Fact]
    public void BuildCreateContainerCommand_rejects_unrepresented_exposed_ports()
    {
        var request = new DockerContainerCreateRequest
        {
            Image = "alpine:3.20",
            ExposedPorts = new Dictionary<string, object?> { ["8080/tcp"] = new() }
        };

        var exception = Assert.Throws<UnsupportedDockerCreateOptionException>(
            () => WslcCommandBuilder.BuildCreateContainerCommand(request, "cid.txt"));

        Assert.Contains("ExposedPorts[8080/tcp] (no PortBinding or PublishAllPorts)", exception.OptionPaths);
    }

    [Fact]
    public void BuildCreateContainerCommand_rejects_meaningful_unsupported_docker_fields()
    {
        var request = JsonSerializer.Deserialize<DockerContainerCreateRequest>(
            """
            {
              "Image": "alpine:3.20",
              "Healthcheck": { "Test": ["CMD", "true"] },
              "HostConfig": {
                "Privileged": true,
                "MemorySwappiness": 50,
                "RestartPolicy": { "Name": "always", "MaximumRetryCount": 0 }
              }
            }
            """)!;

        var exception = Assert.Throws<UnsupportedDockerCreateOptionException>(
            () => WslcCommandBuilder.BuildCreateContainerCommand(request, "cid.txt"));

        Assert.Contains("Healthcheck", exception.OptionPaths);
        Assert.Contains("HostConfig.Privileged", exception.OptionPaths);
        Assert.Contains("HostConfig.MemorySwappiness", exception.OptionPaths);
        Assert.Contains("HostConfig.RestartPolicy", exception.OptionPaths);
    }

    [Fact]
    public void BuildCreateContainerCommand_accepts_default_values_for_unmapped_docker_fields()
    {
        var request = JsonSerializer.Deserialize<DockerContainerCreateRequest>(
            """
            {
              "Image": "alpine:3.20",
              "Env": null,
              "Labels": null,
              "Cmd": null,
              "Entrypoint": null,
              "ExposedPorts": null,
              "Volumes": null,
              "ArgsEscaped": false,
              "Healthcheck": null,
              "NetworkingConfig": {
                "EndpointsConfig": {
                  "default": {
                    "Aliases": null,
                    "DNSNames": null,
                    "IPAMConfig": null,
                    "Links": null
                  }
                }
              },
              "HostConfig": {
                "Binds": null,
                "PortBindings": null,
                "Dns": null,
                "DnsOptions": null,
                "DnsSearch": null,
                "Tmpfs": null,
                "Ulimits": null,
                "Mounts": null,
                "CpuShares": 0,
                "Privileged": false,
                "LogConfig": { "Type": "", "Config": {} },
                "MemorySwappiness": -1,
                "RestartPolicy": { "Name": "no", "MaximumRetryCount": 0 },
                "DeviceRequests": null
              }
            }
            """)!;

        var command = WslcCommandBuilder.BuildCreateContainerCommand(request, "cid.txt");

        Assert.Equal(["create", "--cidfile", "cid.txt", "alpine:3.20"], command.Arguments);
    }

    [Fact]
    public void BuildCreateContainerCommand_rejects_multiple_networks_instead_of_dropping_them()
    {
        var request = new DockerContainerCreateRequest
        {
            Image = "alpine:3.20",
            NetworkingConfig = new DockerNetworkingConfig
            {
                EndpointsConfig = new Dictionary<string, DockerEndpointSettings>
                {
                    ["network-a"] = new(),
                    ["network-b"] = new()
                }
            }
        };

        var exception = Assert.Throws<UnsupportedDockerCreateOptionException>(
            () => WslcCommandBuilder.BuildCreateContainerCommand(request, "cid.txt"));

        Assert.Contains("NetworkingConfig.EndpointsConfig (multiple networks)", exception.OptionPaths);
    }

    [Fact]
    public void BuildStartContainerCommand_maps_to_wslc_start()
    {
        var command = WslcCommandBuilder.BuildStartContainerCommand("container-1");

        Assert.Equal("wslc", command.FileName);
        Assert.Equal(["start", "container-1"], command.Arguments);
    }

    [Fact]
    public void BuildContainerStatsCommand_requests_one_json_snapshot_for_all_containers()
    {
        var command = WslcCommandBuilder.BuildContainerStatsCommand();

        Assert.Equal("wslc", command.FileName);
        Assert.Equal(["stats", "--all", "--format", "json"], command.Arguments);
    }

    [Fact]
    public void BuildLogsCommand_uses_finite_snapshot_even_when_docker_requests_follow()
    {
        var command = WslcCommandBuilder.BuildLogsCommand(
            "container-1",
            new DockerLogRequest(Follow: true, Timestamps: false, Tail: null));

        Assert.Equal("wslc", command.FileName);
        Assert.DoesNotContain("--follow", command.Arguments);
        Assert.Equal(["logs", "container-1"], command.Arguments);
    }

    [Fact]
    public void BuildExecCommand_maps_docker_exec_request_to_wslc_arguments()
    {
        var command = WslcCommandBuilder.BuildExecCommand(
            "container-1",
            new DockerExecCreateRequest
            {
                Cmd = ["printenv", "PATH"],
                Env = ["A=B"],
                User = "mssql",
                WorkingDir = "/tmp",
                Tty = true
            });

        Assert.Equal("wslc", command.FileName);
        Assert.Equal(
            [
                "exec",
                "--env",
                "A=B",
                "--user",
                "mssql",
                "--workdir",
                "/tmp",
                "--tty",
                "container-1",
                "printenv",
                "PATH"
            ],
            command.Arguments);
    }

    [Fact]
    public void BuildPullImageCommand_maps_repository_and_tag()
    {
        var command = WslcCommandBuilder.BuildPullImageCommand("redis:7");

        Assert.Equal("wslc", command.FileName);
        Assert.Equal(["pull", "redis:7"], command.Arguments);
    }

    [Theory]
    [InlineData(DockerResourceKind.Network, "network-a", "network", "create")]
    [InlineData(DockerResourceKind.Volume, "volume-a", "volume", "create")]
    public void BuildCreateResourceCommand_maps_named_resources(
        DockerResourceKind kind,
        string name,
        string commandGroup,
        string commandName)
    {
        var command = WslcCommandBuilder.BuildCreateResourceCommand(
            kind,
            new DockerResourceCreateRequest
            {
                Name = name,
                Labels = new Dictionary<string, string> { ["org.testcontainers"] = "true" }
            });

        Assert.Equal("wslc", command.FileName);
        Assert.Equal(
            [commandGroup, commandName, "--label", "org.testcontainers=true", name],
            command.Arguments);
    }
}
