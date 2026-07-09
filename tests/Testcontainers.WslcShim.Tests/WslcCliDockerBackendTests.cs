using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Wslc;
using System.Text.Json;

namespace Testcontainers.WslcShim.Tests;

public sealed class WslcCliDockerBackendTests
{
    [Fact]
    public async Task InspectResourceJsonAsync_moves_container_ports_to_network_settings()
    {
        var runner = new RecordingWslcProcessRunner
        {
            Result = new WslcCommandResult(
                0,
                """
                [
                  {
                    "Id": "container-1",
                    "Ports": {
                      "8080/tcp": [
                        {
                          "HostIp": "127.0.0.1",
                          "HostPort": "49153"
                        }
                      ]
                    },
                    "NetworkSettings": {
                      "Networks": {}
                    }
                  }
                ]
                """,
                string.Empty)
        };
        var backend = new WslcCliDockerBackend(runner);

        var json = await backend.InspectResourceJsonAsync(DockerResourceKind.Container, "container-1", CancellationToken.None);

        using var document = JsonDocument.Parse(json!);
        var networkPorts = document.RootElement
            .GetProperty("NetworkSettings")
            .GetProperty("Ports")
            .GetProperty("8080/tcp");
        Assert.Equal("49153", networkPorts[0].GetProperty("HostPort").GetString());
    }

    [Fact]
    public async Task WaitContainerAsync_returns_exit_code_from_container_state()
    {
        var runner = new RecordingWslcProcessRunner
        {
            Result = new WslcCommandResult(
                0,
                """
                [
                  {
                    "Id": "container-1",
                    "State": {
                      "ExitCode": 143,
                      "Running": false,
                      "Status": "exited"
                    }
                  }
                ]
                """,
                string.Empty)
        };
        var backend = new WslcCliDockerBackend(runner);

        var response = await backend.WaitContainerAsync("container-1", CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(143, response.StatusCode);
    }

    [Fact]
    public async Task WaitContainerAsync_polls_until_the_container_exits()
    {
        var runner = new RecordingWslcProcessRunner
        {
            Results =
            [
                new WslcCommandResult(
                    0,
                    """
                    [
                      {
                        "Id": "container-1",
                        "State": {
                          "ExitCode": 0,
                          "Running": true,
                          "Status": "running"
                        }
                      }
                    ]
                    """,
                    string.Empty),
                new WslcCommandResult(
                    0,
                    """
                    [
                      {
                        "Id": "container-1",
                        "State": {
                          "ExitCode": 17,
                          "Running": false,
                          "Status": "exited"
                        }
                      }
                    ]
                    """,
                    string.Empty)
            ]
        };
        var backend = new WslcCliDockerBackend(runner);

        var response = await backend.WaitContainerAsync("container-1", CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(17, response.StatusCode);
        Assert.True(runner.Commands.Count >= 2);
    }

    [Fact]
    public async Task WaitContainerAsync_honors_cancellation_while_container_is_running()
    {
        var runner = new RecordingWslcProcessRunner
        {
            Result = new WslcCommandResult(
                0,
                """
                [
                  {
                    "Id": "container-1",
                    "State": {
                      "ExitCode": 0,
                      "Running": true,
                      "Status": "running"
                    }
                  }
                ]
                """,
                string.Empty)
        };
        var backend = new WslcCliDockerBackend(runner);
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => backend.WaitContainerAsync("container-1", cancellationTokenSource.Token));
    }

    private sealed class RecordingWslcProcessRunner : IWslcProcessRunner
    {
        public WslcCommandResult Result { get; init; } = new(0, string.Empty, string.Empty);

        public IReadOnlyList<WslcCommandResult> Results { get; init; } = [];

        public List<WslcCommand> Commands { get; } = [];

        public Task<WslcCommandResult> RunAsync(WslcCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            var result = Commands.Count <= Results.Count
                ? Results[Commands.Count - 1]
                : Result;
            return Task.FromResult(result);
        }
    }
}
