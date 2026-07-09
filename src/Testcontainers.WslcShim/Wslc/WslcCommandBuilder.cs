using Testcontainers.WslcShim.Docker;

namespace Testcontainers.WslcShim.Wslc;

public static class WslcCommandBuilder
{
    public static WslcCommand BuildCreateContainerCommand(DockerContainerCreateRequest request, string cidFile)
    {
        if (string.IsNullOrWhiteSpace(request.Image))
        {
            throw new ArgumentException("Container image is required.", nameof(request));
        }

        var arguments = new List<string> { "create", "--cidfile", cidFile };

        AddOption(arguments, "--name", request.Name);

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

        foreach (var portBinding in BuildPublishArguments(request.HostConfig.PortBindings))
        {
            AddOption(arguments, "--publish", portBinding);
        }

        if (request.HostConfig.PublishAllPorts)
        {
            arguments.Add("--publish-all");
        }

        arguments.Add(request.Image);
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
}
