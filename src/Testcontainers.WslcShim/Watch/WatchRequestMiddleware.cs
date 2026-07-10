using System.Diagnostics;
using Testcontainers.WslcShim.Http;

namespace Testcontainers.WslcShim.Watch;

internal sealed class WatchRequestMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IWatchActivityReporter reporter,
        WatchRequestContext requestContext,
        IShimListenerClassifier listenerClassifier)
    {
        var requestId = reporter.NextRequestId();
        var method = context.Request.Method;
        var path = (context.Request.PathBase + context.Request.Path).Value ?? "/";
        var startedAt = Stopwatch.GetTimestamp();

        reporter.RequestStarted(requestId, listenerClassifier.Classify(context), method, path);
        using var requestScope = requestContext.Push(requestId);

        try
        {
            await next(context);
            reporter.RequestCompleted(
                requestId,
                context.Response.StatusCode,
                method,
                path,
                Stopwatch.GetElapsedTime(startedAt));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            reporter.RequestCancelled(requestId, method, path, Stopwatch.GetElapsedTime(startedAt));
            throw;
        }
        catch (Exception exception)
        {
            reporter.RequestFailed(requestId, method, path, exception, Stopwatch.GetElapsedTime(startedAt));
            throw;
        }
    }
}
