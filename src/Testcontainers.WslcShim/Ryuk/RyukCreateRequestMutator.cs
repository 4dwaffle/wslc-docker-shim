using Testcontainers.WslcShim.Docker;

namespace Testcontainers.WslcShim.Ryuk;

public static class RyukCreateRequestMutator
{
    public static RyukCreateRequestMutationResult MutateIfRyuk(
        DockerContainerCreateRequest request,
        RyukListenerEndpoint ryukEndpoint)
    {
        if (!IsRyuk(request))
        {
            return new RyukCreateRequestMutationResult(false, request);
        }

        var dockerHost = $"DOCKER_HOST=tcp://{ryukEndpoint.Host}:{ryukEndpoint.Port}";
        var environment = request.Env
            .Where(value => !value.StartsWith("DOCKER_HOST=", StringComparison.Ordinal))
            .Append(dockerHost)
            .ToArray();
        var binds = request.HostConfig.Binds
            .Where(bind => !IsDockerSocketBind(bind))
            .ToArray();

        var mutated = new DockerContainerCreateRequest
        {
            Image = request.Image,
            Name = request.Name,
            Env = environment,
            Labels = request.Labels,
            Cmd = request.Cmd,
            ExposedPorts = request.ExposedPorts,
            HostConfig = new DockerHostConfig
            {
                Binds = binds,
                PortBindings = request.HostConfig.PortBindings,
                PublishAllPorts = request.HostConfig.PublishAllPorts
            }
        };

        return new RyukCreateRequestMutationResult(true, mutated);
    }

    private static bool IsRyuk(DockerContainerCreateRequest request)
    {
        return IsRyukImage(request.Image) || IsRyukName(request.Name);
    }

    private static bool IsRyukImage(string? image)
    {
        return image is not null &&
               image.StartsWith("testcontainers/ryuk", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRyukName(string? name)
    {
        return name is not null &&
               name.Contains("testcontainers-ryuk-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDockerSocketBind(string bind)
    {
        return bind.StartsWith("/var/run/docker.sock:", StringComparison.Ordinal) ||
               string.Equals(bind, "/var/run/docker.sock", StringComparison.Ordinal);
    }
}
