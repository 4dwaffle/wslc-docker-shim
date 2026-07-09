# WSLc Docker API Shim With Ryuk Enabled

## Summary

Build `testcontainers-wslc-shim`, a standalone Windows service or executable that exposes the Docker Engine API subset used by Testcontainers for .NET and translates those calls to WSLc.

Ryuk remains enabled. Users should not set `TESTCONTAINERS_RYUK_DISABLED=true`.

The shim adds special Ryuk compatibility: when Testcontainers creates the Ryuk container, the shim runs Ryuk under WSLc but injects `DOCKER_HOST` so Ryuk talks back to the shim instead of using `/var/run/docker.sock`.

This is viable because Ryuk uses Docker client configuration from the environment, and Ryuk's cleanup model is filter-based Docker resource cleanup.

## Key Changes

Expose two shim listeners:

- Full API listener on `127.0.0.1:23755` for Testcontainers and Docker.DotNet.
- Restricted Ryuk API listener on a WSLc-reachable address and random port.

When the shim receives a Ryuk container create request:

- Detect the image or name matching `testcontainers/ryuk` or `testcontainers-ryuk-*`.
- Strip the unusable `/var/run/docker.sock` bind mount from the WSLc container settings.
- Inject `DOCKER_HOST=tcp://<wslc-reachable-shim-address>:<ryuk-api-port>`.
- Preserve `RYUK_*` environment variables and port `8080`.
- Buffer Ryuk stdout and stderr so Testcontainers' log wait strategy can still see `Started`.

Implement the Docker API subset Ryuk needs on the restricted listener:

- `GET /_ping`
- `GET /version`
- List containers, networks, volumes, and images with Docker label filters.
- Remove containers, networks, volumes, and images.
- Enforce that deletes only affect resources labeled `org.testcontainers=true` and matching the active session label.

Keep the normal Testcontainers-facing API:

- Images
- Containers
- Exec
- Logs
- Attach
- Archive copy and read
- Networks
- Volumes
- Build

## Security

- Do not expose the full Docker-compatible API to WSLc containers.
- The Ryuk-facing listener only supports cleanup endpoints and validates Testcontainers labels before deletion.
- Bind the full API to loopback only.
- Generate a random Ryuk listener port per shim process.

## Test Plan

Run Testcontainers for .NET with:

```powershell
$env:DOCKER_HOST = "tcp://127.0.0.1:23755"
```

Leave Ryuk enabled by default.

Verify:

- Ryuk container starts through the shim.
- Testcontainers connects to Ryuk's mapped port.
- Ryuk receives the session label filter and acknowledges it.
- After the test process exits, Ryuk deletes matching WSLc containers, networks, volumes, and images.
- Non-Testcontainers resources and resources from other sessions are not deleted.

Add failure tests:

- Shim restart with stale resources.
- Ryuk container exits early.
- Restricted Ryuk API refuses unlabelled delete attempts.
- Multiple concurrent test sessions.

## Assumptions

- The shim targets Testcontainers compatibility, not full Docker compatibility.
- WSLc containers can reach one configured Windows-host address. If autodetection is unreliable, expose `--wslc-host-address`.
- Ryuk remains unmodified.
- The first version supports Linux containers on Windows only.

## Sources

- [Moby Ryuk README](https://github.com/testcontainers/moby-ryuk)
- [Ryuk source using Docker environment configuration](https://raw.githubusercontent.com/testcontainers/moby-ryuk/main/reaper.go)
- [Testcontainers for .NET Resource Reaper docs](https://dotnet.testcontainers.org/api/resource_reaper/)
- [WSL container API reference](https://wsl.dev/api-reference/)
