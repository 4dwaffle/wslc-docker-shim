namespace Testcontainers.WslcShim.Wslc;

public sealed record WslcCommand(string FileName, IReadOnlyList<string> Arguments);
