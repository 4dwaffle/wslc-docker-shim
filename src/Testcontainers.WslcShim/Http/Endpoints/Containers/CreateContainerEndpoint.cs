using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Exceptions;
using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Http.Models;
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
            ValidateCreateRequest(mutation.Request);
        }
        catch (ArgumentException exception)
        {
            return CreateBadRequestResponse(exception.Message);
        }
        catch (UnsupportedDockerCreateOptionException exception)
        {
            return CreateBadRequestResponse(exception.Message);
        }

        var response = await backend.CreateContainerAsync(mutation.Request, mutation.IsRyuk, cancellationToken);
        if (mutation.IsRyuk)
        {
            cleanupSessions.RegisterRyukContainer(mutation.Request);
        }

        return Results.Json(response, statusCode: StatusCodes.Status201Created);
    }

    private static void ValidateCreateRequest(DockerContainerCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Image))
        {
            throw new ArgumentException("Container image is required.", nameof(request));
        }

        WslcCreateRequestCompatibility.Validate(request);
    }

    private static IResult CreateBadRequestResponse(string message)
    {
        return Results.Json(
            new { message },
            statusCode: StatusCodes.Status400BadRequest);
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
