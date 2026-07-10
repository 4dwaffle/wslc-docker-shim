using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Http.Endpoints.Containers;
using Testcontainers.WslcShim.Http.Endpoints.Exec;
using Testcontainers.WslcShim.Http.Endpoints.Images;
using Testcontainers.WslcShim.Http.Endpoints.Networks;
using Testcontainers.WslcShim.Http.Endpoints.System;
using Testcontainers.WslcShim.Http.Endpoints.Volumes;
using Testcontainers.WslcShim.Http.Models;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Http;

public static class ShimApplication
{
    public static void ConfigureServices(
        IServiceCollection services,
        IDockerBackend backend,
        ShimRuntimeOptions options,
        IShimListenerClassifier listenerClassifier)
    {
        services.AddRouting();
        services.AddSingleton(backend);
        services.AddSingleton(options);
        services.AddSingleton(listenerClassifier);
        services.AddSingleton<RyukCleanupSessionRegistry>();
    }

    public static void MapRoutes(IEndpointRouteBuilder endpoints)
    {
        MapEndpoints(endpoints);
        MapEndpoints(endpoints.MapGroup("/{dockerApiVersion}"));
    }

    private static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PingEndpoint.Map(endpoints);
        VersionEndpoint.Map(endpoints);
        InfoEndpoint.Map(endpoints);

        CreateContainerEndpoint.Map(endpoints);
        StartContainerEndpoint.Map(endpoints);
        StopContainerEndpoint.Map(endpoints);
        WaitContainerEndpoint.Map(endpoints);
        CreateContainerExecEndpoint.Map(endpoints);
        GetContainerLogsEndpoint.Map(endpoints);
        InspectContainerEndpoint.Map(endpoints);
        ListContainersEndpoint.Map(endpoints);
        DeleteContainerEndpoint.Map(endpoints);

        StartExecEndpoint.Map(endpoints);
        InspectExecEndpoint.Map(endpoints);

        ListNetworksEndpoint.Map(endpoints);
        CreateNetworkEndpoint.Map(endpoints);
        InspectNetworkEndpoint.Map(endpoints);
        DeleteNetworkEndpoint.Map(endpoints);

        ListVolumesEndpoint.Map(endpoints);
        CreateVolumeEndpoint.Map(endpoints);
        InspectVolumeEndpoint.Map(endpoints);
        DeleteVolumeEndpoint.Map(endpoints);

        ListImagesEndpoint.Map(endpoints);
        PullImageEndpoint.Map(endpoints);
        InspectImageEndpoint.Map(endpoints);
        DeleteImageEndpoint.Map(endpoints);
    }
}
