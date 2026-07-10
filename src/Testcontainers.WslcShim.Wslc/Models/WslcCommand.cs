namespace Testcontainers.WslcShim.Wslc.Models;

public sealed record WslcCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? ActivityName = null);
