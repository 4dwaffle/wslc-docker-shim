using Testcontainers.WslcShim.Docker;

namespace Testcontainers.WslcShim.Ryuk;

public sealed record RyukCreateRequestMutationResult(bool IsRyuk, DockerContainerCreateRequest Request);
