using System.Text.Json;

namespace Testcontainers.WslcShim.Docker.Models;

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
        if (!document.RootElement.TryGetProperty("label", out var labels))
        {
            return Empty;
        }

        var requiredLabels = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (labels.ValueKind == JsonValueKind.Array)
        {
            foreach (var label in labels.EnumerateArray())
            {
                if (label.ValueKind == JsonValueKind.String)
                {
                    AddLabelFilter(requiredLabels, label.GetString());
                }
            }
        }
        else if (labels.ValueKind == JsonValueKind.Object)
        {
            foreach (var label in labels.EnumerateObject())
            {
                if (label.Value.ValueKind == JsonValueKind.True)
                {
                    AddLabelFilter(requiredLabels, label.Name);
                }
            }
        }

        return new DockerLabelFilters(requiredLabels);
    }

    private static void AddLabelFilter(IDictionary<string, string?> requiredLabels, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var equalsIndex = value.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex < 0)
        {
            requiredLabels[value] = null;
            return;
        }

        requiredLabels[value[..equalsIndex]] = value[(equalsIndex + 1)..];
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
