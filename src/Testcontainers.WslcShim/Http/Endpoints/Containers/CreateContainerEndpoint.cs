using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Ryuk;
using Testcontainers.WslcShim.Wslc;

namespace Testcontainers.WslcShim.Http.Endpoints.Containers;

internal static class CreateContainerEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/containers/create", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IWslcDockerBackend backend,
        ShimRuntimeOptions options,
        IShimListenerClassifier listenerClassifier,
        RyukCleanupSessionRegistry cleanupSessions,
        CancellationToken cancellationToken)
    {
        if (EndpointListenerAccess.IsRyuk(context, listenerClassifier))
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

    private static DockerContainerCreateRequest ApplyCreateName(
        DockerContainerCreateRequest request,
        string? queryName)
    {
        return string.IsNullOrWhiteSpace(queryName)
            ? request
            : request with { Name = queryName };
    }
}
