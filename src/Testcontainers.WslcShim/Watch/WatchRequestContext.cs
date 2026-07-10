namespace Testcontainers.WslcShim.Watch;

internal sealed class WatchRequestContext
{
    private readonly AsyncLocal<string?> currentRequestId = new();

    public string? CurrentRequestId => currentRequestId.Value;

    public IDisposable Push(string requestId)
    {
        var previousRequestId = currentRequestId.Value;
        currentRequestId.Value = requestId;
        return new Scope(this, previousRequestId);
    }

    private sealed class Scope(WatchRequestContext context, string? previousRequestId) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            context.currentRequestId.Value = previousRequestId;
            disposed = true;
        }
    }
}
