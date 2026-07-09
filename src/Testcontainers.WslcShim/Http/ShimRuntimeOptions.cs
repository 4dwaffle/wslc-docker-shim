using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Http;

public sealed class ShimRuntimeOptions
{
    public string FullApiAddress { get; init; } = "127.0.0.1";

    public int FullApiPort { get; init; } = 23755;

    public string RyukBindAddress { get; init; } = "0.0.0.0";

    public RyukListenerEndpoint RyukEndpoint { get; init; } =
        new("127.0.0.1", PortAllocator.GetAvailableTcpPort());

    public static ShimRuntimeOptions FromConfiguration(IConfiguration configuration)
    {
        var fullApiAddress = configuration["full-api-address"] ?? "127.0.0.1";
        var fullApiPort = GetInt(configuration, "full-api-port", 23755);
        var wslcHostAddress = configuration["wslc-host-address"] ?? DetectDefaultWslcHostAddress();
        var ryukBindAddress = configuration["ryuk-bind-address"] ?? "0.0.0.0";
        var ryukPort = GetInt(configuration, "ryuk-api-port", PortAllocator.GetAvailableTcpPort());

        return new ShimRuntimeOptions
        {
            FullApiAddress = fullApiAddress,
            FullApiPort = fullApiPort,
            RyukBindAddress = ryukBindAddress,
            RyukEndpoint = new RyukListenerEndpoint(wslcHostAddress, ryukPort)
        };
    }

    public IPAddress FullApiIPAddress => IPAddress.Parse(FullApiAddress);

    public IPAddress RyukBindIPAddress => IPAddress.Parse(RyukBindAddress);

    private static int GetInt(IConfiguration configuration, string key, int defaultValue)
    {
        return int.TryParse(configuration[key], out var value) ? value : defaultValue;
    }

    private static string DetectDefaultWslcHostAddress()
    {
        var wslInterfaceAddress = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up &&
                networkInterface.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase))
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address.ToString())
            .FirstOrDefault(address => !IPAddress.IsLoopback(IPAddress.Parse(address)));

        return wslInterfaceAddress ?? "127.0.0.1";
    }
}
