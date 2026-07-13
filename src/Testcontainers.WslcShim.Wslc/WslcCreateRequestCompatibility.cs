using System.Globalization;
using System.Text.Json;
using Testcontainers.WslcShim.Docker.Exceptions;
using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Wslc.Models;

namespace Testcontainers.WslcShim.Wslc;

public static class WslcCreateRequestCompatibility
{
    public static void Validate(DockerContainerCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var unsupported = new List<string>();
        AddUnsupportedExtensionData(unsupported, string.Empty, request.AdditionalProperties);
        AddUnsupportedExtensionData(unsupported, "HostConfig.", request.HostConfig.AdditionalProperties);
        AddUnsupportedExtensionData(unsupported, "NetworkingConfig.", request.NetworkingConfig.AdditionalProperties);

        foreach (var endpoint in request.NetworkingConfig.EndpointsConfig)
        {
            AddUnsupportedExtensionData(
                unsupported,
                $"NetworkingConfig.EndpointsConfig[{endpoint.Key}].",
                endpoint.Value.AdditionalProperties);
        }

        for (var index = 0; index < request.HostConfig.Mounts.Count; index++)
        {
            var mount = request.HostConfig.Mounts[index];
            var prefix = $"HostConfig.Mounts[{index}].";
            AddUnsupportedExtensionData(unsupported, prefix, mount.AdditionalProperties);
            if (mount.TmpfsOptions is not null)
            {
                AddUnsupportedExtensionData(unsupported, prefix + "TmpfsOptions.", mount.TmpfsOptions.AdditionalProperties);
            }
        }

        if (request.HostConfig.Privileged)
        {
            unsupported.Add("HostConfig.Privileged");
        }

        if (request.StdinOnce)
        {
            unsupported.Add(nameof(request.StdinOnce));
        }

        if (request.Entrypoint.Count > 0 && string.IsNullOrWhiteSpace(request.Entrypoint[0]))
        {
            unsupported.Add($"{nameof(request.Entrypoint)} (empty override)");
        }

        ValidateExposedPorts(request, unsupported);
        ValidateNetworkConfiguration(request, unsupported);
        ValidateGpuConfiguration(request.HostConfig.DeviceRequests, unsupported);
        ValidateMounts(request.HostConfig.Mounts, unsupported);

        if (unsupported.Count > 0)
        {
            throw new UnsupportedDockerCreateOptionException(unsupported.Distinct(StringComparer.Ordinal).ToArray());
        }

        ValidateNonNegative(request.HostConfig.Memory, "HostConfig.Memory");
        ValidateNonNegative(request.HostConfig.NanoCpus, "HostConfig.NanoCpus");
        ValidateNonNegative(request.HostConfig.CpuCount, "HostConfig.CpuCount");
        ValidateNonNegative(request.HostConfig.ShmSize, "HostConfig.ShmSize");

        if (request.HostConfig.NanoCpus > 0 && request.HostConfig.CpuCount > 0)
        {
            throw new ArgumentException("HostConfig.NanoCpus and HostConfig.CpuCount cannot both be set.", nameof(request));
        }

        foreach (var tmpfs in request.HostConfig.Tmpfs)
        {
            if (string.IsNullOrWhiteSpace(tmpfs.Key))
            {
                throw new ArgumentException("A tmpfs mount path cannot be empty.", nameof(request));
            }
        }

        foreach (var ulimit in request.HostConfig.Ulimits)
        {
            if (string.IsNullOrWhiteSpace(ulimit.Name))
            {
                throw new ArgumentException("A ulimit name cannot be empty.", nameof(request));
            }
        }
    }

    private static void ValidateMounts(
        IReadOnlyList<DockerMount> mounts,
        List<string> unsupported)
    {
        for (var index = 0; index < mounts.Count; index++)
        {
            var mount = mounts[index];
            var path = $"HostConfig.Mounts[{index}]";
            if (string.IsNullOrWhiteSpace(mount.Target))
            {
                throw new ArgumentException($"{path}.Target cannot be empty.", nameof(mounts));
            }

            if (string.Equals(mount.Type, "bind", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(mount.Source))
                {
                    throw new ArgumentException($"{path}.Source cannot be empty for a bind mount.", nameof(mounts));
                }

                if (mount.TmpfsOptions is not null)
                {
                    unsupported.Add(path + ".TmpfsOptions");
                }

                continue;
            }

            if (string.Equals(mount.Type, "volume", StringComparison.OrdinalIgnoreCase))
            {
                if (mount.TmpfsOptions is not null)
                {
                    unsupported.Add(path + ".TmpfsOptions");
                }

                continue;
            }

            if (string.Equals(mount.Type, "tmpfs", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(mount.Source))
                {
                    unsupported.Add(path + ".Source");
                }

                if (mount.TmpfsOptions?.SizeBytes < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        path + ".TmpfsOptions.SizeBytes",
                        mount.TmpfsOptions.SizeBytes,
                        "A tmpfs size cannot be negative.");
                }

                continue;
            }

            unsupported.Add(path + ".Type");
        }
    }

    internal static WslcNetworkSelection GetNetworkSelection(DockerContainerCreateRequest request)
    {
        var endpoint = request.NetworkingConfig.EndpointsConfig.SingleOrDefault();
        var networkMode = NormalizeNetworkMode(request.HostConfig.NetworkMode);
        var endpointNetwork = NormalizeNetworkMode(endpoint.Key);
        var network = IsDefaultNetworkMode(networkMode)
            ? IsDefaultNetworkMode(endpointNetwork) ? null : endpointNetwork
            : networkMode;
        var aliases = endpoint.Value?.Aliases
            .Concat(endpoint.Value.DnsNames)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        return new WslcNetworkSelection(network, aliases);
    }

    internal static string? GetGpuArgument(IReadOnlyList<DockerDeviceRequest?>? requests)
    {
        var request = requests?.FirstOrDefault(candidate => candidate is not null);
        if (request is null)
        {
            return null;
        }

        if (request.DeviceIds.Count > 0)
        {
            return $"device={string.Join(",", request.DeviceIds)}";
        }

        return request.Count switch
        {
            < 0 => "all",
            > 0 => request.Count.ToString(CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static void ValidateNetworkConfiguration(
        DockerContainerCreateRequest request,
        List<string> unsupported)
    {
        var endpoints = request.NetworkingConfig.EndpointsConfig;
        if (endpoints.Count > 1)
        {
            unsupported.Add("NetworkingConfig.EndpointsConfig (multiple networks)");
            return;
        }

        var endpoint = endpoints.SingleOrDefault();
        if (endpoints.Count == 1 && string.IsNullOrWhiteSpace(endpoint.Key))
        {
            throw new ArgumentException("A Docker network name cannot be empty.", nameof(request));
        }

        var networkMode = NormalizeNetworkMode(request.HostConfig.NetworkMode);
        if (request.NetworkDisabled == true)
        {
            unsupported.Add(nameof(request.NetworkDisabled));
        }

        if (networkMode is not null &&
            (networkMode.Equals("host", StringComparison.OrdinalIgnoreCase) ||
             networkMode.Equals("none", StringComparison.OrdinalIgnoreCase) ||
             networkMode.StartsWith("container:", StringComparison.OrdinalIgnoreCase)))
        {
            unsupported.Add("HostConfig.NetworkMode (Docker namespace mode)");
        }

        if (endpoints.Count == 1 &&
            !IsDefaultNetworkMode(networkMode) &&
            !string.Equals(networkMode, endpoint.Key, StringComparison.Ordinal))
        {
            unsupported.Add("HostConfig.NetworkMode conflicting with NetworkingConfig.EndpointsConfig");
        }
    }

    private static void ValidateExposedPorts(
        DockerContainerCreateRequest request,
        List<string> unsupported)
    {
        if (request.HostConfig.PublishAllPorts)
        {
            return;
        }

        foreach (var exposedPort in request.ExposedPorts.Keys)
        {
            if (!request.HostConfig.PortBindings.TryGetValue(exposedPort, out var bindings) || bindings.Count == 0)
            {
                unsupported.Add($"ExposedPorts[{exposedPort}] (no PortBinding or PublishAllPorts)");
            }
        }
    }

    private static void ValidateGpuConfiguration(
        IReadOnlyList<DockerDeviceRequest?>? requests,
        List<string> unsupported)
    {
        var meaningfulRequests = requests?.Where(request => request is not null).ToArray() ?? [];
        if (meaningfulRequests.Length > 1)
        {
            unsupported.Add("HostConfig.DeviceRequests (multiple GPU requests)");
            return;
        }

        if (meaningfulRequests.Length == 0)
        {
            return;
        }

        var request = meaningfulRequests[0]!;
        if (!string.IsNullOrWhiteSpace(request.Driver) &&
            !string.Equals(request.Driver, "nvidia", StringComparison.OrdinalIgnoreCase))
        {
            unsupported.Add("HostConfig.DeviceRequests.Driver");
        }

        if (request.Options.Count > 0)
        {
            unsupported.Add("HostConfig.DeviceRequests.Options");
        }

        if (request.Capabilities.Count > 0 &&
            !request.Capabilities.Any(set => set.Any(value => string.Equals(value, "gpu", StringComparison.OrdinalIgnoreCase))))
        {
            unsupported.Add("HostConfig.DeviceRequests.Capabilities");
        }

        if (request.Count != 0 && request.DeviceIds.Count > 0)
        {
            throw new ArgumentException("A GPU request cannot specify both Count and DeviceIDs.", nameof(requests));
        }

        if (request.DeviceIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A GPU device ID cannot be empty.", nameof(requests));
        }
    }

    private static void AddUnsupportedExtensionData(
        List<string> unsupported,
        string prefix,
        IDictionary<string, JsonElement>? extensionData)
    {
        if (extensionData is null)
        {
            return;
        }

        foreach (var property in extensionData)
        {
            if (!IsAcceptedDockerDefault(prefix, property.Key, property.Value) &&
                HasMeaningfulValue(property.Value))
            {
                unsupported.Add(prefix + property.Key);
            }
        }
    }

    private static bool IsAcceptedDockerDefault(string prefix, string propertyName, JsonElement value)
    {
        if (!string.Equals(prefix, "HostConfig.", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(propertyName, "MemorySwappiness", StringComparison.Ordinal))
        {
            return value.ValueKind == JsonValueKind.Number &&
                   value.TryGetInt64(out var memorySwappiness) &&
                   memorySwappiness == -1;
        }

        if (!string.Equals(propertyName, "RestartPolicy", StringComparison.Ordinal) ||
            value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return value.EnumerateObject().All(property => property.Name switch
        {
            "Name" => property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
                      property.Value.ValueKind == JsonValueKind.String &&
                      (string.IsNullOrWhiteSpace(property.Value.GetString()) ||
                       string.Equals(property.Value.GetString(), "no", StringComparison.OrdinalIgnoreCase)),
            "MaximumRetryCount" => property.Value.ValueKind == JsonValueKind.Number &&
                                   property.Value.TryGetInt64(out var retryCount) &&
                                   retryCount == 0,
            _ => !HasMeaningfulValue(property.Value)
        });
    }

    private static bool HasMeaningfulValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.False => false,
            JsonValueKind.True => true,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Number => !value.TryGetDecimal(out var number) || number != 0,
            JsonValueKind.Array => value.EnumerateArray().Any(HasMeaningfulValue),
            JsonValueKind.Object => value.EnumerateObject().Any(property => HasMeaningfulValue(property.Value)),
            _ => true
        };
    }

    private static string? NormalizeNetworkMode(string? networkMode)
    {
        return string.IsNullOrWhiteSpace(networkMode) ? null : networkMode.Trim();
    }

    private static bool IsDefaultNetworkMode(string? networkMode)
    {
        return networkMode is null ||
               networkMode.Equals("default", StringComparison.OrdinalIgnoreCase) ||
               networkMode.Equals("bridge", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateNonNegative(long value, string path)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(path, value, "Docker create resource values cannot be negative.");
        }
    }
}
