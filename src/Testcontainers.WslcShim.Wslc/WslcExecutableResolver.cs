namespace Testcontainers.WslcShim.Wslc;

internal static class WslcExecutableResolver
{
    private const string WslcFileName = "wslc";
    private const string WslcWindowsFileName = "wslc.exe";

    public static string Resolve(string fileName)
    {
        return Resolve(
            fileName,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            File.Exists);
    }

    internal static string Resolve(
        string fileName,
        string? pathEnvironmentVariable,
        string? programFilesDirectory,
        Func<string, bool> fileExists)
    {
        if (Path.IsPathFullyQualified(fileName))
        {
            return fileName;
        }

        foreach (var directory in SplitPath(pathEnvironmentVariable))
        {
            foreach (var candidateFileName in GetCandidateFileNames(fileName))
            {
                var candidate = Path.Combine(directory, candidateFileName);
                if (fileExists(candidate))
                {
                    return candidate;
                }
            }
        }

        if (IsWslc(fileName) && !string.IsNullOrWhiteSpace(programFilesDirectory))
        {
            var installedWslc = Path.Combine(programFilesDirectory, "WSL", WslcWindowsFileName);
            if (fileExists(installedWslc))
            {
                return installedWslc;
            }
        }

        return fileName;
    }

    private static IEnumerable<string> SplitPath(string? pathEnvironmentVariable)
    {
        return (pathEnvironmentVariable ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => directory.Trim('"'))
            .Where(directory => !string.IsNullOrWhiteSpace(directory));
    }

    private static IEnumerable<string> GetCandidateFileNames(string fileName)
    {
        yield return fileName;

        if (OperatingSystem.IsWindows() && string.IsNullOrEmpty(Path.GetExtension(fileName)))
        {
            yield return fileName + ".exe";
        }
    }

    private static bool IsWslc(string fileName)
    {
        return string.Equals(fileName, WslcFileName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, WslcWindowsFileName, StringComparison.OrdinalIgnoreCase);
    }
}
