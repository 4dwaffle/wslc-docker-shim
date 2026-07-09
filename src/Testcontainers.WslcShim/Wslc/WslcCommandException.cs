namespace Testcontainers.WslcShim.Wslc;

public sealed class WslcCommandException(WslcCommand command, WslcCommandResult result)
    : InvalidOperationException($"wslc exited with code {result.ExitCode}: {result.StandardError}")
{
    public WslcCommand Command { get; } = command;

    public WslcCommandResult Result { get; } = result;
}
