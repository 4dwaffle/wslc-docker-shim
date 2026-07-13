using System.Globalization;
using Testcontainers.WslcShim;
using Testcontainers.WslcShim.Cli;
using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Wslc;

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
    public void Launch_options_require_an_interactive_terminal_only_for_watch()
    {
        Assert.Contains("interactive terminal", ShimLaunchOptions.Parse(["--watch"]).GetValidationError(false));
        Assert.Null(ShimLaunchOptions.Parse([]).GetValidationError(false));
    }

    [Fact]
    public void Cli_help_documents_watch_mode()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var handled = CliHelp.TryWrite(["--help"], output);

        Assert.True(handled);
        Assert.Contains("--watch", output.ToString());
        Assert.DoesNotContain("--verbose", output.ToString());
        Assert.Contains("does not reload source", output.ToString());
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
}
