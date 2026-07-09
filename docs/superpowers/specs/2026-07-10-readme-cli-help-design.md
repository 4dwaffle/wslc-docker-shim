# Concise README and CLI help design

## Goal

Make the project easier to adopt by keeping the README focused on setup and essential safety guidance, while moving runtime option reference material into real command-line help.

## README

Reduce the README from about 840 words to roughly 350–450 words. Keep:

- a short description of the shim and its intended scope;
- prerequisites and WSLc host-address discovery behavior;
- one quick-start command;
- persistent Testcontainers configuration through `C:\Users\{user}\.testcontainers.properties` containing `docker.host=tcp://127.0.0.1:23755`;
- the `DOCKER_HOST` environment variable as a temporary alternative;
- the requirement to keep Ryuk enabled; and
- a concise explanation of the full-listener/restricted-listener security boundary and major API limitations.

Remove the Tests section, runtime option table, route-by-route API matrix, and request/exec lifecycle internals. Refer readers to `--help` for runtime options.

## CLI help

Handle `--help` and `-h` before constructing or starting the ASP.NET Core host. Print a dependency-free help page containing:

- a one-line description;
- usage syntax;
- all five supported runtime options;
- each option's default or discovery behavior; and
- the listener security expectation.

Help must exit successfully without binding ports or starting WSLc processes. Normal startup and existing option handling remain unchanged. Unknown options retain the current ASP.NET Core configuration behavior.

## Verification

Add a focused automated test around the help formatter or early help path, checking that usage and all supported options are present. Also run the existing unit test project and manually invoke both `--help` and `-h` to confirm successful output without server startup.
