using System.Text.Json;

namespace Testcontainers.WslcShim.Docker;

public sealed class DockerLabelFilters
{
    private DockerLabelFilters(IReadOnlyDictionary<string, string?> requiredLabels)
    {
        RequiredLabels = requiredLabels;
    }

    public IReadOnlyDictionary<string, string?> RequiredLabels { get; }

    public static DockerLabelFilters Empty { get; } = new(new Dictionary<string, string?>());

    public static DockerLabelFilters FromDockerFiltersQuery(string? filtersQuery)
    {
        if (string.IsNullOrWhiteSpace(filtersQuery))
        {
            return Empty;
        }

        using var document = JsonDocument.Parse(filtersQuery);
        if (!document.RootElement.TryGetProperty("label", out var labels) ||
            labels.ValueKind != JsonValueKind.Array)
        {
            return Empty;
        }

        var requiredLabels = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var label in labels.EnumerateArray())
        {
            if (label.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = label.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var equalsIndex = value.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex < 0)
            {
                requiredLabels[value] = null;
                continue;
            }

            requiredLabels[value[..equalsIndex]] = value[(equalsIndex + 1)..];
        }

        return new DockerLabelFilters(requiredLabels);
    }

    public bool RequiresLabel(string key)
    {
        return RequiredLabels.TryGetValue(key, out var value) && value is null;
    }

    public bool RequiresLabel(string key, string value)
    {
        return RequiredLabels.TryGetValue(key, out var requiredValue) &&
               string.Equals(requiredValue, value, StringComparison.Ordinal);
    }

    public string? GetRequiredLabelValue(string key)
    {
        return RequiredLabels.TryGetValue(key, out var value) ? value : null;
    }

    public bool Matches(IReadOnlyDictionary<string, string> labels)
    {
        foreach (var requiredLabel in RequiredLabels)
        {
            if (!labels.TryGetValue(requiredLabel.Key, out var labelValue))
            {
                return false;
            }

            if (requiredLabel.Value is not null &&
                !string.Equals(labelValue, requiredLabel.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
