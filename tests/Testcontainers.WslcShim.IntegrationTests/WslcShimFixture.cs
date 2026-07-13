using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.IntegrationTests;

public sealed class WslcShimFixture : IAsyncLifetime
{
#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    private readonly ConcurrentQueue<string> output = new();
    private readonly HttpClient dockerClient = new();
    private Process? process;
    private ResourceReaper? resourceReaper;
    private string? originalRyukDisabled;

    public int FullApiPort { get; } = TcpPort.Allocate();

    public int RyukApiPort { get; } = TcpPort.Allocate();

    public Uri DockerEndpoint => new($"tcp://127.0.0.1:{FullApiPort}");

    private string WslcHostAddress =>
        Environment.GetEnvironmentVariable("WSLC_SHIM_TEST_HOST_ADDRESS") ??
        WslcHostAddressDetector.Detect() ??
        "127.0.0.1";

    public async Task InitializeAsync()
    {
        originalRyukDisabled = Environment.GetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED");
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "false");

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
        dockerClient.BaseAddress = new Uri($"http://127.0.0.1:{FullApiPort}");
        resourceReaper = await ResourceReaper.GetAndStartDefaultAsync(
            new DockerEndpointAuthenticationConfiguration(DockerEndpoint),
            NullLogger.Instance);
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (resourceReaper is not null)
            {
                await resourceReaper.DisposeAsync();
                resourceReaper = null;
            }

            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        finally
        {
            process?.Dispose();
            process = null;
            dockerClient.Dispose();
            Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", originalRyukDisabled);
        }
    }

    public async Task<RyukCleanupResult> RunRyukCleanupScenarioAsync()
    {
        _ = resourceReaper ?? throw new InvalidOperationException("The default resource reaper is not running.");
        var session = Guid.NewGuid().ToString("D");
        var suffix = Guid.NewGuid().ToString("N");
        var matchingName = $"wslc-ryuk-match-{suffix}";
        var otherName = $"wslc-ryuk-other-{suffix}";
        var unlabelledName = $"wslc-ryuk-unlabelled-{suffix}";
        var createdIds = new List<string>();

        try
        {
            var ryukPort = TcpPort.Allocate();
            var ryukId = await CreateRyukContainerAsync(session, ryukPort);
            createdIds.Add(ryukId);
            using (var startResponse = await dockerClient.PostAsync($"/containers/{ryukId}/start", null))
            {
                startResponse.EnsureSuccessStatusCode();
            }

            using var registration = await RegisterWithRyukAsync(ryukPort, session);

            var matchingId = await CreateStoppedContainerAsync(
                matchingName,
                new Dictionary<string, string>
                {
                    [ResourceReaper.ResourceReaperSessionLabel] = session
                });
            var otherId = await CreateStoppedContainerAsync(
                otherName,
                new Dictionary<string, string>
                {
                    [ResourceReaper.ResourceReaperSessionLabel] = Guid.NewGuid().ToString("D")
                });
            var unlabelledId = await CreateStoppedContainerAsync(
                unlabelledName,
                new Dictionary<string, string>());
            createdIds.AddRange([matchingId, otherId, unlabelledId]);

            var ryukWasRunning = await IsContainerRunningAsync(ryukId);
            registration.Dispose();

            await WaitUntilAsync(
                async () => !await ContainerListedAsync(matchingId),
                TimeSpan.FromSeconds(60),
                "Ryuk did not remove the matching stopped resource.");

            var matchingRemoved = !await ContainerExistsAsync(matchingId);
            var otherSurvived = await ContainerExistsAsync(otherId);
            var unlabelledSurvived = await ContainerExistsAsync(unlabelledId);
            await WaitUntilAsync(
                async () => !await ContainerListedAsync(ryukId) || !await IsContainerRunningAsync(ryukId),
                TimeSpan.FromSeconds(30),
                "Ryuk did not exit after completing its cleanup pass.");
            await DeleteContainerIfPresentAsync(ryukId);

            var ryukRemoved = !await ContainerListedAsync(ryukId);

            return new RyukCleanupResult(
                ryukWasRunning,
                matchingRemoved,
                otherSurvived,
                unlabelledSurvived,
                ryukRemoved);
        }
        finally
        {
            foreach (var id in createdIds)
            {
                await DeleteContainerIfPresentAsync(id);
            }
        }
    }

    public async Task<AspireLifecycleResult> RunAspireLifecycleScenarioAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var networkName = $"aspire-session-network-{suffix}";
        var containerName = $"aspire-smoke-{suffix}";
        string? containerId = null;
        var networkCreated = false;

        try
        {
            using (var pullResponse = await dockerClient.PostAsync("/images/create?fromImage=alpine&tag=3.20", null))
            {
                pullResponse.EnsureSuccessStatusCode();
                var pullBody = await pullResponse.Content.ReadAsStringAsync();
                if (!pullBody.EndsWith('\n'))
                {
                    throw new InvalidOperationException("The image pull response was not a Docker JSON stream.");
                }
            }

            using (var createNetworkResponse = await dockerClient.PostAsJsonAsync(
                       "/networks/create",
                       new DockerResourceCreateRequest { Name = networkName, Driver = "bridge" }))
            {
                createNetworkResponse.EnsureSuccessStatusCode();
            }

            networkCreated = true;
            using (var createContainerResponse = await dockerClient.PostAsJsonAsync(
                       $"/containers/create?name={Uri.EscapeDataString(containerName)}",
                       new DockerContainerCreateRequest
                       {
                           Image = "alpine:3.20",
                           Entrypoint = ["/bin/sh", "-c"],
                           Cmd = ["while true; do sleep 1; done"],
                           Labels = new Dictionary<string, string>
                           {
                               ["com.microsoft.developer-service"] = "true"
                           },
                           HostConfig = new DockerHostConfig { NetworkMode = "bridge" }
                       }))
            {
                createContainerResponse.EnsureSuccessStatusCode();
                var created = await createContainerResponse.Content.ReadFromJsonAsync<DockerCreateContainerResponse>();
                containerId = created?.Id ?? throw new InvalidOperationException("The shim returned no container ID.");
            }

            using (var disconnectResponse = await dockerClient.PostAsJsonAsync(
                       "/networks/bridge/disconnect",
                       new DockerNetworkDisconnectRequest { Container = containerId, Force = true }))
            {
                disconnectResponse.EnsureSuccessStatusCode();
            }

            using (var connectResponse = await dockerClient.PostAsJsonAsync(
                       $"/networks/{Uri.EscapeDataString(networkName)}/connect",
                       new DockerNetworkConnectRequest
                       {
                           Container = containerId,
                           EndpointConfig = new DockerEndpointSettings
                           {
                               Aliases = [containerName, $"{containerName}.dev.internal"]
                           }
                       }))
            {
                connectResponse.EnsureSuccessStatusCode();
            }

            using var containerInspect = JsonDocument.Parse(
                await dockerClient.GetStringAsync($"/containers/{Uri.EscapeDataString(containerId)}/json"));
            var networks = containerInspect.RootElement
                .GetProperty("NetworkSettings")
                .GetProperty("Networks");
            var bridgeHidden = !networks.TryGetProperty("bridge", out _);
            var aspireNetworkVisible = networks.TryGetProperty(networkName, out var network) &&
                                       network.GetProperty("Aliases").EnumerateArray().Any(alias =>
                                           string.Equals(alias.GetString(), containerName, StringComparison.Ordinal));

            using var networkInspect = JsonDocument.Parse(
                await dockerClient.GetStringAsync($"/networks/{Uri.EscapeDataString(networkName)}"));
            var networkListsContainer = networkInspect.RootElement
                .GetProperty("Containers")
                .TryGetProperty(containerId, out _);

            using (var startResponse = await dockerClient.PostAsync(
                       $"/containers/{Uri.EscapeDataString(containerId)}/start",
                       null))
            {
                startResponse.EnsureSuccessStatusCode();
            }

            await WaitUntilAsync(
                () => IsContainerRunningAsync(containerId),
                TimeSpan.FromSeconds(30),
                "The Aspire-shaped container did not start.");

            using (var stopResponse = await dockerClient.PostAsync(
                       $"/containers/{Uri.EscapeDataString(containerId)}/stop?t=10",
                       null))
            {
                stopResponse.EnsureSuccessStatusCode();
            }

            var stopped = !await IsContainerRunningAsync(containerId);
            return new AspireLifecycleResult(
                bridgeHidden,
                aspireNetworkVisible,
                networkListsContainer,
                stopped);
        }
        finally
        {
            if (containerId is not null)
            {
                await DeleteContainerIfPresentAsync(containerId);
            }

            if (networkCreated)
            {
                using var deleteNetworkResponse = await dockerClient.DeleteAsync(
                    $"/networks/{Uri.EscapeDataString(networkName)}");
                if (deleteNetworkResponse.StatusCode is not (HttpStatusCode.NoContent or HttpStatusCode.NotFound))
                {
                    deleteNetworkResponse.EnsureSuccessStatusCode();
                }
            }
        }
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
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(BuildConfiguration);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--full-api-port");
        startInfo.ArgumentList.Add(FullApiPort.ToString());
        startInfo.ArgumentList.Add("--ryuk-api-port");
        startInfo.ArgumentList.Add(RyukApiPort.ToString());
        startInfo.ArgumentList.Add("--wslc-host-address");
        startInfo.ArgumentList.Add(WslcHostAddress);

        startInfo.Environment["TESTCONTAINERS_RYUK_DISABLED"] = "false";

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

    private async Task<string> CreateStoppedContainerAsync(
        string name,
        IReadOnlyDictionary<string, string> labels)
    {
        var response = await dockerClient.PostAsJsonAsync(
            $"/containers/create?name={Uri.EscapeDataString(name)}",
            new DockerContainerCreateRequest
            {
                Image = "mcr.microsoft.com/mssql/server:2022-latest",
                Labels = labels
            });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<DockerCreateContainerResponse>();
        var id = created?.Id ?? throw new InvalidOperationException("The shim returned no container ID.");
        using var stopResponse = await dockerClient.PostAsync($"/containers/{Uri.EscapeDataString(id)}/stop", content: null);
        stopResponse.EnsureSuccessStatusCode();
        return id;
    }

    private async Task<string> CreateRyukContainerAsync(string session, int hostPort)
    {
        var response = await dockerClient.PostAsJsonAsync(
            $"/containers/create?name=testcontainers-ryuk-{session}",
            new DockerContainerCreateRequest
            {
                Image = "testcontainers/ryuk:0.14.0",
                Env = ["RYUK_CONNECTION_TIMEOUT=30s", "RYUK_RECONNECTION_TIMEOUT=10s"],
                Labels = new Dictionary<string, string>
                {
                    ["org.testcontainers"] = "true",
                    ["org.testcontainers.session-id"] = session,
                    [ResourceReaper.ResourceReaperSessionLabel] = Guid.Empty.ToString("D")
                },
                ExposedPorts = new Dictionary<string, object?> { ["8080/tcp"] = new() },
                HostConfig = new DockerHostConfig
                {
                    AutoRemove = true,
                    Privileged = true,
                    PortBindings = new Dictionary<string, IReadOnlyList<DockerPortBinding>>
                    {
                        ["8080/tcp"] = [new DockerPortBinding("127.0.0.1", hostPort.ToString())]
                    },
                    Mounts =
                    [
                        new DockerMount
                        {
                            Type = "bind",
                            Source = "/var/run/docker.sock",
                            Target = "/var/run/docker.sock",
                            ReadOnly = true
                        }
                    ]
                }
            });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<DockerCreateContainerResponse>();
        return created?.Id ?? throw new InvalidOperationException("The shim returned no Ryuk container ID.");
    }

    private static async Task<TcpClient> RegisterWithRyukAsync(int port, string session)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var registrationValue = Uri.EscapeDataString(
            $"{ResourceReaper.ResourceReaperSessionLabel}={session}");
        var registrationBytes = System.Text.Encoding.UTF8.GetBytes($"label={registrationValue}\n");
        while (DateTimeOffset.UtcNow < deadline)
        {
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                await client.GetStream().WriteAsync(registrationBytes);
                await client.GetStream().FlushAsync();
                using var reader = new StreamReader(client.GetStream(), leaveOpen: true);
                var acknowledgement = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
                if (string.Equals(acknowledgement, "ACK", StringComparison.Ordinal))
                {
                    return client;
                }
            }
            catch (Exception exception) when (exception is SocketException or IOException or TimeoutException)
            {
            }

            client.Dispose();
            await Task.Delay(250);
        }

        throw new TimeoutException("Ryuk did not acknowledge its cleanup filter in time.");
    }

    private async Task<bool> ContainerExistsAsync(string id)
    {
        using var response = await dockerClient.GetAsync($"/containers/{Uri.EscapeDataString(id)}/json");
        return response.StatusCode == HttpStatusCode.OK;
    }

    private async Task<bool> ContainerListedAsync(string id)
    {
        using var document = JsonDocument.Parse(await dockerClient.GetStringAsync("/containers/json?all=true"));
        return document.RootElement.EnumerateArray().Any(container =>
            container.TryGetProperty("id", out var idProperty) &&
            string.Equals(idProperty.GetString(), id, StringComparison.Ordinal));
    }

    private async Task<bool> IsContainerRunningAsync(string id)
    {
        using var response = await dockerClient.GetAsync($"/containers/{Uri.EscapeDataString(id)}/json");
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("State", out var state) &&
               state.TryGetProperty("Running", out var running) &&
               running.ValueKind == JsonValueKind.True;
    }

    private async Task DeleteContainerIfPresentAsync(string id)
    {
        if (!await ContainerExistsAsync(id))
        {
            return;
        }

        using var response = await dockerClient.DeleteAsync($"/containers/{Uri.EscapeDataString(id)}?force=true");
        if (response.StatusCode is not (HttpStatusCode.NoContent or HttpStatusCode.NotFound) &&
            await ContainerExistsAsync(id))
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task WaitUntilAsync(
        Func<Task<bool>> predicate,
        TimeSpan timeout,
        string timeoutMessage)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await predicate())
            {
                return;
            }

            if (process is { HasExited: true })
            {
                throw new InvalidOperationException($"Shim exited during Ryuk cleanup.{Environment.NewLine}{ReadOutput()}");
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"{timeoutMessage}{Environment.NewLine}{ReadOutput()}");
    }
}

public sealed record RyukCleanupResult(
    bool RyukWasRunning,
    bool MatchingResourceRemoved,
    bool OtherSessionResourceSurvived,
    bool UnlabelledResourceSurvived,
    bool RyukRemoved);

public sealed record AspireLifecycleResult(
    bool BridgeHidden,
    bool AspireNetworkVisible,
    bool NetworkListsContainer,
    bool ContainerStopped);

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
