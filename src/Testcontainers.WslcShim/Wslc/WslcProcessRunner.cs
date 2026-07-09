using System.Diagnostics;

namespace Testcontainers.WslcShim.Wslc;

public sealed class WslcProcessRunner : IWslcProcessRunner
{
    public async Task<WslcCommandResult> RunAsync(WslcCommand command, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(WslcExecutableResolver.Resolve(command.FileName))
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new WslcCommandResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }
}
