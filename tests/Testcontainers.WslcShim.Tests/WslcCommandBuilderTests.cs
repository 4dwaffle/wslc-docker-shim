using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Wslc;

namespace Testcontainers.WslcShim.Tests;

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
    public void BuildStartContainerCommand_maps_to_wslc_start()
    {
        var command = WslcCommandBuilder.BuildStartContainerCommand("container-1");

        Assert.Equal("wslc", command.FileName);
        Assert.Equal(["start", "container-1"], command.Arguments);
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
