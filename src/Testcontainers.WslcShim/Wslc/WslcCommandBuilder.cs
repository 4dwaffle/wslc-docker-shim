using System.Globalization;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Wslc.Models;

namespace Testcontainers.WslcShim.Wslc;

public static class WslcCommandBuilder
{
    public static WslcCommand BuildCreateContainerCommand(DockerContainerCreateRequest request, string cidFile)
    {
        if (string.IsNullOrWhiteSpace(request.Image))
        {
            throw new ArgumentException("Container image is required.", nameof(request));
        }

        WslcCreateRequestCompatibility.Validate(request);

        var arguments = new List<string> { "create", "--cidfile", cidFile };

        AddOption(arguments, "--name", request.Name);
        AddOption(arguments, "--hostname", request.Hostname);
        AddOption(arguments, "--domainname", request.Domainname);
        AddOption(arguments, "--user", request.User);
        AddOption(arguments, "--workdir", request.WorkingDir);

        if (request.Entrypoint.Count > 0)
        {
            AddOption(arguments, "--entrypoint", request.Entrypoint[0]);
        }

        foreach (var environmentVariable in request.Env)
        {
            AddOption(arguments, "--env", environmentVariable);
        }

        foreach (var label in request.Labels)
        {
            AddOption(arguments, "--label", $"{label.Key}={label.Value}");
        }

        foreach (var bind in request.HostConfig.Binds)
        {
            AddOption(arguments, "--volume", bind);
        }

        foreach (var mount in request.HostConfig.Mounts)
        {
            if (string.Equals(mount.Type, "tmpfs", StringComparison.OrdinalIgnoreCase))
            {
                AddOption(arguments, "--tmpfs", FormatTmpfsMount(mount));
            }
            else
            {
                AddOption(arguments, "--volume", FormatVolumeMount(mount));
            }
        }

        // Docker's top-level Volumes map declares anonymous volumes. WSLc's
        // single-argument --volume form preserves the same destination paths.
        foreach (var volume in request.Volumes.Keys)
        {
            AddOption(arguments, "--volume", volume);
        }

        var network = WslcCreateRequestCompatibility.GetNetworkSelection(request);
        AddOption(arguments, "--network", network.Network);
        foreach (var alias in network.Aliases)
        {
            AddOption(arguments, "--network-alias", alias);
        }

        foreach (var dns in request.HostConfig.Dns)
        {
            AddOption(arguments, "--dns", dns);
        }

        foreach (var dnsOption in request.HostConfig.DnsOptions)
        {
            AddOption(arguments, "--dns-option", dnsOption);
        }

        foreach (var dnsSearch in request.HostConfig.DnsSearch)
        {
            AddOption(arguments, "--dns-search", dnsSearch);
        }

        if (request.HostConfig.Memory > 0)
        {
            AddOption(arguments, "--memory", FormatBytes(request.HostConfig.Memory));
        }

        if (request.HostConfig.NanoCpus > 0)
        {
            AddOption(arguments, "--cpus", FormatNanoCpus(request.HostConfig.NanoCpus));
        }
        else if (request.HostConfig.CpuCount > 0)
        {
            AddOption(arguments, "--cpus", request.HostConfig.CpuCount.ToString(CultureInfo.InvariantCulture));
        }

        if (request.HostConfig.ShmSize > 0)
        {
            AddOption(arguments, "--shm-size", FormatBytes(request.HostConfig.ShmSize));
        }

        foreach (var tmpfs in request.HostConfig.Tmpfs)
        {
            AddOption(
                arguments,
                "--tmpfs",
                string.IsNullOrWhiteSpace(tmpfs.Value) ? tmpfs.Key : $"{tmpfs.Key}:{tmpfs.Value}");
        }

        foreach (var ulimit in request.HostConfig.Ulimits)
        {
            AddOption(
                arguments,
                "--ulimit",
                $"{ulimit.Name}={ulimit.Soft.ToString(CultureInfo.InvariantCulture)}:{ulimit.Hard.ToString(CultureInfo.InvariantCulture)}");
        }

        AddOption(arguments, "--gpus", WslcCreateRequestCompatibility.GetGpuArgument(request.HostConfig.DeviceRequests));

        foreach (var portBinding in BuildPublishArguments(request.HostConfig.PortBindings))
        {
            AddOption(arguments, "--publish", portBinding);
        }

        if (request.HostConfig.PublishAllPorts)
        {
            arguments.Add("--publish-all");
        }

        if (request.HostConfig.AutoRemove)
        {
            arguments.Add("--rm");
        }

        if (request.OpenStdin)
        {
            arguments.Add("--interactive");
        }

        if (request.Tty)
        {
            arguments.Add("--tty");
        }

        AddOption(arguments, "--stop-signal", request.StopSignal);

        arguments.Add(request.Image);
        arguments.AddRange(request.Entrypoint.Skip(1));
        arguments.AddRange(request.Cmd);
        return new WslcCommand("wslc", arguments);
    }

    public static WslcCommand BuildListContainersCommand()
    {
        return BuildListResourcesCommand(DockerResourceKind.Container);
    }

    public static WslcCommand BuildInspectContainerCommand(string id)
    {
        return BuildInspectResourceCommand(DockerResourceKind.Container, id);
    }

    public static WslcCommand BuildDeleteContainerCommand(string id)
    {
        return BuildDeleteResourceCommand(DockerResourceKind.Container, id);
    }

    public static WslcCommand BuildListResourcesCommand(DockerResourceKind kind)
    {
        return kind switch
        {
            DockerResourceKind.Container => new WslcCommand("wslc", ["list", "--all", "--format", "json"]),
            DockerResourceKind.Network => new WslcCommand("wslc", ["network", "list", "--format", "json"]),
            DockerResourceKind.Volume => new WslcCommand("wslc", ["volume", "list", "--format", "json"]),
            DockerResourceKind.Image => new WslcCommand("wslc", ["image", "list", "--format", "json"]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    public static WslcCommand BuildInspectResourceCommand(DockerResourceKind kind, string id)
    {
        return kind switch
        {
            DockerResourceKind.Container => new WslcCommand("wslc", ["inspect", "--type", "container", id]),
            DockerResourceKind.Network => new WslcCommand("wslc", ["network", "inspect", id]),
            DockerResourceKind.Volume => new WslcCommand("wslc", ["volume", "inspect", id]),
            DockerResourceKind.Image => new WslcCommand("wslc", ["image", "inspect", id]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    public static WslcCommand BuildDeleteResourceCommand(DockerResourceKind kind, string id)
    {
        return kind switch
        {
            DockerResourceKind.Container => new WslcCommand("wslc", ["remove", "--force", id]),
            DockerResourceKind.Network => new WslcCommand("wslc", ["network", "remove", id]),
            DockerResourceKind.Volume => new WslcCommand("wslc", ["volume", "remove", id]),
            DockerResourceKind.Image => new WslcCommand("wslc", ["image", "remove", id]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    public static WslcCommand BuildStartContainerCommand(string id)
    {
        return new WslcCommand("wslc", ["start", id]);
    }

    public static WslcCommand BuildStopContainerCommand(string id)
    {
        return new WslcCommand("wslc", ["stop", id]);
    }

    public static WslcCommand BuildLogsCommand(string id, DockerLogRequest request)
    {
        var arguments = new List<string> { "logs" };
        if (request.Timestamps)
        {
            arguments.Add("--timestamps");
        }

        AddOption(arguments, "--tail", request.Tail);
        arguments.Add(id);
        return new WslcCommand("wslc", arguments);
    }

    public static WslcCommand BuildExecCommand(string containerId, DockerExecCreateRequest request)
    {
        if (request.Cmd.Count == 0)
        {
            throw new ArgumentException("Exec command is required.", nameof(request));
        }

        var arguments = new List<string> { "exec" };
        foreach (var environmentVariable in request.Env)
        {
            AddOption(arguments, "--env", environmentVariable);
        }

        AddOption(arguments, "--user", request.User);
        AddOption(arguments, "--workdir", request.WorkingDir);

        if (request.Tty)
        {
            arguments.Add("--tty");
        }

        if (request.AttachStdin)
        {
            arguments.Add("--interactive");
        }

        arguments.Add(containerId);
        arguments.AddRange(request.Cmd);
        return new WslcCommand("wslc", arguments);
    }

    public static WslcCommand BuildPullImageCommand(string image)
    {
        return new WslcCommand("wslc", ["pull", image]);
    }

    public static WslcCommand BuildCreateResourceCommand(
        DockerResourceKind kind,
        DockerResourceCreateRequest request)
    {
        if (kind is not (DockerResourceKind.Network or DockerResourceKind.Volume))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only networks and volumes can be created with this command.");
        }

        var arguments = new List<string>
        {
            kind == DockerResourceKind.Network ? "network" : "volume",
            "create"
        };

        AddOption(arguments, "--driver", request.Driver);
        foreach (var label in request.Labels)
        {
            AddOption(arguments, "--label", $"{label.Key}={label.Value}");
        }

        AddValue(arguments, request.Name);
        return new WslcCommand("wslc", arguments);
    }

    private static IEnumerable<string> BuildPublishArguments(
        IReadOnlyDictionary<string, IReadOnlyList<DockerPortBinding>> portBindings)
    {
        foreach (var portBinding in portBindings)
        {
            foreach (var binding in portBinding.Value)
            {
                if (string.IsNullOrWhiteSpace(binding.HostPort))
                {
                    yield return portBinding.Key;
                    continue;
                }

                yield return string.IsNullOrWhiteSpace(binding.HostIp)
                    ? $"{binding.HostPort}:{portBinding.Key}"
                    : $"{binding.HostIp}:{binding.HostPort}:{portBinding.Key}";
            }
        }
    }

    private static void AddOption(List<string> arguments, string optionName, string? optionValue)
    {
        if (string.IsNullOrWhiteSpace(optionValue))
        {
            return;
        }

        arguments.Add(optionName);
        arguments.Add(optionValue);
    }

    private static void AddValue(List<string> arguments, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            arguments.Add(value);
        }
    }

    private static string FormatBytes(long bytes)
    {
        const long gibibyte = 1024L * 1024L * 1024L;
        const long mebibyte = 1024L * 1024L;
        return bytes switch
        {
            _ when bytes % gibibyte == 0 => $"{bytes / gibibyte}G",
            _ when bytes % mebibyte == 0 => $"{bytes / mebibyte}M",
            _ => bytes.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string FormatNanoCpus(long nanoCpus)
    {
        return ((decimal)nanoCpus / 1_000_000_000m).ToString("0.#########", CultureInfo.InvariantCulture);
    }

    private static string FormatVolumeMount(DockerMount mount)
    {
        var value = string.IsNullOrWhiteSpace(mount.Source)
            ? mount.Target!
            : $"{mount.Source}:{mount.Target}";
        return mount.ReadOnly == true ? value + ":ro" : value;
    }

    private static string FormatTmpfsMount(DockerMount mount)
    {
        var options = new List<string>();
        if (mount.ReadOnly == true)
        {
            options.Add("ro");
        }

        if (mount.TmpfsOptions?.SizeBytes is > 0)
        {
            options.Add("size=" + FormatBytes(mount.TmpfsOptions.SizeBytes.Value));
        }

        if (mount.TmpfsOptions?.Mode is uint mode)
        {
            options.Add("mode=" + Convert.ToString(mode, 8));
        }

        if (mount.TmpfsOptions is not null)
        {
            options.AddRange(mount.TmpfsOptions.Options
                .Where(option => option.Count > 0)
                .Select(option => string.Join("=", option)));
        }

        return options.Count == 0 ? mount.Target! : $"{mount.Target}:{string.Join(",", options)}";
    }
}
