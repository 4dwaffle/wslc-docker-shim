using Microsoft.AspNetCore.Http;
using Testcontainers.WslcShim.Http.Enums;

namespace Testcontainers.WslcShim.Http;

public interface IShimListenerClassifier
{
    ShimListenerKind Classify(HttpContext context);
}
