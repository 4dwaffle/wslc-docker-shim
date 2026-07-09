using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Testcontainers.WslcShim.Docker;

namespace Testcontainers.WslcShim.Wslc;

public sealed class WslcCliDockerBackend(IWslcProcessRunner processRunner) : IWslcDockerBackend
{
    private static readonly TimeSpan WaitPollInterval = TimeSpan.FromMilliseconds(250);
    private readonly ConcurrentDictionary<string, DockerExecState> execs = new(StringComparer.Ordinal);

    public async Task<DockerCreateContainerResponse> CreateContainerAsync(
        DockerContainerCreateRequest request,
        bool isRyuk,
        CancellationToken cancellationToken)
    {
        var cidFile = Path.Combine(Path.GetTempPath(), $"wslc-shim-{Guid.NewGuid():N}.cid");
        try
        {
            var command = WslcCommandBuilder.BuildCreateContainerCommand(request, cidFile);
            var result = await processRunner.RunAsync(command, cancellationToken);
            EnsureSuccess(command, result);

            var id = File.Exists(cidFile)
                ? (await File.ReadAllTextAsync(cidFile, cancellationToken)).Trim()
                : result.StandardOutput.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                id = request.Name ?? string.Empty;
            }

            return new DockerCreateContainerResponse(id);
        }
        finally
        {
            if (File.Exists(cidFile))
            {
                File.Delete(cidFile);
            }
        }
    }

    public async Task<IReadOnlyList<DockerResourceSnapshot>> ListResourcesAsync(
        DockerResourceKind kind,
        DockerLabelFilters filters,
        CancellationToken cancellationToken)
    {
        var command = WslcCommandBuilder.BuildListResourcesCommand(kind);
        var result = await processRunner.RunAsync(command, cancellationToken);
        EnsureSuccess(command, result);

        using var document = JsonDocument.Parse(result.StandardOutput);
        var resources = new List<DockerResourceSnapshot>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = GetListResourceIdentifier(kind, item);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var snapshot = await InspectResourceAsync(kind, id, cancellationToken);
            if (snapshot is not null && filters.Matches(snapshot.Labels))
            {
                resources.Add(snapshot);
            }
        }

        return resources;
    }

    public async Task<DockerResourceSnapshot?> InspectResourceAsync(
        DockerResourceKind kind,
        string id,
        CancellationToken cancellationToken)
    {
        var json = await InspectResourceJsonAsync(kind, id, cancellationToken);
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var labels = ExtractLabels(root);
        var resourceId = GetStringProperty(root, "Id") ?? id;
        var name = GetResourceName(kind, root);
        return new DockerResourceSnapshot(resourceId, labels, name);
    }

    public async Task<string?> InspectResourceJsonAsync(
        DockerResourceKind kind,
        string id,
        CancellationToken cancellationToken)
    {
        var command = WslcCommandBuilder.BuildInspectResourceCommand(kind, id);
        var result = await processRunner.RunAsync(command, cancellationToken);
        if (result.ExitCode != 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        var rawJson = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().FirstOrDefault().GetRawText()
            : document.RootElement.GetRawText();

        return kind == DockerResourceKind.Container
            ? NormalizeContainerInspectJson(rawJson)
            : rawJson;
    }

    public async Task DeleteResourceAsync(
        DockerResourceKind kind,
        string id,
        CancellationToken cancellationToken)
    {
        var command = WslcCommandBuilder.BuildDeleteResourceCommand(kind, id);
        var result = await processRunner.RunAsync(command, cancellationToken);
        EnsureSuccess(command, result);
    }

    public async Task StartContainerAsync(string id, CancellationToken cancellationToken)
    {
        var command = WslcCommandBuilder.BuildStartContainerCommand(id);
        var result = await processRunner.RunAsync(command, cancellationToken);
        EnsureSuccess(command, result);
    }

    public async Task StopContainerAsync(string id, CancellationToken cancellationToken)
    {
        var command = WslcCommandBuilder.BuildStopContainerCommand(id);
        var result = await processRunner.RunAsync(command, cancellationToken);
        EnsureSuccess(command, result);
    }

    public async Task<string> GetContainerLogsAsync(
        string id,
        DockerLogRequest request,
        CancellationToken cancellationToken)
    {
        var command = WslcCommandBuilder.BuildLogsCommand(id, request);
        var result = await processRunner.RunAsync(command, cancellationToken);
        EnsureSuccess(command, result);
        return result.StandardOutput;
    }

    public async Task<DockerWaitContainerResponse?> WaitContainerAsync(
        string id,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var json = await InspectResourceJsonAsync(DockerResourceKind.Container, id, cancellationToken);
            if (json is null)
            {
                return null;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("State", out var state) ||
                state.ValueKind != JsonValueKind.Object)
            {
                return new DockerWaitContainerResponse(0);
            }

            var isRunning = GetBooleanProperty(state, "Running") ?? false;
            if (!isRunning)
            {
                return new DockerWaitContainerResponse(GetInt64Property(state, "ExitCode") ?? 0);
            }

            await Task.Delay(WaitPollInterval, cancellationToken);
        }
    }

    public Task<DockerExecCreateResponse> CreateContainerExecAsync(
        string containerId,
        DockerExecCreateRequest request,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        execs[id] = new DockerExecState(containerId, request, null);
        return Task.FromResult(new DockerExecCreateResponse(id));
    }

    public async Task<DockerExecStartResponse?> StartExecAsync(
        string id,
        DockerExecStartRequest request,
        CancellationToken cancellationToken)
    {
        if (!execs.TryGetValue(id, out var state))
        {
            return null;
        }

        var command = WslcCommandBuilder.BuildExecCommand(state.ContainerId, state.Request);
        var result = await processRunner.RunAsync(command, cancellationToken);
        execs[id] = state with { ExitCode = result.ExitCode };
        return new DockerExecStartResponse(result.StandardOutput + result.StandardError, result.ExitCode);
    }

    public Task<DockerExecInspectResponse?> InspectExecAsync(string id, CancellationToken cancellationToken)
    {
        return Task.FromResult(execs.TryGetValue(id, out var state)
            ? new DockerExecInspectResponse(id, false, state.ExitCode)
            : null);
    }

    public async Task PullImageAsync(string image, CancellationToken cancellationToken)
    {
        var command = WslcCommandBuilder.BuildPullImageCommand(image);
        var result = await processRunner.RunAsync(command, cancellationToken);
        EnsureSuccess(command, result);
    }

    public async Task<DockerResourceSnapshot> CreateResourceAsync(
        DockerResourceKind kind,
        DockerResourceCreateRequest request,
        CancellationToken cancellationToken)
    {
        var command = WslcCommandBuilder.BuildCreateResourceCommand(kind, request);
        var result = await processRunner.RunAsync(command, cancellationToken);
        EnsureSuccess(command, result);

        var name = request.Name ?? result.StandardOutput.Trim();
        return new DockerResourceSnapshot(name, request.Labels, name);
    }

    private static void EnsureSuccess(WslcCommand command, WslcCommandResult result)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        throw new WslcCommandException(command, result);
    }

    private static IReadOnlyDictionary<string, string> ExtractLabels(JsonElement element)
    {
        if (!element.TryGetProperty("Labels", out var labels) &&
            (!element.TryGetProperty("Config", out var config) ||
             !config.TryGetProperty("Labels", out labels)))
        {
            return new Dictionary<string, string>();
        }

        if (labels.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var label in labels.EnumerateObject())
        {
            if (label.Value.ValueKind == JsonValueKind.String)
            {
                result[label.Name] = label.Value.GetString() ?? string.Empty;
            }
        }

        return result;
    }

    private static string? GetListResourceIdentifier(DockerResourceKind kind, JsonElement element)
    {
        if (kind == DockerResourceKind.Image)
        {
            var repository = GetStringProperty(element, "Repository");
            var tag = GetStringProperty(element, "Tag");
            if (!string.IsNullOrWhiteSpace(repository) &&
                !string.Equals(repository, "<none>", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(tag) &&
                !string.Equals(tag, "<none>", StringComparison.Ordinal))
            {
                return $"{repository}:{tag}";
            }
        }

        return GetStringProperty(element, "Id") ??
               GetStringProperty(element, "ID") ??
               GetStringProperty(element, "Name");
    }

    private static string? GetResourceName(DockerResourceKind kind, JsonElement element)
    {
        if (kind == DockerResourceKind.Image &&
            element.TryGetProperty("RepoTags", out var repoTags) &&
            repoTags.ValueKind == JsonValueKind.Array)
        {
            return repoTags.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        return GetStringProperty(element, "Name");
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static long? GetInt64Property(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt64()
            : null;
    }

    private static bool? GetBooleanProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }

    private static string NormalizeContainerInspectJson(string rawJson)
    {
        var node = JsonNode.Parse(rawJson)?.AsObject();
        if (node is null)
        {
            return rawJson;
        }

        if (node["Ports"] is JsonNode ports)
        {
            var networkSettings = node["NetworkSettings"]?.AsObject();
            if (networkSettings is null)
            {
                networkSettings = [];
                node["NetworkSettings"] = networkSettings;
            }

            networkSettings["Ports"] ??= ports.DeepClone();
        }

        return node.ToJsonString();
    }

    private sealed record DockerExecState(
        string ContainerId,
        DockerExecCreateRequest Request,
        long? ExitCode);
}
