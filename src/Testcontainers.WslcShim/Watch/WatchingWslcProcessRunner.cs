using System.Diagnostics;
using Testcontainers.WslcShim.Wslc;
using Testcontainers.WslcShim.Wslc.Models;

namespace Testcontainers.WslcShim.Watch;

internal sealed class WatchingWslcProcessRunner(
    IWslcProcessRunner inner,
    IWatchActivityReporter reporter,
    WatchRequestContext requestContext) : IWslcProcessRunner
{
    public async Task<WslcCommandResult> RunAsync(WslcCommand command, CancellationToken cancellationToken)
    {
        var activityName = command.ActivityName ?? GetFallbackActivityName(command);
        var requestId = requestContext.CurrentRequestId;
        var startedAt = Stopwatch.GetTimestamp();

        reporter.CommandStarted(requestId, activityName);
        try
        {
            var result = await inner.RunAsync(command, cancellationToken);
            reporter.CommandCompleted(
                requestId,
                activityName,
                result.ExitCode,
                Stopwatch.GetElapsedTime(startedAt));
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            reporter.CommandCancelled(requestId, activityName, Stopwatch.GetElapsedTime(startedAt));
            throw;
        }
        catch (Exception exception)
        {
            reporter.CommandFailed(requestId, activityName, exception, Stopwatch.GetElapsedTime(startedAt));
            throw;
        }
    }

    private static string GetFallbackActivityName(WslcCommand command)
    {
        var executable = Path.GetFileNameWithoutExtension(command.FileName);
        var verb = command.Arguments.FirstOrDefault();
        return string.IsNullOrWhiteSpace(verb) ? executable : $"{executable} {verb}";
    }
}
