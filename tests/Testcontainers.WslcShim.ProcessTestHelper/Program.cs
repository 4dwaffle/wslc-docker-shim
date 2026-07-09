using System.Diagnostics;

namespace Testcontainers.WslcShim.ProcessTestHelper;

public static class ProcessTestHelperMarker
{
}

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            return 2;
        }

        if (string.Equals(args[0], "child", StringComparison.Ordinal))
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            return 0;
        }

        if (!string.Equals(args[0], "parent", StringComparison.Ordinal) || args.Length != 2)
        {
            return 2;
        }

        using var child = StartChild();
        await File.WriteAllLinesAsync(
            args[1],
            [Environment.ProcessId.ToString(), child.Id.ToString()]);

        Console.WriteLine("parent-ready");
        Console.Error.WriteLine("child-ready");
        await child.WaitForExitAsync();
        return child.ExitCode;
    }

    private static Process StartChild()
    {
        var dotnetHost = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the .NET host.");
        var startInfo = new ProcessStartInfo(dotnetHost)
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(typeof(ProcessTestHelperMarker).Assembly.Location);
        startInfo.ArgumentList.Add("child");
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the child process.");
    }
}
