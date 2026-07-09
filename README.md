# wslc-docker-shim

`wslc-docker-shim` exposes the Docker Engine API subset used by Testcontainers for .NET and translates it to WSLc. It is a compatibility layer for Linux containers on Windows, not a general-purpose Docker daemon.

## Prerequisites

- Windows with WSLc installed. `wslc.exe` must be on `PATH` or installed at `%ProgramFiles%\WSL\wslc.exe`.
- The .NET 10 SDK.
- A Windows host address that containers launched by WSLc can reach.
- A firewall rule that permits WSLc containers to reach the restricted Ryuk listener. Do not expose the full API listener beyond loopback.

The shim tries to discover an IPv4 address on a network interface whose name contains `WSL`. If that is not the address visible from WSLc, pass it explicitly with `--wslc-host-address`.

## Run

Restore and start the shim:

```powershell
dotnet restore WslcDockerShim.slnx
dotnet run --project src\Testcontainers.WslcShim -- --wslc-host-address <windows-host-address>
```

Point Testcontainers or another Docker client at the full API listener:

```powershell
$env:DOCKER_HOST = "tcp://127.0.0.1:23755"
Remove-Item Env:TESTCONTAINERS_RYUK_DISABLED -ErrorAction Ignore
```

Ryuk must remain enabled. When the shim receives a `testcontainers/ryuk` create request, it removes the unusable Docker socket bind mount and injects `DOCKER_HOST` for the restricted listener.

Runtime options can be passed after `--`:

| Option | Default | Purpose |
| --- | --- | --- |
| `--full-api-address` | `127.0.0.1` | Address for the Testcontainers-facing listener. Keep this on loopback. |
| `--full-api-port` | `23755` | Port for the full API listener. |
| `--wslc-host-address` | auto-detected, then `127.0.0.1` | Windows address advertised to the Ryuk container. |
| `--ryuk-bind-address` | `0.0.0.0` | Local bind address for the restricted listener. |
| `--ryuk-api-port` | random available port | Port for the restricted listener. |

If the Windows firewall prompts for access, allow only the WSL/WSLc virtual network profile and the selected restricted port. The full API port should remain reachable only from the Windows host.

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
dotnet test tests\Testcontainers.WslcShim.Tests\Testcontainers.WslcShim.Tests.csproj
```

The integration suite starts the production two-listener shim and real Testcontainers/Ryuk workloads through WSLc. It requires WSLc, network access to pull the MSSQL and Ryuk images, and a host address reachable from WSLc. Override address detection when necessary:

```powershell
$env:WSLC_SHIM_TEST_HOST_ADDRESS = "<windows-host-address>"
dotnet test tests\Testcontainers.WslcShim.IntegrationTests\Testcontainers.WslcShim.IntegrationTests.csproj
```

The integration tests control the Testcontainers settings they require. Avoid running unrelated Testcontainers workloads against the same shim ports while the suite is active.
