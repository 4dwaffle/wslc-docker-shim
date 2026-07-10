namespace Testcontainers.WslcShim.Wslc.Models;

internal sealed record WslcNetworkSelection(string? Network, IReadOnlyList<string> Aliases);
