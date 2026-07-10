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

Create `.testcontainers.properties` in your Windows user profile (for example, `%USERPROFILE%\.testcontainers.properties`) with:

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

## Watch activity

For an app-like view while exercising the shim, add `--watch`:

```powershell
dotnet run --project src\Testcontainers.WslcShim -- --watch --wslc-host-address <windows-host-address>
```

Watch mode prints the configured endpoints and a scrolling, correlated trace:

```text
12:04:11.231  0001  HTTP  -> FULL POST    /v1.43/containers/create
12:04:11.238  0001  WSLC  -> create container test-db
12:04:11.710  0001  WSLC  OK create container test-db  exit=0  472ms
12:04:11.716  0001  HTTP  <- 201 POST    /v1.43/containers/create  485ms
```

The trace includes request paths, status codes, durations, and safe operation descriptions. It does not print query strings, request bodies, environment and label values, exec arguments, or WSLc output. Colors are disabled automatically when output is redirected or `NO_COLOR` is set.

`--watch` observes runtime activity; it does not reload the application when source files change.

## Security and compatibility

The full API listener is intended only for the local Testcontainers process and should remain on loopback. The separate Ryuk listener is reachable by WSLc containers but permits only health/version checks, label-filtered resource lists, and cleanup of resources authorized for the active Testcontainers session. Limit any firewall rule to the WSL/WSLc virtual network and the selected Ryuk port.

The full listener can create and remove resources, so do not bind it to `0.0.0.0` or expose it to the local network. The restricted listener rejects create, start, inspect, logs, exec, pull, and unlabelled cleanup requests.

The shim supports the image, container, exec, network, and volume operations needed by Testcontainers where WSLc can represent them. Docker API version prefixes such as `/v1.43` are accepted. Logs and exec output are buffered rather than streamed; build, attach, archive, events, stats, and other Docker Engine features are not implemented.
