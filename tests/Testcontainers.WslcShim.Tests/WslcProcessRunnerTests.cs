using System.Diagnostics;
using Testcontainers.WslcShim.ProcessTestHelper;
using Testcontainers.WslcShim.Wslc;
using Testcontainers.WslcShim.Wslc.Models;

namespace Testcontainers.WslcShim.Tests;

public sealed class WslcProcessRunnerTests
{
    [Fact(Timeout = 15_000)]
    public async Task RunAsync_cancellation_terminates_process_tree_and_preserves_cancellation()
    {
        var pidFile = Path.Combine(Path.GetTempPath(), $"wslc-process-test-{Guid.NewGuid():N}.pid");
        var processIds = Array.Empty<int>();

        try
        {
            var runner = new WslcProcessRunner();
            using var cancellationTokenSource = new CancellationTokenSource();
            var command = new WslcCommand(
                GetDotNetHostPath(),
                [typeof(ProcessTestHelperMarker).Assembly.Location, "parent", pidFile]);
            var runTask = runner.RunAsync(command, cancellationTokenSource.Token);
            processIds = await WaitForProcessIdsAsync(pidFile);

            Assert.All(processIds, processId => Assert.True(IsProcessRunning(processId)));

            cancellationTokenSource.Cancel();
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

            Assert.Equal(cancellationTokenSource.Token, exception.CancellationToken);
            Assert.All(processIds, processId => Assert.False(IsProcessRunning(processId)));
        }
        finally
        {
            foreach (var processId in processIds)
            {
                KillIfRunning(processId);
            }

            File.Delete(pidFile);
        }
    }

    private static string GetDotNetHostPath()
    {
        return Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    }

    private static async Task<int[]> WaitForProcessIdsAsync(string pidFile)
    {
        var timeout = Task.Delay(TimeSpan.FromSeconds(10));
        while (!timeout.IsCompleted)
        {
            if (File.Exists(pidFile))
            {
                var lines = await File.ReadAllLinesAsync(pidFile);
                if (lines.Length == 2 && lines.All(line => int.TryParse(line, out _)))
                {
                    return lines.Select(int.Parse).ToArray();
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        throw new TimeoutException("The process test helper did not publish its process IDs.");
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void KillIfRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        catch (ArgumentException)
        {
            // The process already exited.
        }
    }
}
