using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Ryuk;
using Testcontainers.WslcShim.Wslc;

namespace Testcontainers.WslcShim.Http;

public static class ShimApplication
{
    public static void ConfigureServices(
        IServiceCollection services,
        IWslcDockerBackend backend,
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
        MapRoutes(endpoints, string.Empty);
        MapRoutes(endpoints, "/{dockerApiVersion}");
    }

    private static void MapRoutes(IEndpointRouteBuilder endpoints, string prefix)
    {
        endpoints.MapGet($"{prefix}/_ping", () => Results.Text("OK"));
        endpoints.MapGet($"{prefix}/version", () => Results.Json(new
        {
            Version = "wslc-docker-shim",
            ApiVersion = "1.43",
            MinAPIVersion = "1.24",
            Os = "linux",
            Arch = "amd64"
        }));
        endpoints.MapGet($"{prefix}/info", GetInfo);

        endpoints.MapPost($"{prefix}/containers/create", CreateContainerAsync);
        endpoints.MapPost($"{prefix}/containers/{{id}}/start", StartContainerAsync);
        endpoints.MapPost($"{prefix}/containers/{{id}}/stop", StopContainerAsync);
        endpoints.MapPost($"{prefix}/containers/{{id}}/wait", WaitContainerAsync);
        endpoints.MapPost($"{prefix}/containers/{{id}}/exec", CreateContainerExecAsync);
        endpoints.MapGet($"{prefix}/containers/{{id}}/logs", GetContainerLogsAsync);
        endpoints.MapGet($"{prefix}/containers/{{id}}/json", (
            string id,
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            CancellationToken cancellationToken) =>
            InspectResourceAsync(DockerResourceKind.Container, id, context, backend, listenerClassifier, cancellationToken));
        endpoints.MapGet($"{prefix}/containers/json", (
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            RyukCleanupSessionRegistry cleanupSessions,
            CancellationToken cancellationToken) =>
            ListResourcesAsync(DockerResourceKind.Container, context, backend, listenerClassifier, cleanupSessions, cancellationToken));
        endpoints.MapDelete($"{prefix}/containers/{{id}}", (
            string id,
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            RyukCleanupSessionRegistry cleanupSessions,
            CancellationToken cancellationToken) =>
            DeleteResourceAsync(DockerResourceKind.Container, id, context, backend, listenerClassifier, cleanupSessions, cancellationToken));
        endpoints.MapPost($"{prefix}/exec/{{id}}/start", StartExecAsync);
        endpoints.MapGet($"{prefix}/exec/{{id}}/json", InspectExecAsync);
        endpoints.MapGet($"{prefix}/networks", (
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            RyukCleanupSessionRegistry cleanupSessions,
            CancellationToken cancellationToken) =>
            ListResourcesAsync(DockerResourceKind.Network, context, backend, listenerClassifier, cleanupSessions, cancellationToken));
        endpoints.MapPost($"{prefix}/networks/create", (
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            CancellationToken cancellationToken) =>
            CreateResourceAsync(DockerResourceKind.Network, context, backend, listenerClassifier, cancellationToken));
        endpoints.MapGet($"{prefix}/networks/{{id}}", (
            string id,
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            CancellationToken cancellationToken) =>
            InspectResourceAsync(DockerResourceKind.Network, id, context, backend, listenerClassifier, cancellationToken));
        endpoints.MapDelete($"{prefix}/networks/{{id}}", (
            string id,
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            RyukCleanupSessionRegistry cleanupSessions,
            CancellationToken cancellationToken) =>
            DeleteResourceAsync(DockerResourceKind.Network, id, context, backend, listenerClassifier, cleanupSessions, cancellationToken));
        endpoints.MapGet($"{prefix}/volumes", (
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            RyukCleanupSessionRegistry cleanupSessions,
            CancellationToken cancellationToken) =>
            ListResourcesAsync(DockerResourceKind.Volume, context, backend, listenerClassifier, cleanupSessions, cancellationToken));
        endpoints.MapPost($"{prefix}/volumes/create", (
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            CancellationToken cancellationToken) =>
            CreateResourceAsync(DockerResourceKind.Volume, context, backend, listenerClassifier, cancellationToken));
        endpoints.MapGet($"{prefix}/volumes/{{id}}", (
            string id,
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            CancellationToken cancellationToken) =>
            InspectResourceAsync(DockerResourceKind.Volume, id, context, backend, listenerClassifier, cancellationToken));
        endpoints.MapDelete($"{prefix}/volumes/{{id}}", (
            string id,
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            RyukCleanupSessionRegistry cleanupSessions,
            CancellationToken cancellationToken) =>
            DeleteResourceAsync(DockerResourceKind.Volume, id, context, backend, listenerClassifier, cleanupSessions, cancellationToken));
        endpoints.MapGet($"{prefix}/images/json", (
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            RyukCleanupSessionRegistry cleanupSessions,
            CancellationToken cancellationToken) =>
            ListResourcesAsync(DockerResourceKind.Image, context, backend, listenerClassifier, cleanupSessions, cancellationToken));
        endpoints.MapPost($"{prefix}/images/create", PullImageAsync);
        endpoints.MapGet($"{prefix}/images/{{**id}}", (
            string id,
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            CancellationToken cancellationToken) =>
            InspectImageAsync(id, context, backend, listenerClassifier, cancellationToken));
        endpoints.MapDelete($"{prefix}/images/{{**id}}", (
            string id,
            HttpContext context,
            IWslcDockerBackend backend,
            IShimListenerClassifier listenerClassifier,
            RyukCleanupSessionRegistry cleanupSessions,
            CancellationToken cancellationToken) =>
            DeleteResourceAsync(DockerResourceKind.Image, id, context, backend, listenerClassifier, cleanupSessions, cancellationToken));
    }

    private static IResult GetInfo(
        HttpContext context,
        IShimListenerClassifier listenerClassifier)
    {
        if (IsRyukListener(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        return Results.Json(new
        {
            ID = "wslc-docker-shim",
            OSType = "linux",
            Architecture = "x86_64",
            OperatingSystem = "WSLc",
            ServerVersion = "wslc-docker-shim"
        });
    }

    private static async Task<IResult> CreateContainerAsync(
        HttpContext context,
        IWslcDockerBackend backend,
        ShimRuntimeOptions options,
        IShimListenerClassifier listenerClassifier,
        RyukCleanupSessionRegistry cleanupSessions,
        CancellationToken cancellationToken)
    {
        if (listenerClassifier.Classify(context) == ShimListenerKind.Ryuk)
        {
            return Results.NotFound();
        }

        var request = await context.Request.ReadFromJsonAsync<DockerContainerCreateRequest>(
            cancellationToken: cancellationToken);
        if (request is null)
        {
            return Results.BadRequest();
        }

        var requestWithName = ApplyCreateName(request, context.Request.Query["name"]);
        var mutation = RyukCreateRequestMutator.MutateIfRyuk(requestWithName, options.RyukEndpoint);
        try
        {
            WslcCreateRequestCompatibility.Validate(mutation.Request);
        }
        catch (UnsupportedDockerCreateOptionException exception)
        {
            return Results.Json(
                new { message = exception.Message },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await backend.CreateContainerAsync(mutation.Request, mutation.IsRyuk, cancellationToken);
        if (mutation.IsRyuk)
        {
            cleanupSessions.RegisterRyukContainer(mutation.Request);
        }

        return Results.Json(response, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> StartContainerAsync(
        string id,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (IsRyukListener(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        await backend.StartContainerAsync(id, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> StopContainerAsync(
        string id,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (IsRyukListener(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        await backend.StopContainerAsync(id, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetContainerLogsAsync(
        string id,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (IsRyukListener(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var request = new DockerLogRequest(
            Follow: string.Equals(context.Request.Query["follow"], "1", StringComparison.Ordinal) ||
                    string.Equals(context.Request.Query["follow"], "true", StringComparison.OrdinalIgnoreCase),
            Timestamps: string.Equals(context.Request.Query["timestamps"], "1", StringComparison.Ordinal) ||
                        string.Equals(context.Request.Query["timestamps"], "true", StringComparison.OrdinalIgnoreCase),
            Tail: context.Request.Query["tail"]);
        var logs = await backend.GetContainerLogsAsync(id, request, cancellationToken);
        var bytes = DockerRawStream.FromStdout(logs);
        context.Response.ContentType = "application/vnd.docker.raw-stream";
        await context.Response.Body.WriteAsync(bytes, cancellationToken);
        return Results.Empty;
    }

    private static async Task<IResult> WaitContainerAsync(
        string id,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (IsRyukListener(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var response = await backend.WaitContainerAsync(id, cancellationToken);
        return response is null
            ? Results.NotFound()
            : Results.Json(response);
    }

    private static async Task<IResult> CreateContainerExecAsync(
        string id,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (IsRyukListener(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var request = await context.Request.ReadFromJsonAsync<DockerExecCreateRequest>(
            cancellationToken: cancellationToken);
        if (request is null)
        {
            return Results.BadRequest();
        }

        var response = await backend.CreateContainerExecAsync(id, request, cancellationToken);
        return Results.Json(response, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> StartExecAsync(
        string id,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (IsRyukListener(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var request = await context.Request.ReadFromJsonAsync<DockerExecStartRequest>(
            cancellationToken: cancellationToken) ?? new DockerExecStartRequest();
        var response = await backend.StartExecAsync(id, request, cancellationToken);
        if (response is null)
        {
            return Results.NotFound();
        }

        var bytes = DockerRawStream.FromStdout(response.Output);
        var upgradeFeature = context.Features.Get<IHttpUpgradeFeature>();
        if (upgradeFeature?.IsUpgradableRequest == true)
        {
            await using var stream = await upgradeFeature.UpgradeAsync();
            await stream.WriteAsync(bytes, cancellationToken);
            return Results.Empty;
        }

        context.Response.StatusCode = StatusCodes.Status101SwitchingProtocols;
        context.Response.Headers.Connection = "Upgrade";
        context.Response.Headers.Upgrade = "tcp";
        context.Response.ContentType = "application/vnd.docker.raw-stream";
        await context.Response.Body.WriteAsync(bytes, cancellationToken);
        return Results.Empty;
    }

    private static async Task<IResult> InspectExecAsync(
        string id,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (IsRyukListener(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var response = await backend.InspectExecAsync(id, cancellationToken);
        return response is null
            ? Results.NotFound()
            : Results.Json(response);
    }

    private static async Task<IResult> InspectResourceAsync(
        DockerResourceKind kind,
        string id,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (IsRyukListener(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var json = await backend.InspectResourceJsonAsync(kind, id, cancellationToken);
        return json is null
            ? Results.NotFound()
            : Results.Text(json, "application/json");
    }

    private static Task<IResult> InspectImageAsync(
        string id,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        const string inspectSuffix = "/json";
        return id.EndsWith(inspectSuffix, StringComparison.Ordinal)
            ? InspectResourceAsync(
                DockerResourceKind.Image,
                id[..^inspectSuffix.Length],
                context,
                backend,
                listenerClassifier,
                cancellationToken)
            : Task.FromResult<IResult>(Results.NotFound());
    }

    private static async Task<IResult> PullImageAsync(
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (IsRyukListener(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var fromImage = context.Request.Query["fromImage"].ToString();
        if (string.IsNullOrWhiteSpace(fromImage))
        {
            return Results.BadRequest();
        }

        var tag = context.Request.Query["tag"].ToString();
        var image = string.IsNullOrWhiteSpace(tag) ? fromImage : $"{fromImage}:{tag}";
        await backend.PullImageAsync(image, cancellationToken);
        return Results.Json(new { status = $"Pulled {image}" });
    }

    private static async Task<IResult> CreateResourceAsync(
        DockerResourceKind kind,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        CancellationToken cancellationToken)
    {
        if (IsRyukListener(context, listenerClassifier))
        {
            return Results.NotFound();
        }

        var request = await context.Request.ReadFromJsonAsync<DockerResourceCreateRequest>(
            cancellationToken: cancellationToken) ?? new DockerResourceCreateRequest();
        var resource = await backend.CreateResourceAsync(kind, request, cancellationToken);
        return Results.Json(
            kind == DockerResourceKind.Volume
                ? new { Name = resource.Name ?? resource.Id }
                : new { Id = resource.Id, Warning = string.Empty },
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListResourcesAsync(
        DockerResourceKind kind,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        RyukCleanupSessionRegistry cleanupSessions,
        CancellationToken cancellationToken)
    {
        var filters = DockerLabelFilters.FromDockerFiltersQuery(context.Request.Query["filters"]);
        if (listenerClassifier.Classify(context) == ShimListenerKind.Ryuk &&
            (!RestrictedRyukCleanupPolicy.CanList(filters) || !cleanupSessions.TryActivate(context, filters)))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var resources = await backend.ListResourcesAsync(kind, filters, cancellationToken);
        return Results.Json(ToDockerListResponse(kind, resources));
    }

    private static async Task<IResult> DeleteResourceAsync(
        DockerResourceKind kind,
        string id,
        HttpContext context,
        IWslcDockerBackend backend,
        IShimListenerClassifier listenerClassifier,
        RyukCleanupSessionRegistry cleanupSessions,
        CancellationToken cancellationToken)
    {
        if (listenerClassifier.Classify(context) == ShimListenerKind.Ryuk)
        {
            var resource = await backend.InspectResourceAsync(kind, id, cancellationToken);
            if (resource is null)
            {
                return Results.NotFound();
            }

            if (!cleanupSessions.TryGetActiveSession(context, out var activeSession) ||
                !RestrictedRyukCleanupPolicy.CanDelete(resource, activeSession))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
        }

        await backend.DeleteResourceAsync(kind, id, cancellationToken);
        return kind == DockerResourceKind.Image
            ? Results.Json(Array.Empty<object>())
            : Results.NoContent();
    }

    private static DockerContainerCreateRequest ApplyCreateName(
        DockerContainerCreateRequest request,
        string? queryName)
    {
        if (string.IsNullOrWhiteSpace(queryName))
        {
            return request;
        }

        return request with { Name = queryName };
    }

    private static object ToDockerListResponse(
        DockerResourceKind kind,
        IReadOnlyList<DockerResourceSnapshot> resources)
    {
        return kind switch
        {
            DockerResourceKind.Container => resources.Select(resource => new
            {
                resource.Id,
                Names = new[] { "/" + (resource.Name ?? resource.Id) },
                Created = GetCreationTime(resource).ToUnixTimeSeconds(),
                resource.Labels
            }),
            DockerResourceKind.Network => resources.Select(resource => new
            {
                resource.Id,
                Name = resource.Name ?? resource.Id,
                Created = ToDockerTimestamp(GetCreationTime(resource)),
                resource.Labels
            }),
            DockerResourceKind.Volume => new
            {
                Volumes = resources.Select(resource => new
                {
                    Name = resource.Name ?? resource.Id,
                    CreatedAt = ToDockerTimestamp(GetCreationTime(resource)),
                    resource.Labels
                }),
                Warnings = Array.Empty<string>()
            },
            DockerResourceKind.Image => resources.Select(resource => new
            {
                resource.Id,
                RepoTags = resource.Name is null ? Array.Empty<string>() : new[] { resource.Name },
                Created = GetCreationTime(resource).ToUnixTimeSeconds(),
                resource.Labels
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static DateTimeOffset GetCreationTime(DockerResourceSnapshot resource)
    {
        return resource.CreatedAt ?? DateTimeOffset.UnixEpoch;
    }

    private static string ToDockerTimestamp(DateTimeOffset createdAt)
    {
        return createdAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    }

    private static bool IsRyukListener(HttpContext context, IShimListenerClassifier listenerClassifier)
    {
        return listenerClassifier.Classify(context) == ShimListenerKind.Ryuk;
    }
}
