using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.Watch;

internal sealed class WatchingDockerBackend(
    IDockerBackend inner,
    WatchDashboardState dashboard) : IDockerBackend
{
    public async Task<DockerCreateContainerResponse> CreateContainerAsync(
        DockerContainerCreateRequest request,
        CancellationToken cancellationToken)
    {
        var operation = dashboard.BeginContainerCreate(request.Name, request.Image, FormatRequestedPorts(request));
        try
        {
            var response = await inner.CreateContainerAsync(request, cancellationToken);
            dashboard.CompleteContainerCreate(operation, response.Id);
            return response;
        }
        catch
        {
            dashboard.FailContainerCreate(operation);
            throw;
        }
    }

    public Task<IReadOnlyList<DockerResourceSnapshot>> ListResourcesAsync(
        DockerResourceKind kind,
        DockerLabelFilters filters,
        CancellationToken cancellationToken) =>
        inner.ListResourcesAsync(kind, filters, cancellationToken);

    public Task<DockerResourceSnapshot?> InspectResourceAsync(
        DockerResourceKind kind,
        string id,
        CancellationToken cancellationToken) =>
        inner.InspectResourceAsync(kind, id, cancellationToken);

    public Task<string?> InspectResourceJsonAsync(
        DockerResourceKind kind,
        string id,
        CancellationToken cancellationToken) =>
        inner.InspectResourceJsonAsync(kind, id, cancellationToken);

    public async Task DeleteResourceAsync(DockerResourceKind kind, string id, CancellationToken cancellationToken)
    {
        if (kind != DockerResourceKind.Container)
        {
            await inner.DeleteResourceAsync(kind, id, cancellationToken);
            return;
        }

        var operation = dashboard.BeginContainerOperation(id, "removing");
        try
        {
            await inner.DeleteResourceAsync(kind, id, cancellationToken);
            dashboard.CompleteContainerOperation(operation, "removed");
        }
        catch
        {
            dashboard.FailContainerOperation(operation);
            throw;
        }
    }

    public async Task StartContainerAsync(string id, CancellationToken cancellationToken)
    {
        var operation = dashboard.BeginContainerOperation(id, "starting");
        try
        {
            await inner.StartContainerAsync(id, cancellationToken);
            dashboard.CompleteContainerOperation(operation, "running");
        }
        catch
        {
            dashboard.FailContainerOperation(operation);
            throw;
        }
    }

    public async Task StopContainerAsync(string id, CancellationToken cancellationToken)
    {
        var operation = dashboard.BeginContainerOperation(id, "stopping");
        try
        {
            await inner.StopContainerAsync(id, cancellationToken);
            dashboard.CompleteContainerOperation(operation, "exited");
        }
        catch
        {
            dashboard.FailContainerOperation(operation);
            throw;
        }
    }

    public Task<string> GetContainerLogsAsync(
        string id,
        DockerLogRequest request,
        CancellationToken cancellationToken) =>
        inner.GetContainerLogsAsync(id, request, cancellationToken);

    public async Task<DockerWaitContainerResponse?> WaitContainerAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var response = await inner.WaitContainerAsync(id, cancellationToken);
        if (response is not null)
        {
            dashboard.SetContainerStatus(id, $"exited ({response.StatusCode})");
        }

        return response;
    }

    public Task<DockerExecCreateResponse> CreateContainerExecAsync(
        string containerId,
        DockerExecCreateRequest request,
        CancellationToken cancellationToken) =>
        inner.CreateContainerExecAsync(containerId, request, cancellationToken);

    public Task<DockerExecStartResponse?> StartExecAsync(
        string id,
        DockerExecStartRequest request,
        CancellationToken cancellationToken) =>
        inner.StartExecAsync(id, request, cancellationToken);

    public Task<DockerExecInspectResponse?> InspectExecAsync(string id, CancellationToken cancellationToken) =>
        inner.InspectExecAsync(id, cancellationToken);

    public Task PullImageAsync(string image, CancellationToken cancellationToken) =>
        inner.PullImageAsync(image, cancellationToken);

    public Task<DockerResourceSnapshot> CreateResourceAsync(
        DockerResourceKind kind,
        DockerResourceCreateRequest request,
        CancellationToken cancellationToken) =>
        inner.CreateResourceAsync(kind, request, cancellationToken);

    private static string FormatRequestedPorts(DockerContainerCreateRequest request)
    {
        var ports = request.HostConfig.PortBindings.SelectMany(binding =>
            binding.Value.Count == 0
                ? [binding.Key]
                : binding.Value.Select(value =>
                    string.IsNullOrWhiteSpace(value.HostPort)
                        ? binding.Key
                        : $"{value.HostIp ?? "0.0.0.0"}:{value.HostPort}->{binding.Key}"));
        var formatted = string.Join(", ", ports);
        return request.HostConfig.PublishAllPorts
            ? string.IsNullOrEmpty(formatted) ? "all" : formatted + ", all"
            : formatted;
    }
}
