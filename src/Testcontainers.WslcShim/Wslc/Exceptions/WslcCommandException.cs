using Testcontainers.WslcShim.Wslc.Models;

namespace Testcontainers.WslcShim.Wslc.Exceptions;

public sealed class WslcCommandException(WslcCommand command, WslcCommandResult result)
    : InvalidOperationException($"wslc exited with code {result.ExitCode}: {result.StandardError}")
{
    public WslcCommand Command { get; } = command;

    public WslcCommandResult Result { get; } = result;
}
