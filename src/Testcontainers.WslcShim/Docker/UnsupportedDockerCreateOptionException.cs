namespace Testcontainers.WslcShim.Docker;

public sealed class UnsupportedDockerCreateOptionException(IReadOnlyList<string> optionPaths)
    : NotSupportedException(BuildMessage(optionPaths))
{
    public IReadOnlyList<string> OptionPaths { get; } = optionPaths;

    private static string BuildMessage(IReadOnlyList<string> optionPaths)
    {
        return $"The Docker create request uses options that WSLc cannot represent: {string.Join(", ", optionPaths)}.";
    }
}
