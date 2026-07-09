using System.Net;
using System.Net.Sockets;

namespace Testcontainers.WslcShim.Http;

public static class PortAllocator
{
    public static int GetAvailableTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
