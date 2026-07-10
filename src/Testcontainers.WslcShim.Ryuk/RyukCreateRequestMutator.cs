using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Ryuk.Models;

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
        var mounts = request.HostConfig.Mounts
            .Where(mount => !IsDockerSocketMount(mount))
            .ToArray();

        var mutated = request with
        {
            Env = environment,
            // Privileged mode is only requested so Docker-hosted Ryuk can use
            // the daemon socket. The shim removes that socket and gives Ryuk a
            // restricted TCP API instead, so privileged mode is unnecessary.
            HostConfig = request.HostConfig with
            {
                Binds = binds,
                Mounts = mounts,
                Privileged = false
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

    private static bool IsDockerSocketMount(DockerMount mount)
    {
        return string.Equals(mount.Source, "/var/run/docker.sock", StringComparison.Ordinal) ||
               string.Equals(mount.Target, "/var/run/docker.sock", StringComparison.Ordinal);
    }
}
