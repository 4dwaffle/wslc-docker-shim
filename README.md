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

## Security and compatibility

The full API listener is intended only for the local Testcontainers process and should remain on loopback. The separate Ryuk listener is reachable by WSLc containers but permits only health/version checks, label-filtered resource lists, and cleanup of resources authorized for the active Testcontainers session. Limit any firewall rule to the WSL/WSLc virtual network and the selected Ryuk port.

The full listener can create and remove resources, so do not bind it to `0.0.0.0` or expose it to the local network. The restricted listener rejects create, start, inspect, logs, exec, pull, and unlabelled cleanup requests.

<<<<<<< HEAD
## Listener security boundary

The full listener is intended for the local Testcontainers process. The restricted listener is advertised only to Ryuk and exposes:

- `GET /_ping`
- `GET /version`
- label-filtered container, network, volume, and image list endpoints
- removal of resources previously authorized for that Ryuk cleanup session

`GET /info`, create, start, stop, inspect, logs, wait, exec, pull, and resource-create routes return `404` on the restricted listener. Cleanup requests are constrained to the active Testcontainers resource-reaper session; unlabelled resources and resources from another session are rejected.

## Implemented API subset

All routes accept an optional negotiated Docker API prefix such as `/v1.43`.

| Area | Implemented routes | Compatibility |
| --- | --- | --- |
| System | `GET /_ping`, `GET /version`, `GET /info` | Implemented. `/info` is full-listener-only. |
| Images | `POST /images/create`, `GET /images/json`, `GET /images/{name}/json`, `DELETE /images/{name}` | Partial. Pull returns a single status object instead of Docker's progress stream. |
| Containers | `POST /containers/create`, `POST /containers/{id}/start`, `POST /containers/{id}/stop`, `POST /containers/{id}/wait`, `GET /containers/{id}/json`, `GET /containers/{id}/logs`, `GET /containers/json`, `DELETE /containers/{id}` | Partial. The create request is limited to settings WSLc can represent. Logs are returned as a buffered Docker raw-stream response; streaming follow is not implemented. |
| Exec | `POST /containers/{id}/exec`, `POST /exec/{id}/start`, `GET /exec/{id}/json` | Partial. Output is buffered; attached stdin and bidirectional streaming are not implemented. Completed exec inspection is retained for a bounded compatibility window. |
| Networks | `POST /networks/create`, `GET /networks`, `GET /networks/{id}`, `DELETE /networks/{id}` | Implemented for the fields used by Testcontainers. |
| Volumes | `POST /volumes/create`, `GET /volumes`, `GET /volumes/{id}`, `DELETE /volumes/{id}` | Implemented for the fields used by Testcontainers. |

Label filters are supported for resource lists. Other Docker filter types are not implemented. Attach, archive copy/read, build, events, stats, and the rest of the Docker Engine API are not implemented.

## Request lifecycle guarantees

Cancelling an HTTP request terminates the spawned WSLc process and its child-process tree, then drains redirected output before returning cancellation to the caller. Cancellation cannot roll back a WSLc side effect that completed before the process was terminated; after cancelling a mutating request, callers should inspect the resource before retrying.

Completed exec inspection state is retained for five minutes, subject to a hard limit of 1,024 completed entries. Under unusually high exec volume, the oldest completed entries are evicted first and may disappear before five minutes; newly completed entries remain inspectable. Cancelled and failed starts enter the same bounded retention cache with an unknown exit code. Created-but-not-started and currently running execs are intentionally not counted because they are still live Docker handles; evicting one would turn a valid later start or inspect into `404`.

## Tests

Run the unit and protocol-level endpoint tests without requiring WSLc:

```powershell
dotnet test tests\Testcontainers.WslcShim.Docker.Tests\Testcontainers.WslcShim.Docker.Tests.csproj
dotnet test tests\Testcontainers.WslcShim.Wslc.Tests\Testcontainers.WslcShim.Wslc.Tests.csproj
dotnet test tests\Testcontainers.WslcShim.Ryuk.Tests\Testcontainers.WslcShim.Ryuk.Tests.csproj
dotnet test tests\Testcontainers.WslcShim.Http.Tests\Testcontainers.WslcShim.Http.Tests.csproj
```

The integration suite starts the production two-listener shim and real Testcontainers/Ryuk workloads through WSLc. It requires WSLc, network access to pull the MSSQL and Ryuk images, and a host address reachable from WSLc. Override address detection when necessary:

```powershell
$env:WSLC_SHIM_TEST_HOST_ADDRESS = "<windows-host-address>"
dotnet test tests\Testcontainers.WslcShim.IntegrationTests\Testcontainers.WslcShim.IntegrationTests.csproj
```

The integration tests control the Testcontainers settings they require. Avoid running unrelated Testcontainers workloads against the same shim ports while the suite is active.
=======
The shim supports the image, container, exec, network, and volume operations needed by Testcontainers where WSLc can represent them. Docker API version prefixes such as `/v1.43` are accepted. Logs and exec output are buffered rather than streamed; build, attach, archive, events, stats, and other Docker Engine features are not implemented.
>>>>>>> origin/main
