using System.Globalization;
using Microsoft.AspNetCore.Http;
using Testcontainers.WslcShim;
using Testcontainers.WslcShim.Cli;
using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Http;
using Testcontainers.WslcShim.Http.Enums;
using Testcontainers.WslcShim.Http.Models;
using Testcontainers.WslcShim.Ryuk.Models;
using Testcontainers.WslcShim.Watch;
using Testcontainers.WslcShim.Wslc;
using Testcontainers.WslcShim.Wslc.Models;

namespace Testcontainers.WslcShim.Tests;

public sealed class WatchModeTests
{
    [Fact]
    public void Launch_options_extract_watch_without_changing_application_arguments()
    {
        var options = ShimLaunchOptions.Parse(
            ["--full-api-port", "49152", "--WATCH", "--wslc-host-address", "172.28.48.1"]);

        Assert.True(options.WatchEnabled);
        Assert.Equal(
            ["--full-api-port", "49152", "--wslc-host-address", "172.28.48.1"],
            options.ApplicationArguments);
    }

    [Fact]
    public void Launch_options_leave_arguments_unchanged_without_watch()
    {
        var arguments = new[] { "--full-api-port", "49152" };

        var options = ShimLaunchOptions.Parse(arguments);

        Assert.False(options.WatchEnabled);
        Assert.Equal(arguments, options.ApplicationArguments);
    }

    [Fact]
    public void Cli_help_documents_watch_mode()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var handled = CliHelp.TryWrite(["--help"], output);

        Assert.True(handled);
        Assert.Contains("--watch", output.ToString());
        Assert.Contains("does not reload source", output.ToString());
    }

    [Fact]
    public void Reporter_prints_plain_startup_and_activity_output()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var reporter = CreateReporter(output);
        var options = new ShimRuntimeOptions
        {
            FullApiAddress = "127.0.0.1",
            FullApiPort = 23755,
            RyukBindAddress = "0.0.0.0",
            RyukEndpoint = new RyukListenerEndpoint("172.28.48.1", 49152)
        };

        reporter.WriteStartup(options);
        reporter.RequestStarted("0001", ShimListenerKind.FullApi, "GET", "/_ping");
        reporter.RequestCompleted("0001", 200, "GET", "/_ping", TimeSpan.FromMilliseconds(12));
        reporter.WriteStopping();

        var trace = output.ToString();
        Assert.Contains("WSLc Docker Shim  WATCH", trace);
        Assert.Contains("Docker API   tcp://127.0.0.1:23755", trace);
        Assert.Contains("Ryuk bind    tcp://0.0.0.0:49152", trace);
        Assert.Contains("Ryuk target  tcp://172.28.48.1:49152", trace);
        Assert.Contains("$env:DOCKER_HOST = \"tcp://127.0.0.1:23755\"", trace);
        Assert.Contains("0001  HTTP  -> FULL GET", trace);
        Assert.Contains("0001  HTTP  <- 200 GET", trace);
        Assert.Contains("12ms", trace);
        Assert.DoesNotContain("\u001b[", trace);
    }

    [Fact]
    public async Task Request_and_wslc_activity_share_a_correlation_id_without_leaking_raw_values()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var reporter = CreateReporter(output);
        var requestContext = new WatchRequestContext();
        var inner = new StubProcessRunner((_, _) => Task.FromResult(
            new WslcCommandResult(0, "stdout-secret", "stderr-secret")));
        var runner = new WatchingWslcProcessRunner(inner, reporter, requestContext);
        var middleware = new WatchRequestMiddleware(async context =>
        {
            await runner.RunAsync(
                new WslcCommand(
                    "wslc",
                    ["create", "--env", "PASSWORD=raw-secret", "alpine", "echo", "command-secret"],
                    "create container test-db"),
                context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status201Created;
        });
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1.43/containers/create";
        context.Request.QueryString = new QueryString("?name=query-secret");

        await middleware.InvokeAsync(context, reporter, requestContext, new HeaderListenerClassifier());

        var activityLines = output.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, activityLines.Length);
        Assert.All(activityLines, line => Assert.Contains("0001", line));
        Assert.Contains("FULL POST", activityLines[0]);
        Assert.Contains("create container test-db", activityLines[1]);
        Assert.Contains("exit=0", activityLines[2]);
        Assert.Contains("201 POST", activityLines[3]);
        Assert.DoesNotContain("raw-secret", output.ToString());
        Assert.DoesNotContain("command-secret", output.ToString());
        Assert.DoesNotContain("query-secret", output.ToString());
        Assert.DoesNotContain("stdout-secret", output.ToString());
        Assert.DoesNotContain("stderr-secret", output.ToString());
    }

    [Fact]
    public async Task Watching_runner_reports_nonzero_exit_without_exposing_process_output()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var reporter = CreateReporter(output);
        var runner = new WatchingWslcProcessRunner(
            new StubProcessRunner((_, _) => Task.FromResult(
                new WslcCommandResult(23, "private-output", "private-error"))),
            reporter,
            new WatchRequestContext());

        var result = await runner.RunAsync(
            new WslcCommand("wslc", ["pull", "redis:7"], "pull image redis:7"),
            CancellationToken.None);

        Assert.Equal(23, result.ExitCode);
        Assert.Contains("----  WSLC  XX pull image redis:7  exit=23", output.ToString());
        Assert.DoesNotContain("private-output", output.ToString());
        Assert.DoesNotContain("private-error", output.ToString());
    }

    [Fact]
    public async Task Watching_runner_preserves_and_reports_cancellation()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var reporter = CreateReporter(output);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var runner = new WatchingWslcProcessRunner(
            new StubProcessRunner((_, cancellationToken) =>
                Task.FromCanceled<WslcCommandResult>(cancellationToken)),
            reporter,
            new WatchRequestContext());

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            new WslcCommand("wslc", ["inspect", "container-1"], "inspect container container-1"),
            cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, exception.CancellationToken);
        Assert.Contains("inspect container container-1  cancelled", output.ToString());
    }

    [Fact]
    public void Create_command_activity_name_omits_environment_and_container_command()
    {
        var command = WslcCommandBuilder.BuildCreateContainerCommand(
            new DockerContainerCreateRequest
            {
                Image = "alpine:3.20",
                Name = "safe-name",
                Env = ["PASSWORD=raw-secret"],
                Cmd = ["echo", "command-secret"]
            },
            "cid.txt");

        Assert.Equal("create container safe-name", command.ActivityName);
        Assert.DoesNotContain("raw-secret", command.ActivityName);
        Assert.DoesNotContain("command-secret", command.ActivityName);
    }

    private static ConsoleWatchActivityReporter CreateReporter(TextWriter writer)
    {
        return new ConsoleWatchActivityReporter(
            writer,
            useColor: false,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 10, 12, 4, 11, TimeSpan.Zero)));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class StubProcessRunner(
        Func<WslcCommand, CancellationToken, Task<WslcCommandResult>> run) : IWslcProcessRunner
    {
        public Task<WslcCommandResult> RunAsync(WslcCommand command, CancellationToken cancellationToken)
        {
            return run(command, cancellationToken);
        }
    }
}
