namespace Testcontainers.WslcShim.Wslc;

public sealed record WslcCommandResult(int ExitCode, string StandardOutput, string StandardError);
