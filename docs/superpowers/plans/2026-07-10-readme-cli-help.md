# Concise README and CLI Help Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace duplicated README option reference material with real `--help`/`-h` output and reduce the README to essential setup, security, and compatibility guidance.

**Architecture:** Add a small internal CLI helper that recognizes help arguments and writes static help text before ASP.NET Core constructs the web host. Keep runtime configuration unchanged, test the helper directly, and rewrite the README around the shortest successful Testcontainers setup.

**Tech Stack:** .NET 10, ASP.NET Core top-level program, xUnit, Markdown, PowerShell

---

## File structure

- Create `src/Testcontainers.WslcShim/Cli/CliHelp.cs` to own help detection and output.
- Modify `src/Testcontainers.WslcShim/Program.cs` to exit before host construction when help is requested.
- Create `tests/Testcontainers.WslcShim.Tests/CliHelpTests.cs` to verify both help aliases, all options, and non-help arguments.
- Modify `README.md` to contain only adoption-critical documentation.

### Task 1: Implement dependency-free CLI help

**Files:**
- Create: `tests/Testcontainers.WslcShim.Tests/CliHelpTests.cs`
- Create: `src/Testcontainers.WslcShim/Cli/CliHelp.cs`
- Modify: `src/Testcontainers.WslcShim/Program.cs`

- [ ] **Step 1: Write the failing CLI help tests**

Create `tests/Testcontainers.WslcShim.Tests/CliHelpTests.cs`:

```csharp
using Testcontainers.WslcShim.Cli;

namespace Testcontainers.WslcShim.Tests;

public sealed class CliHelpTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void TryWrite_writes_help_for_supported_alias(string argument)
    {
        using var output = new StringWriter();

        var handled = CliHelp.TryWrite([argument], output);

        Assert.True(handled);
        var help = output.ToString();
        Assert.Contains("Usage:", help);
        Assert.Contains("--full-api-address", help);
        Assert.Contains("--full-api-port", help);
        Assert.Contains("--wslc-host-address", help);
        Assert.Contains("--ryuk-bind-address", help);
        Assert.Contains("--ryuk-api-port", help);
    }

    [Fact]
    public void TryWrite_ignores_arguments_without_help()
    {
        using var output = new StringWriter();

        var handled = CliHelp.TryWrite(["--full-api-port", "12345"], output);

        Assert.False(handled);
        Assert.Equal(string.Empty, output.ToString());
    }
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
dotnet test tests\Testcontainers.WslcShim.Tests\Testcontainers.WslcShim.Tests.csproj --filter FullyQualifiedName~CliHelpTests
```

Expected: compilation fails because `Testcontainers.WslcShim.Cli.CliHelp` does not exist.

- [ ] **Step 3: Implement the CLI helper**

Create `src/Testcontainers.WslcShim/Cli/CliHelp.cs`:

```csharp
namespace Testcontainers.WslcShim.Cli;

internal static class CliHelp
{
    private const string HelpText = """
        wslc-docker-shim - Docker Engine API compatibility layer for Testcontainers on WSLc.

        Usage:
          Testcontainers.WslcShim [options]

        Options:
          -h, --help
              Show this help and exit.

          --full-api-address <address>
              Address for the Testcontainers-facing listener.
              Default: 127.0.0.1. Keep this listener on loopback.

          --full-api-port <port>
              Port for the Testcontainers-facing listener.
              Default: 23755.

          --wslc-host-address <address>
              Windows address advertised to the Ryuk container.
              Default: auto-detect an IPv4 address on a WSL interface,
              then fall back to 127.0.0.1.

          --ryuk-bind-address <address>
              Local bind address for the restricted Ryuk listener.
              Default: 0.0.0.0.

          --ryuk-api-port <port>
              Port for the restricted Ryuk listener.
              Default: a random available port.

        Security:
          Keep the full API listener on loopback. Expose only the restricted Ryuk
          listener to the WSL/WSLc virtual network.
        """;

    public static bool TryWrite(string[] args, TextWriter output)
    {
        if (!args.Any(argument => argument is "--help" or "-h"))
        {
            return false;
        }

        output.WriteLine(HelpText);
        return true;
    }
}
```

- [ ] **Step 4: Exit before constructing the web host**

Replace `src/Testcontainers.WslcShim/Program.cs` with:

```csharp
using Testcontainers.WslcShim.Cli;
using Testcontainers.WslcShim.Http;
using Testcontainers.WslcShim.Wslc;

if (CliHelp.TryWrite(args, Console.Out))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);
var options = ShimRuntimeOptions.FromConfiguration(builder.Configuration);

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Listen(options.FullApiIPAddress, options.FullApiPort);
    serverOptions.Listen(options.RyukBindIPAddress, options.RyukEndpoint.Port);
});

ShimApplication.ConfigureServices(
    builder.Services,
    new WslcCliDockerBackend(new WslcProcessRunner()),
    options,
    new PortListenerClassifier(options));

var app = builder.Build();
app.UseDockerApiVersionPrefix();
ShimApplication.MapRoutes(app);

app.Run();
```

- [ ] **Step 5: Run the focused test and verify it passes**

Run:

```powershell
dotnet test tests\Testcontainers.WslcShim.Tests\Testcontainers.WslcShim.Tests.csproj --filter FullyQualifiedName~CliHelpTests
```

Expected: both test cases pass.

- [ ] **Step 6: Commit the CLI help change**

```powershell
git add src/Testcontainers.WslcShim/Cli/CliHelp.cs src/Testcontainers.WslcShim/Program.cs tests/Testcontainers.WslcShim.Tests/CliHelpTests.cs
git commit -m "feat: add command-line help"
```

### Task 2: Replace the README with the concise adoption guide

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Rewrite the README**

Replace `README.md` with:

````markdown
# wslc-docker-shim

`wslc-docker-shim` translates the Docker Engine API subset used by Testcontainers for .NET into WSLc commands. It enables Linux Testcontainers workloads on Windows without acting as a general-purpose Docker daemon.

## Prerequisites

- Windows with WSLc installed. `wslc.exe` must be on `PATH` or at `%ProgramFiles%\WSL\wslc.exe`.
- The .NET 10 SDK.
- A Windows host address reachable from WSLc containers.

The shim tries to discover an IPv4 address on a network interface whose name contains `WSL`. If WSLc cannot reach that address, pass the correct address with `--wslc-host-address`.

## Run

```powershell
dotnet run --project src\Testcontainers.WslcShim -- --wslc-host-address <windows-host-address>
```

The Testcontainers-facing API listens on `127.0.0.1:23755` by default. Run the following command to see all address and port options:

```powershell
dotnet run --project src\Testcontainers.WslcShim -- --help
```

## Configure Testcontainers

Create `.testcontainers.properties` in your Windows user profile (`C:\Users\{user}\.testcontainers.properties`) with:

```properties
docker.host=tcp://127.0.0.1:23755
```

Testcontainers reads this persistent setting for future sessions, so you do not need to configure each PowerShell window separately.

Alternatively, set `DOCKER_HOST` for the current PowerShell session:

```powershell
$env:DOCKER_HOST = "tcp://127.0.0.1:23755"
```

Keep Ryuk enabled. If it was disabled in your environment, remove that override:

```powershell
Remove-Item Env:TESTCONTAINERS_RYUK_DISABLED -ErrorAction Ignore
```

The shim rewrites Ryuk's create request by removing the unusable Docker socket mount and advertising a restricted cleanup listener through `DOCKER_HOST`.

## Security and compatibility

The full API listener is intended only for the local Testcontainers process and should remain on loopback. The separate Ryuk listener is reachable by WSLc containers but permits only health/version checks, label-filtered resource lists, and cleanup of resources authorized for the active Testcontainers session. Limit any firewall rule to the WSL/WSLc virtual network and the selected Ryuk port.

The full listener can create and remove resources, so do not bind it to `0.0.0.0` or expose it to the local network. The restricted listener rejects create, start, inspect, logs, exec, pull, and unlabelled cleanup requests.

The shim supports the image, container, exec, network, and volume operations needed by Testcontainers where WSLc can represent them. Docker API version prefixes such as `/v1.43` are accepted. Logs and exec output are buffered rather than streamed; build, attach, archive, events, stats, and other Docker Engine features are not implemented.
````

- [ ] **Step 2: Check the README size and diff**

Run:

```powershell
(Get-Content README.md | Measure-Object -Line -Word -Character) | Format-List
git diff -- README.md
```

Expected: 350–450 words, no Tests section, no runtime option table, and no route-by-route matrix or request-lifecycle detail.

- [ ] **Step 3: Commit the README rewrite**

```powershell
git add README.md
git commit -m "docs: streamline setup guide"
```

### Task 3: Verify the complete change

**Files:**
- Verify: `src/Testcontainers.WslcShim/Cli/CliHelp.cs`
- Verify: `src/Testcontainers.WslcShim/Program.cs`
- Verify: `tests/Testcontainers.WslcShim.Tests/CliHelpTests.cs`
- Verify: `README.md`

- [ ] **Step 1: Run the unit test project**

Run:

```powershell
dotnet test tests\Testcontainers.WslcShim.Tests\Testcontainers.WslcShim.Tests.csproj
```

Expected: all unit and protocol-level tests pass.

- [ ] **Step 2: Verify long help exits without starting listeners**

Run:

```powershell
dotnet run --project src\Testcontainers.WslcShim -- --help
```

Expected: exit code 0; output contains `Usage:` and all five runtime options; output does not contain `Now listening on` or `Application started`.

- [ ] **Step 3: Verify short help exits without starting listeners**

Run:

```powershell
dotnet run --project src\Testcontainers.WslcShim -- -h
```

Expected: the same help output and exit behavior as `--help`.

- [ ] **Step 4: Check formatting and repository state**

Run:

```powershell
git diff --check HEAD~2..HEAD
git status --short
```

Expected: no whitespace errors and a clean worktree.
