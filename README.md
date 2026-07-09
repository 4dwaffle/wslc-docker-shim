# wslc-docker-shim

Compatible layer to allow using Testcontainers with WSLc.

## Run

```powershell
dotnet run --project src\Testcontainers.WslcShim -- --wslc-host-address 127.0.0.1
```

Point Docker clients at the full API listener:

```powershell
$env:DOCKER_HOST = "tcp://127.0.0.1:23755"
```

Ryuk should remain enabled. When the shim sees a `testcontainers/ryuk` container create request, it removes the unusable Docker socket bind mount and injects `DOCKER_HOST` pointing at the restricted Ryuk listener.

## Listeners

- Full API: `127.0.0.1:23755`
- Restricted Ryuk API: random port per process, advertised to Ryuk through `DOCKER_HOST`

The restricted listener supports only `_ping`, `version`, label-filtered list endpoints, and deletes that match `org.testcontainers=true` plus the active `org.testcontainers.session-id`.

## Implemented API subset

- `GET /_ping`
- `GET /version`
- `POST /images/create`
- `GET /images/json`
- `DELETE /images/{id}`
- `POST /containers/create`
- `POST /containers/{id}/start`
- `POST /containers/{id}/stop`
- `GET /containers/{id}/json`
- `GET /containers/{id}/logs`
- `GET /containers/json`
- `DELETE /containers/{id}`
- `POST /networks/create`
- `GET /networks`
- `GET /networks/{id}`
- `DELETE /networks/{id}`
- `POST /volumes/create`
- `GET /volumes`
- `GET /volumes/{id}`
- `DELETE /volumes/{id}`

Build, exec, attach, archive copy/read, and Docker-compatible streaming behavior are still future work.
