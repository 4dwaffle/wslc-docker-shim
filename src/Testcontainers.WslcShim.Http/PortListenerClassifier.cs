using Microsoft.AspNetCore.Http;
using Testcontainers.WslcShim.Http.Enums;
using Testcontainers.WslcShim.Http.Models;

namespace Testcontainers.WslcShim.Http;

public sealed class PortListenerClassifier(ShimRuntimeOptions options) : IShimListenerClassifier
{
    public ShimListenerKind Classify(HttpContext context)
    {
        return context.Connection.LocalPort == options.RyukEndpoint.Port
            ? ShimListenerKind.Ryuk
            : ShimListenerKind.FullApi;
    }
}
