using Microsoft.AspNetCore.Http;
using Testcontainers.WslcShim.Http.Enums;

namespace Testcontainers.WslcShim.Http;

public sealed class HeaderListenerClassifier : IShimListenerClassifier
{
    public const string ListenerHeaderName = "X-Wslc-Shim-Listener";

    public ShimListenerKind Classify(HttpContext context)
    {
        return context.Request.Headers.TryGetValue(ListenerHeaderName, out var value) &&
               string.Equals(value.ToString(), "ryuk", StringComparison.OrdinalIgnoreCase)
            ? ShimListenerKind.Ryuk
            : ShimListenerKind.FullApi;
    }
}
