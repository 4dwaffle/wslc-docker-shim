using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Testcontainers.WslcShim.Wslc;

public sealed class WslcProcessRunner : IWslcProcessRunner
{
    public async Task<WslcCommandResult> RunAsync(WslcCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        // Stream reads deliberately outlive request cancellation. On cancellation we kill the
        // process tree, then drain both redirected streams before disposing the Process object.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(outputTask, errorTask).WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested)
        {
            var cancellationException = exception as OperationCanceledException ??
                                        new OperationCanceledException(cancellationToken);
            await TerminateAndDrainAsync(
                process,
                outputTask,
                errorTask,
                ExceptionDispatchInfo.Capture(cancellationException));
            throw new UnreachableException();
        }

        return new WslcCommandResult(
            process.ExitCode,
            outputTask.Result,
            errorTask.Result);
    }

    private static async Task TerminateAndDrainAsync(
        Process process,
        Task<string> outputTask,
        Task<string> errorTask,
        ExceptionDispatchInfo cancellationException)
    {
        try
        {
            try
            {
                KillProcessTree(process);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            finally
            {
                await Task.WhenAll(outputTask, errorTask);
            }
        }
        finally
        {
            // Cancellation remains the public result even if process or stream cleanup faults.
            cancellationException.Throw();
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill. There is nothing left to terminate.
        }
        catch (Win32Exception) when (process.HasExited)
        {
            // Windows can report a failed Kill when the process exits during the native call.
        }
    }
}
