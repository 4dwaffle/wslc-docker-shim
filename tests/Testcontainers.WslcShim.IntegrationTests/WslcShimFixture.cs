using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Testcontainers.WslcShim.IntegrationTests;

public sealed class WslcShimFixture : IAsyncLifetime
{
    private readonly ConcurrentQueue<string> output = new();
    private Process? process;

    public int FullApiPort { get; } = TcpPort.Allocate();

    public int RyukApiPort { get; } = TcpPort.Allocate();

    public Uri DockerEndpoint => new($"tcp://127.0.0.1:{FullApiPort}");

    private string WslcHostAddress =>
        Environment.GetEnvironmentVariable("WSLC_SHIM_TEST_HOST_ADDRESS") ??
        WslcHostAddressDetector.Detect() ??
        "127.0.0.1";

    public async Task InitializeAsync()
    {
        var repoRoot = RepoPaths.FindRepositoryRoot();
        var projectPath = Path.Combine(repoRoot, "src", "Testcontainers.WslcShim", "Testcontainers.WslcShim.csproj");

        process = new Process
        {
            StartInfo = CreateStartInfo(repoRoot, projectPath),
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, args) => EnqueueOutput(args.Data);
        process.ErrorDataReceived += (_, args) => EnqueueOutput(args.Data);

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start the WSLc Docker API shim process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WaitForShimAsync();
    }

    public async Task DisposeAsync()
    {
        if (process is null)
        {
            return;
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        process.Dispose();
    }

    private ProcessStartInfo CreateStartInfo(string repoRoot, string projectPath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--full-api-port");
        startInfo.ArgumentList.Add(FullApiPort.ToString());
        startInfo.ArgumentList.Add("--ryuk-api-port");
        startInfo.ArgumentList.Add(RyukApiPort.ToString());
        startInfo.ArgumentList.Add("--wslc-host-address");
        startInfo.ArgumentList.Add(WslcHostAddress);

        startInfo.Environment["TESTCONTAINERS_RYUK_DISABLED"] = string.Empty;

        return startInfo;
    }

    private async Task WaitForShimAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var pingUri = new Uri($"http://127.0.0.1:{FullApiPort}/_ping");

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process is not null && process.HasExited)
            {
                throw new InvalidOperationException($"Shim exited before it became ready.{Environment.NewLine}{ReadOutput()}");
            }

            try
            {
                var response = await client.GetStringAsync(pingUri);
                if (response == "OK")
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Shim did not respond to _ping in time.{Environment.NewLine}{ReadOutput()}");
    }

    private void EnqueueOutput(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            output.Enqueue(line);
        }
    }

    private string ReadOutput()
    {
        return string.Join(Environment.NewLine, output);
    }
}

file static class RepoPaths
{
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WslcDockerShim.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find WslcDockerShim.slnx from the test output directory.");
    }
}

file static class TcpPort
{
    public static int Allocate()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

file static class WslcHostAddressDetector
{
    public static string? Detect()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up &&
                networkInterface.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase))
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address.ToString())
            .FirstOrDefault(address => !IPAddress.IsLoopback(IPAddress.Parse(address)));
    }
}
