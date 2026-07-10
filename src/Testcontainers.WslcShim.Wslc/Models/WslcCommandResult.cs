namespace Testcontainers.WslcShim.Wslc.Models;

public sealed record WslcCommandResult(int ExitCode, string StandardOutput, string StandardError);
