using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.Ryuk.Models;

public sealed record RyukCreateRequestMutationResult(bool IsRyuk, DockerContainerCreateRequest Request);
