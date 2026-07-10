using Testcontainers.WslcShim.Cli;

namespace Testcontainers.WslcShim.Tests;

public sealed class CliHelpTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void TryWrite_writes_help_for_supported_alias(string argument)
    {
        using var output = new StringWriter();

        var handled = CliHelp.TryWrite([argument], output);

        Assert.True(handled);
        var help = output.ToString();
        Assert.Contains("Usage:", help);
        Assert.Contains("--full-api-address", help);
        Assert.Contains("--full-api-port", help);
        Assert.Contains("--wslc-host-address", help);
        Assert.Contains("--ryuk-bind-address", help);
        Assert.Contains("--ryuk-api-port", help);
    }

    [Fact]
    public void TryWrite_ignores_arguments_without_help()
    {
        using var output = new StringWriter();

        var handled = CliHelp.TryWrite(["--full-api-port", "12345"], output);

        Assert.False(handled);
        Assert.Equal(string.Empty, output.ToString());
    }
}
