namespace Testcontainers.WslcShim.Cli;

internal static class CliHelp
{
    private const string HelpText = """
        wslc-docker-shim - Docker Engine API compatibility layer for Testcontainers on WSLc.

        Usage:
          wslc-docker-shim [options]

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

          --watch
              Print a live terminal trace of Docker requests and translated
              WSLc operations. This observes activity; it does not reload source.

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
