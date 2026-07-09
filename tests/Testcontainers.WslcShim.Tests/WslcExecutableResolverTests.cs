using Testcontainers.WslcShim.Wslc;

namespace Testcontainers.WslcShim.Tests;

public sealed class WslcExecutableResolverTests
{
    [Fact]
    public void Resolve_finds_executable_on_path()
    {
        var pathDirectory = Path.Combine("C:\\", "tools");
        var expected = Path.Combine(pathDirectory, "wslc.exe");

        var resolved = WslcExecutableResolver.Resolve(
            "wslc",
            pathDirectory,
            Path.Combine("C:\\", "Program Files"),
            path => string.Equals(path, expected, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Resolve_falls_back_to_standard_wsl_installation()
    {
        var programFiles = Path.Combine("C:\\", "Program Files");
        var expected = Path.Combine(programFiles, "WSL", "wslc.exe");

        var resolved = WslcExecutableResolver.Resolve(
            "wslc",
            Path.Combine("C:\\", "Windows", "System32"),
            programFiles,
            path => string.Equals(path, expected, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Resolve_preserves_unknown_command_when_no_executable_is_found()
    {
        var resolved = WslcExecutableResolver.Resolve(
            "custom-command",
            null,
            Path.Combine("C:\\", "Program Files"),
            _ => false);

        Assert.Equal("custom-command", resolved);
    }
}
