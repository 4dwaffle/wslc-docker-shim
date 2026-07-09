using Microsoft.AspNetCore.Http;

namespace Testcontainers.WslcShim.Http;

public interface IShimListenerClassifier
{
    ShimListenerKind Classify(HttpContext context);
}
