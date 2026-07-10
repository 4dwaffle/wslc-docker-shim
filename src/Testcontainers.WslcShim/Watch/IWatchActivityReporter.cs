using Testcontainers.WslcShim.Http.Enums;
using Testcontainers.WslcShim.Http.Models;

namespace Testcontainers.WslcShim.Watch;

internal interface IWatchActivityReporter
{
    string NextRequestId();

    void WriteStartup(ShimRuntimeOptions options);

    void WriteStopping();

    void RequestStarted(string requestId, ShimListenerKind listenerKind, string method, string path);

    void RequestCompleted(string requestId, int statusCode, string method, string path, TimeSpan elapsed);

    void RequestCancelled(string requestId, string method, string path, TimeSpan elapsed);

    void RequestFailed(string requestId, string method, string path, Exception exception, TimeSpan elapsed);

    void CommandStarted(string? requestId, string activityName);

    void CommandCompleted(string? requestId, string activityName, int exitCode, TimeSpan elapsed);

    void CommandCancelled(string? requestId, string activityName, TimeSpan elapsed);

    void CommandFailed(string? requestId, string activityName, Exception exception, TimeSpan elapsed);
}
