namespace Testcontainers.WslcShim;

internal sealed record ShimLaunchOptions(
    bool WatchEnabled,
    string[] ApplicationArguments)
{
    private const string WatchOption = "--watch";

    public static ShimLaunchOptions Parse(IEnumerable<string> arguments)
    {
        var watchEnabled = false;
        var applicationArguments = new List<string>();

        foreach (var argument in arguments)
        {
            if (string.Equals(argument, WatchOption, StringComparison.OrdinalIgnoreCase))
            {
                watchEnabled = true;
                continue;
            }

            applicationArguments.Add(argument);
        }

        return new ShimLaunchOptions(watchEnabled, applicationArguments.ToArray());
    }

    public string? GetValidationError(bool interactiveTerminal) =>
        WatchEnabled && !interactiveTerminal
            ? "Option --watch requires an interactive terminal."
            : null;
}
