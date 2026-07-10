using System.Text.Json;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Wslc;
using Testcontainers.WslcShim.Wslc.Models;

namespace Testcontainers.WslcShim.Tests;

public sealed class WslcCliDockerBackendTests
{
    [Fact]
    public async Task CreateContainerAsync_passes_supported_create_settings_to_wslc()
    {
        var runner = new RecordingWslcProcessRunner
        {
            Result = new WslcCommandResult(0, "container-1", string.Empty)
        };
        var backend = new WslcCliDockerBackend(runner);

        var response = await backend.CreateContainerAsync(
            new DockerContainerCreateRequest
            {
                Image = "alpine:3.20",
                User = "1000",
                WorkingDir = "/workspace",
                Entrypoint = ["/bin/sh"],
                HostConfig = new DockerHostConfig
                {
                    AutoRemove = true,
                    NetworkMode = "test-network"
                },
                NetworkingConfig = new DockerNetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, DockerEndpointSettings>
                    {
                        ["test-network"] = new() { Aliases = ["worker"] }
                    }
                }
            },
            isRyuk: false,
            CancellationToken.None);

        Assert.Equal("container-1", response.Id);
        var command = Assert.Single(runner.Commands);
        Assert.Contains("--rm", command.Arguments);
        Assert.Contains("--network", command.Arguments);
        Assert.Contains("test-network", command.Arguments);
        Assert.Contains("--network-alias", command.Arguments);
        Assert.Contains("worker", command.Arguments);
        Assert.Contains("--entrypoint", command.Arguments);
        Assert.Contains("--user", command.Arguments);
        Assert.Contains("--workdir", command.Arguments);
    }

    [Fact]
    public async Task InspectResourceAsync_reads_iso_creation_timestamp()
    {
        var runner = new RecordingWslcProcessRunner
        {
            Result = new WslcCommandResult(
                0,
                """[{"Id":"container-1","Created":"2026-07-09T21:58:23.734685022Z"}]""",
                string.Empty)
        };
        var backend = new WslcCliDockerBackend(runner);

        var resource = await backend.InspectResourceAsync(
            DockerResourceKind.Container,
            "container-1",
            CancellationToken.None);

        Assert.NotNull(resource);
        Assert.Equal(1783634303, resource.CreatedAt?.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task InspectResourceAsync_reads_numeric_creation_timestamp()
    {
        var runner = new RecordingWslcProcessRunner
        {
            Result = new WslcCommandResult(
                0,
                """[{"Id":"image-1","CreatedAt":1783634303}]""",
                string.Empty)
        };
        var backend = new WslcCliDockerBackend(runner);

        var resource = await backend.InspectResourceAsync(
            DockerResourceKind.Image,
            "image-1",
            CancellationToken.None);

        Assert.NotNull(resource);
        Assert.Equal(1783634303, resource.CreatedAt?.ToUnixTimeSeconds());
    }

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

    [Fact]
    public async Task InspectExecAsync_retains_completed_exec_before_compatibility_window_expires()
    {
        var timeProvider = new ManualTimeProvider();
        var backend = CreateBackendWithExecCache(timeProvider, TimeSpan.FromMinutes(5), 10);
        var id = await CreateAndCompleteExecAsync(backend);

        timeProvider.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));

        var response = await backend.InspectExecAsync(id, CancellationToken.None);

        Assert.NotNull(response);
        Assert.False(response.Running);
        Assert.Equal(23, response.ExitCode);
    }

    [Fact]
    public async Task InspectExecAsync_evicts_completed_exec_when_compatibility_window_expires()
    {
        var timeProvider = new ManualTimeProvider();
        var backend = CreateBackendWithExecCache(timeProvider, TimeSpan.FromMinutes(5), 10);
        var id = await CreateAndCompleteExecAsync(backend);

        timeProvider.Advance(TimeSpan.FromMinutes(5));

        var response = await backend.InspectExecAsync(id, CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task Completed_exec_cache_stays_within_bound_under_sustained_usage()
    {
        const int maximumCompletedExecs = 3;
        var timeProvider = new ManualTimeProvider();
        var backend = CreateBackendWithExecCache(
            timeProvider,
            TimeSpan.FromHours(1),
            maximumCompletedExecs);
        var ids = new List<string>();

        for (var index = 0; index < 20; index++)
        {
            ids.Add(await CreateAndCompleteExecAsync(backend));
        }

        var inspections = await Task.WhenAll(
            ids.Select(id => backend.InspectExecAsync(id, CancellationToken.None)));

        Assert.All(inspections[..^maximumCompletedExecs], Assert.Null);
        Assert.All(inspections[^maximumCompletedExecs..], Assert.NotNull);
    }

    [Fact]
    public async Task Completed_exec_cache_stays_within_bound_during_concurrent_completions()
    {
        const int maximumCompletedExecs = 4;
        var backend = new WslcCliDockerBackend(
            new YieldingWslcProcessRunner(),
            new ManualTimeProvider(),
            new WslcExecCacheOptions
            {
                CompletedExecRetention = TimeSpan.FromHours(1),
                MaxCompletedExecs = maximumCompletedExecs
            });
        var createResponses = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(index => backend.CreateContainerExecAsync(
                $"container-{index}",
                new DockerExecCreateRequest { Cmd = ["true"] },
                CancellationToken.None)));

        await Task.WhenAll(createResponses.Select(response => backend.StartExecAsync(
            response.Id,
            new DockerExecStartRequest(),
            CancellationToken.None)));
        var inspections = await Task.WhenAll(createResponses.Select(response =>
            backend.InspectExecAsync(response.Id, CancellationToken.None)));

        Assert.Equal(maximumCompletedExecs, inspections.Count(response => response is not null));
    }

    [Fact]
    public async Task Failed_exec_starts_enter_the_bounded_completed_cache()
    {
        const int maximumCompletedExecs = 2;
        var backend = new WslcCliDockerBackend(
            new ThrowingWslcProcessRunner(),
            new ManualTimeProvider(),
            new WslcExecCacheOptions
            {
                CompletedExecRetention = TimeSpan.FromHours(1),
                MaxCompletedExecs = maximumCompletedExecs
            });
        var createResponses = new List<DockerExecCreateResponse>();

        for (var index = 0; index < 10; index++)
        {
            var createResponse = await backend.CreateContainerExecAsync(
                $"container-{index}",
                new DockerExecCreateRequest { Cmd = ["false"] },
                CancellationToken.None);
            createResponses.Add(createResponse);
            await Assert.ThrowsAsync<InvalidOperationException>(() => backend.StartExecAsync(
                createResponse.Id,
                new DockerExecStartRequest(),
                CancellationToken.None));
        }

        var inspections = await Task.WhenAll(createResponses.Select(response =>
            backend.InspectExecAsync(response.Id, CancellationToken.None)));

        Assert.Equal(maximumCompletedExecs, inspections.Count(response => response is not null));
        Assert.All(inspections.Where(response => response is not null), response => Assert.Null(response!.ExitCode));
    }

    private static WslcCliDockerBackend CreateBackendWithExecCache(
        TimeProvider timeProvider,
        TimeSpan retention,
        int maximumCompletedExecs)
    {
        return new WslcCliDockerBackend(
            new RecordingWslcProcessRunner
            {
                Result = new WslcCommandResult(23, "output", "error")
            },
            timeProvider,
            new WslcExecCacheOptions
            {
                CompletedExecRetention = retention,
                MaxCompletedExecs = maximumCompletedExecs
            });
    }

    private static async Task<string> CreateAndCompleteExecAsync(WslcCliDockerBackend backend)
    {
        var createResponse = await backend.CreateContainerExecAsync(
            "container-1",
            new DockerExecCreateRequest { Cmd = ["echo", "ready"] },
            CancellationToken.None);

        var startResponse = await backend.StartExecAsync(
            createResponse.Id,
            new DockerExecStartRequest(),
            CancellationToken.None);

        Assert.NotNull(startResponse);
        return createResponse.Id;
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

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return Volatile.Read(ref timestamp);
        }

        public void Advance(TimeSpan duration)
        {
            Interlocked.Add(ref timestamp, duration.Ticks);
        }
    }

    private sealed class YieldingWslcProcessRunner : IWslcProcessRunner
    {
        public async Task<WslcCommandResult> RunAsync(
            WslcCommand command,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return new WslcCommandResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class ThrowingWslcProcessRunner : IWslcProcessRunner
    {
        public Task<WslcCommandResult> RunAsync(
            WslcCommand command,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated exec start failure.");
        }
    }
}
