using Microsoft.AspNetCore.Http;

namespace Testcontainers.WslcShim.Http.Endpoints;

internal static class EndpointListenerAccess
{
    public static bool IsRyuk(HttpContext context, IShimListenerClassifier listenerClassifier)
    {
        return listenerClassifier.Classify(context) == ShimListenerKind.Ryuk;
    }
}
