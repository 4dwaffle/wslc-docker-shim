using System.Text.Json.Nodes;
using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.Http;

internal sealed class DockerNetworkAttachmentStore
{
    private readonly object sync = new();
    private readonly Dictionary<string, ContainerState> containers = new(StringComparer.Ordinal);

    public void Connect(
        DockerResourceSnapshot container,
        DockerResourceSnapshot network,
        IReadOnlyList<string> aliases)
    {
        lock (sync)
        {
            var state = GetOrCreateContainer(container);
            var networkName = NormalizeName(network.Name, network.Id);
            state.SuppressedNetworks.Remove(network.Id);
            state.SuppressedNetworks.Remove(networkName);
            state.Attachments[network.Id] = new NetworkAttachment(
                network.Id,
                networkName,
                aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)).Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    public void Disconnect(DockerResourceSnapshot container, string networkReference)
    {
        lock (sync)
        {
            var state = GetOrCreateContainer(container);
            state.SuppressedNetworks.Add(networkReference);

            foreach (var attachmentId in state.Attachments
                         .Where(pair => MatchesNetwork(pair.Value, networkReference))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                var attachment = state.Attachments[attachmentId];
                state.SuppressedNetworks.Add(attachment.NetworkId);
                state.SuppressedNetworks.Add(attachment.NetworkName);
                state.Attachments.Remove(attachmentId);
            }
        }
    }

    public void RemoveContainer(string containerReference)
    {
        lock (sync)
        {
            foreach (var containerId in containers.Values
                         .Where(container => MatchesContainer(container, containerReference))
                         .Select(container => container.ContainerId)
                         .ToArray())
            {
                containers.Remove(containerId);
            }
        }
    }

    public void RemoveNetwork(string networkReference)
    {
        lock (sync)
        {
            foreach (var container in containers.Values)
            {
                foreach (var attachmentId in container.Attachments
                             .Where(pair => MatchesNetwork(pair.Value, networkReference))
                             .Select(pair => pair.Key)
                             .ToArray())
                {
                    var attachment = container.Attachments[attachmentId];
                    container.SuppressedNetworks.Remove(attachment.NetworkId);
                    container.SuppressedNetworks.Remove(attachment.NetworkName);
                    container.Attachments.Remove(attachmentId);
                }

                container.SuppressedNetworks.Remove(networkReference);
            }
        }
    }

    public string ApplyToContainerInspect(string requestedId, string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root)
        {
            return json;
        }

        var projection = GetContainerProjection(
            ReadString(root, "Id", "ID") ?? requestedId,
            ReadString(root, "Name"),
            requestedId);
        if (projection is null)
        {
            return json;
        }

        var networkSettings = GetOrCreateObject(root, "NetworkSettings");
        var networks = GetOrCreateObject(networkSettings, "Networks");
        foreach (var property in networks.ToArray())
        {
            if (projection.SuppressedNetworks.Contains(property.Key))
            {
                networks.Remove(property.Key);
            }
        }

        foreach (var attachment in projection.Attachments)
        {
            var endpoint = networks[attachment.NetworkName] as JsonObject ?? [];
            endpoint["Aliases"] = new JsonArray(
                attachment.Aliases.Select(alias => JsonValue.Create(alias)).ToArray<JsonNode?>());
            endpoint["NetworkID"] = attachment.NetworkId;
            endpoint["EndpointID"] ??= string.Empty;
            endpoint["Gateway"] ??= string.Empty;
            endpoint["IPAddress"] ??= string.Empty;
            endpoint["IPPrefixLen"] ??= 0;
            endpoint["IPv6Gateway"] ??= string.Empty;
            endpoint["GlobalIPv6Address"] ??= string.Empty;
            endpoint["GlobalIPv6PrefixLen"] ??= 0;
            endpoint["MacAddress"] ??= string.Empty;
            networks[attachment.NetworkName] = endpoint;
        }

        return root.ToJsonString();
    }

    public string ApplyToNetworkInspect(string requestedId, string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root)
        {
            return json;
        }

        var networkReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { requestedId };
        AddReference(networkReferences, ReadString(root, "Id", "ID"));
        AddReference(networkReferences, ReadString(root, "Name"));

        var attachments = GetNetworkProjection(networkReferences);
        if (attachments.Count == 0)
        {
            return json;
        }

        var containersNode = GetOrCreateObject(root, "Containers");
        foreach (var attachment in attachments)
        {
            var containerNode = containersNode[attachment.ContainerId] as JsonObject ?? [];
            containerNode["Name"] = attachment.ContainerName;
            containerNode["EndpointID"] ??= string.Empty;
            containerNode["MacAddress"] ??= string.Empty;
            containerNode["IPv4Address"] ??= string.Empty;
            containerNode["IPv6Address"] ??= string.Empty;
            containersNode[attachment.ContainerId] = containerNode;
        }

        return root.ToJsonString();
    }

    private ContainerState GetOrCreateContainer(DockerResourceSnapshot container)
    {
        if (!containers.TryGetValue(container.Id, out var state))
        {
            state = new ContainerState(container.Id, NormalizeName(container.Name, container.Id));
            containers.Add(container.Id, state);
        }
        else
        {
            state.ContainerName = NormalizeName(container.Name, container.Id);
        }

        return state;
    }

    private ContainerProjection? GetContainerProjection(params string?[] references)
    {
        lock (sync)
        {
            var referenceSet = references
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Select(reference => reference!)
                .ToHashSet(StringComparer.Ordinal);
            var state = containers.Values.FirstOrDefault(container =>
                referenceSet.Contains(container.ContainerId) || referenceSet.Contains(container.ContainerName));
            return state is null
                ? null
                : new ContainerProjection(
                    state.SuppressedNetworks.ToHashSet(StringComparer.OrdinalIgnoreCase),
                    state.Attachments.Values.ToArray());
        }
    }

    private IReadOnlyList<NetworkContainerProjection> GetNetworkProjection(IReadOnlySet<string> references)
    {
        lock (sync)
        {
            return containers.Values
                .SelectMany(container => container.Attachments.Values
                    .Where(attachment =>
                        references.Contains(attachment.NetworkId) || references.Contains(attachment.NetworkName))
                    .Select(_ => new NetworkContainerProjection(container.ContainerId, container.ContainerName)))
                .ToArray();
        }
    }

    private static JsonObject GetOrCreateObject(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonObject value)
        {
            return value;
        }

        value = [];
        parent[propertyName] = value;
        return value;
    }

    private static string? ReadString(JsonObject root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root[propertyName] is JsonValue value && value.TryGetValue<string>(out var text))
            {
                return text;
            }
        }

        return null;
    }

    private static void AddReference(ISet<string> references, string? reference)
    {
        if (!string.IsNullOrWhiteSpace(reference))
        {
            references.Add(reference);
        }
    }

    private static bool MatchesContainer(ContainerState container, string reference)
    {
        return string.Equals(container.ContainerId, reference, StringComparison.Ordinal) ||
               string.Equals(container.ContainerName, reference, StringComparison.Ordinal);
    }

    private static bool MatchesNetwork(NetworkAttachment attachment, string reference)
    {
        return string.Equals(attachment.NetworkId, reference, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(attachment.NetworkName, reference, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string? name, string fallback)
    {
        return string.IsNullOrWhiteSpace(name) ? fallback : name.TrimStart('/');
    }

    private sealed class ContainerState(string containerId, string containerName)
    {
        public string ContainerId { get; } = containerId;

        public string ContainerName { get; set; } = containerName;

        public HashSet<string> SuppressedNetworks { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, NetworkAttachment> Attachments { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record NetworkAttachment(
        string NetworkId,
        string NetworkName,
        IReadOnlyList<string> Aliases);

    private sealed record ContainerProjection(
        IReadOnlySet<string> SuppressedNetworks,
        IReadOnlyList<NetworkAttachment> Attachments);

    private sealed record NetworkContainerProjection(string ContainerId, string ContainerName);
}
