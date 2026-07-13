using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Http.Models;
using Testcontainers.WslcShim.Ryuk.Models;
using Testcontainers.WslcShim.Watch;
using Testcontainers.WslcShim.Wslc;
using Testcontainers.WslcShim.Wslc.Models;

namespace Testcontainers.WslcShim.Tests;

public sealed class DashboardModeTests
{
    [Fact]
    public void Dashboard_state_removes_containers_after_three_missing_observations()
    {
        var state = new WatchDashboardState(FixedClock());
        var create = state.BeginContainerCreate("db", "postgres:17", "127.0.0.1:5432->5432/tcp");
        state.CompleteContainerCreate(create, "container-1234567890");

        state.ObserveMissing("container-1234567890");
        state.ObserveMissing("container-1234567890");

        var container = Assert.Single(state.Snapshot().Containers);
        Assert.Equal("container-1234567890", container.Id);
        Assert.Equal("db", container.Name);
        Assert.Equal("created", container.Status);

        state.ObserveMissing("container-1234567890");

        Assert.Empty(state.Snapshot().Containers);
        Assert.Null(state.BeginContainerOperation("db", "starting"));
    }

    [Fact]
    public void Dashboard_state_tracks_status_and_stats_without_redrawing_unchanged_values()
    {
        var state = new WatchDashboardState(FixedClock());
        var create = state.BeginContainerCreate("cache", "redis:7", string.Empty);
        state.CompleteContainerCreate(create, "container-1");
        var failedStart = state.BeginContainerOperation("container-1", "starting");

        Assert.Equal("starting", Assert.Single(state.Snapshot().Containers).Status);

        state.FailContainerOperation(failedStart);
        var start = state.BeginContainerOperation("container-1", "starting");
        state.CompleteContainerOperation(start, "running");
        state.ObserveContainerStats("container-1", new WatchContainerStats("12.34%", "64.5 MiB"));
        var observedVersion = state.Snapshot().Version;

        state.ObserveContainerStats("container-1", new WatchContainerStats("12.34%", "64.5 MiB"));

        var container = Assert.Single(state.Snapshot().Containers);
        Assert.Equal(observedVersion, state.Snapshot().Version);
        Assert.Equal("running", container.Status);
        Assert.Equal("12.34%", container.CpuUsage);
        Assert.Equal("64.5 MiB", container.MemoryUsage);
    }

    [Fact]
    public void Dashboard_state_does_not_request_a_render_for_an_unchanged_inventory_observation()
    {
        var state = new WatchDashboardState(FixedClock());
        var create = state.BeginContainerCreate("cache", "redis:7", string.Empty);
        state.CompleteContainerCreate(create, "container-1");
        var observation = new WatchContainerListObservation(
            "container-1",
            "cache",
            "redis:7",
            FixedClock().GetUtcNow(),
            string.Empty,
            "running-token");

        Assert.True(state.ObserveListEntry(observation));
        state.ObserveContainerDetails("container-1", new WatchContainerDetails("running", null), observation.StateToken);
        var observedVersion = state.Snapshot().Version;

        Assert.False(state.ObserveListEntry(observation));
        Assert.Equal(observedVersion, state.Snapshot().Version);
    }

    [Fact]
    public void Inventory_parser_reads_list_inspect_and_stats_shapes()
    {
        const string listJson = """
            [{
              "CreatedAt": 1783623438,
              "Id": "container-1",
              "Image": "redis:7@sha256:abc",
              "Name": "cache",
              "Ports": [
                {"BindingAddress":"127.0.0.1","HostPort":49152,"ContainerPort":6379,"Protocol":6},
                {"BindingAddress":"0.0.0.0","HostPort":49153,"ContainerPort":53,"Protocol":17}
              ],
              "State": 2,
              "StateChangedAt": 1783623463
            }]
            """;
        const string inspectJson = """
            [{"State":{"ExitCode":17,"Running":false,"Status":"exited"}}]
            """;
        const string statsJson = """
            [{
              "CPUPerc": "12.34%",
              "ID": "container-1",
              "MemPerc": "0.40%",
              "MemUsage": "123.45 MiB / 30.16 GiB",
              "Name": "cache"
            }]
            """;

        var item = Assert.Single(WslcContainerInventoryService.ParseList(listJson)).Value;
        var parsed = WslcContainerInventoryService.TryParseDetails(inspectJson, out var details);
        var stats = Assert.Single(WslcContainerInventoryService.ParseStats(statsJson)).Value;

        Assert.Equal("cache", item.Name);
        Assert.Equal("redis:7@sha256:abc", item.Image);
        Assert.Equal("127.0.0.1:49152->6379/tcp, 0.0.0.0:49153->53/udp", item.Ports);
        Assert.Contains("1783623463", item.StateToken);
        Assert.True(parsed);
        Assert.Equal("exited", details.Status);
        Assert.Equal(17, details.ExitCode);
        Assert.Equal("12.34%", stats.CpuUsage);
        Assert.Equal("123.45 MiB", stats.MemoryUsage);
    }

    [Fact]
    public async Task Inventory_refresh_updates_container_status_cpu_and_memory_usage()
    {
        var state = new WatchDashboardState(FixedClock());
        var create = state.BeginContainerCreate("cache", "redis:7", string.Empty);
        state.CompleteContainerCreate(create, "container-1");
        var commands = new List<WslcCommand>();
        var runner = new StubWslcProcessRunner((command, _) =>
        {
            commands.Add(command);
            var output = command.Arguments[0] switch
            {
                "list" => """[{"Id":"container-1","Name":"cache","Image":"redis:7","State":2}]""",
                "stats" => """[{"ID":"container-1","CPUPerc":"3.50%","MemUsage":"72.25 MiB / 4 GiB"}]""",
                "inspect" => """[{"State":{"Running":true,"Status":"running"}}]""",
                _ => throw new InvalidOperationException($"Unexpected command: {command.Arguments[0]}")
            };
            return Task.FromResult(new WslcCommandResult(0, output, string.Empty));
        });
        var inventory = new WslcContainerInventoryService(runner, state);

        await inventory.RefreshAsync(CancellationToken.None);

        var container = Assert.Single(state.Snapshot().Containers);
        Assert.Equal("running", container.Status);
        Assert.Equal("3.50%", container.CpuUsage);
        Assert.Equal("72.25 MiB", container.MemoryUsage);
        Assert.Contains(commands, command => command.Arguments.SequenceEqual(["stats", "--all", "--format", "json"]));
    }

    [Fact]
    public async Task Inventory_retries_inspection_until_the_state_token_is_successfully_applied()
    {
        var state = new WatchDashboardState(FixedClock());
        var create = state.BeginContainerCreate("worker", "example/worker:1", string.Empty);
        state.CompleteContainerCreate(create, "container-1");
        state.SetContainerStatus("container-1", "running");
        var inspectAttempts = 0;
        var runner = new StubWslcProcessRunner((command, _) =>
        {
            if (command.Arguments[0] == "list")
            {
                return Task.FromResult(new WslcCommandResult(
                    0,
                    """[{"Id":"container-1","Name":"worker","Image":"example/worker:1","State":3,"StateChangedAt":99}]""",
                    string.Empty));
            }

            if (command.Arguments[0] == "stats")
            {
                return Task.FromResult(new WslcCommandResult(0, "[]", string.Empty));
            }

            inspectAttempts++;
            return Task.FromResult(inspectAttempts == 1
                ? new WslcCommandResult(0, "{malformed", string.Empty)
                : new WslcCommandResult(
                    0,
                    """[{"State":{"ExitCode":0,"Running":false,"Status":"exited"}}]""",
                    string.Empty));
        });
        var inventory = new WslcContainerInventoryService(runner, state);

        await inventory.RefreshAsync(CancellationToken.None);
        await inventory.RefreshAsync(CancellationToken.None);

        Assert.Equal(2, inspectAttempts);
        Assert.Equal("exited (0)", Assert.Single(state.Snapshot().Containers).Status);
    }

    [Fact]
    public async Task Watching_backend_updates_only_live_container_state()
    {
        var state = new WatchDashboardState(FixedClock());
        var backend = new WatchingDockerBackend(new StubDockerBackend(), state);

        var created = await backend.CreateContainerAsync(
            new DockerContainerCreateRequest { Name = "db", Image = "postgres:17" },
            CancellationToken.None);
        await backend.StartContainerAsync(created.Id, CancellationToken.None);
        await backend.PullImageAsync("postgres:17", CancellationToken.None);
        await backend.CreateResourceAsync(
            DockerResourceKind.Network,
            new DockerResourceCreateRequest { Name = "test-network" },
            CancellationToken.None);

        var active = Assert.Single(state.Snapshot().Containers);
        Assert.Equal("running", active.Status);

        await backend.WaitContainerAsync(created.Id, CancellationToken.None);
        Assert.Equal("exited (0)", Assert.Single(state.Snapshot().Containers).Status);

        await backend.DeleteResourceAsync(DockerResourceKind.Container, created.Id, CancellationToken.None);
        Assert.Empty(state.Snapshot().Containers);
        Assert.Null(state.BeginContainerOperation(created.Id, "starting"));
    }

    [Fact]
    public async Task Watching_backend_restores_status_after_a_failed_lifecycle_operation()
    {
        var state = new WatchDashboardState(FixedClock());
        var backend = new WatchingDockerBackend(new StubDockerBackend { FailStart = true }, state);
        var create = state.BeginContainerCreate("api", "example/api:1", string.Empty);
        state.CompleteContainerCreate(create, "known-container");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backend.StartContainerAsync("known-container", CancellationToken.None));

        Assert.Equal("created", Assert.Single(state.Snapshot().Containers).Status);
    }

    [Fact]
    public void Renderer_produces_one_live_container_table_without_event_sections()
    {
        var clock = FixedClock();
        var state = new WatchDashboardState(clock);
        var create = state.BeginContainerCreate("database", "postgres:17@sha256:abc", "127.0.0.1:5432->5432/tcp");
        state.CompleteContainerCreate(create, "abcdef1234567890");
        var start = state.BeginContainerOperation("abcdef", "starting");
        state.CompleteContainerOperation(start, "running");
        state.ObserveContainerStats("abcdef", new WatchContainerStats("2.75%", "42.5 MiB"));
        var renderer = new WatchDashboardRenderer(clock);
        var options = Options();

        var result = renderer.Render(state.Snapshot(), options, 140, 30, 0, useColor: false);
        var compact = renderer.Render(state.Snapshot(), options, 86, 10, 0, useColor: false);
        var small = renderer.Render(state.Snapshot(), options, 70, 9, 0, useColor: false);

        Assert.Contains("CONTAINER ID", result.Frame);
        Assert.Contains("NAME", result.Frame);
        Assert.Contains("IMAGE", result.Frame);
        Assert.Contains("STATUS", result.Frame);
        Assert.Contains("CPU %", result.Frame);
        Assert.Contains("MEMORY", result.Frame);
        Assert.Contains("PORTS", result.Frame);
        Assert.Contains("abcdef123456", result.Frame);
        Assert.Contains("database", result.Frame);
        Assert.Contains("running", result.Frame);
        Assert.Contains("2.75%", result.Frame);
        Assert.Contains("42.5 MiB", result.Frame);
        Assert.DoesNotContain("EVENTS", result.Frame);
        Assert.DoesNotContain("↳", result.Frame);
        Assert.DoesNotContain("GENERAL EVENTS", result.Frame);
        Assert.DoesNotContain("Tab switch", result.Frame);
        Assert.Single(result.Frame.Split(Environment.NewLine), line => line.Contains("abcdef123456", StringComparison.Ordinal));
        Assert.Contains("CPU %", compact.Frame);
        Assert.Contains("MEMORY", compact.Frame);
        Assert.All(
            compact.Frame.Split(Environment.NewLine),
            line => Assert.True(line.Length <= 86, $"Rendered line is {line.Length} characters: {line}"));
        Assert.Contains("resize terminal", small.Frame);
    }

    [Fact]
    public async Task Dashboard_service_enters_renders_and_restores_terminal()
    {
        var terminal = new RecordingTerminal();
        var state = new WatchDashboardState(FixedClock());
        var service = new WatchDashboardService(
            terminal,
            new WatchDashboardRenderer(FixedClock()),
            state,
            Options());

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(250);
        await service.StopAsync(CancellationToken.None);

        Assert.True(terminal.Entered);
        Assert.True(terminal.Exited);
        Assert.NotEmpty(terminal.Frames);
        Assert.Contains("CONTAINERS", terminal.Frames[^1]);
        Assert.DoesNotContain("GENERAL EVENTS", terminal.Frames[^1]);
    }

    [Fact]
    public void System_terminal_clears_once_then_rewrites_frames_in_place()
    {
        using var output = new StringWriter();
        var terminal = new SystemWatchTerminal(output);

        terminal.Enter();
        terminal.WriteFrame($"first{Environment.NewLine}frame");
        terminal.WriteFrame("second");
        terminal.Exit();

        var trace = output.ToString();
        Assert.Equal(1, trace.Split("\u001b[2J", StringSplitOptions.None).Length - 1);
        Assert.Contains($"\u001b[Hfirst\u001b[K{Environment.NewLine}frame\u001b[J", trace);
        Assert.Contains("\u001b[Hsecond\u001b[J", trace);
    }

    private static TimeProvider FixedClock() =>
        new FixedTimeProvider(new DateTimeOffset(2026, 7, 10, 12, 4, 11, TimeSpan.Zero));

    private static ShimRuntimeOptions Options() => new()
    {
        FullApiAddress = "127.0.0.1",
        FullApiPort = 23755,
        RyukBindAddress = "0.0.0.0",
        RyukEndpoint = new RyukListenerEndpoint("172.28.48.1", 49152)
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class RecordingTerminal : IWatchTerminal
    {
        public int Width => 120;
        public int Height => 24;
        public bool UseColor => false;
        public bool Entered { get; private set; }
        public bool Exited { get; private set; }
        public List<string> Frames { get; } = [];
        public void Enter() => Entered = true;
        public void WriteFrame(string frame) => Frames.Add(frame);
        public bool TryReadKey(out ConsoleKeyInfo key) { key = default; return false; }
        public void Exit() => Exited = true;
    }

    private sealed class StubWslcProcessRunner(
        Func<WslcCommand, CancellationToken, Task<WslcCommandResult>> run) : IWslcProcessRunner
    {
        public Task<WslcCommandResult> RunAsync(WslcCommand command, CancellationToken cancellationToken) =>
            run(command, cancellationToken);
    }

    private sealed class StubDockerBackend : IDockerBackend
    {
        public bool FailStart { get; init; }

        public Task<DockerCreateContainerResponse> CreateContainerAsync(DockerContainerCreateRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new DockerCreateContainerResponse("container-1234567890"));
        public Task<IReadOnlyList<DockerResourceSnapshot>> ListResourcesAsync(DockerResourceKind kind, DockerLabelFilters filters, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DockerResourceSnapshot>>([]);
        public Task<DockerResourceSnapshot?> InspectResourceAsync(DockerResourceKind kind, string id, CancellationToken cancellationToken) =>
            Task.FromResult<DockerResourceSnapshot?>(null);
        public Task<string?> InspectResourceJsonAsync(DockerResourceKind kind, string id, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task DeleteResourceAsync(DockerResourceKind kind, string id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartContainerAsync(string id, CancellationToken cancellationToken) =>
            FailStart
                ? Task.FromException(new InvalidOperationException("start failed"))
                : Task.CompletedTask;
        public Task StopContainerAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> GetContainerLogsAsync(string id, DockerLogRequest request, CancellationToken cancellationToken) => Task.FromResult("logs");
        public Task<DockerWaitContainerResponse?> WaitContainerAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult<DockerWaitContainerResponse?>(new DockerWaitContainerResponse(0));
        public Task<DockerExecCreateResponse> CreateContainerExecAsync(string containerId, DockerExecCreateRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new DockerExecCreateResponse("exec-1"));
        public Task<DockerExecStartResponse?> StartExecAsync(string id, DockerExecStartRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<DockerExecStartResponse?>(new DockerExecStartResponse(string.Empty, 0));
        public Task<DockerExecInspectResponse?> InspectExecAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult<DockerExecInspectResponse?>(null);
        public Task PullImageAsync(string image, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<DockerResourceSnapshot> CreateResourceAsync(DockerResourceKind kind, DockerResourceCreateRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new DockerResourceSnapshot(request.Name ?? "generated", new Dictionary<string, string>(), request.Name));
    }
}
