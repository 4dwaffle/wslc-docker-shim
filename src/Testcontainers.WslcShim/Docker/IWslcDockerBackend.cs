using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.Docker;

public interface IWslcDockerBackend
{
    Task<DockerCreateContainerResponse> CreateContainerAsync(
        DockerContainerCreateRequest request,
        bool isRyuk,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DockerResourceSnapshot>> ListResourcesAsync(
        DockerResourceKind kind,
        DockerLabelFilters filters,
        CancellationToken cancellationToken);

    Task<DockerResourceSnapshot?> InspectResourceAsync(
        DockerResourceKind kind,
        string id,
        CancellationToken cancellationToken);

    Task<string?> InspectResourceJsonAsync(
        DockerResourceKind kind,
        string id,
        CancellationToken cancellationToken);

    Task DeleteResourceAsync(DockerResourceKind kind, string id, CancellationToken cancellationToken);

    Task StartContainerAsync(string id, CancellationToken cancellationToken);

    Task StopContainerAsync(string id, CancellationToken cancellationToken);

    Task<string> GetContainerLogsAsync(
        string id,
        DockerLogRequest request,
        CancellationToken cancellationToken);

    Task<DockerWaitContainerResponse?> WaitContainerAsync(string id, CancellationToken cancellationToken);

    Task<DockerExecCreateResponse> CreateContainerExecAsync(
        string containerId,
        DockerExecCreateRequest request,
        CancellationToken cancellationToken);

    Task<DockerExecStartResponse?> StartExecAsync(
        string id,
        DockerExecStartRequest request,
        CancellationToken cancellationToken);

    Task<DockerExecInspectResponse?> InspectExecAsync(string id, CancellationToken cancellationToken);

    Task PullImageAsync(string image, CancellationToken cancellationToken);

    Task<DockerResourceSnapshot> CreateResourceAsync(
        DockerResourceKind kind,
        DockerResourceCreateRequest request,
        CancellationToken cancellationToken);
}
