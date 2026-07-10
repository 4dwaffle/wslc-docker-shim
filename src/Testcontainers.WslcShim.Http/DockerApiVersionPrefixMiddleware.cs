using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Testcontainers.WslcShim.Http;

public static class DockerApiVersionPrefixMiddleware
{
    private static readonly Regex VersionPrefix = new(
        "^/v\\d+(?:\\.\\d+)?(?=/|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IApplicationBuilder UseDockerApiVersionPrefix(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var originalPath = context.Request.Path;
            var path = originalPath.Value;
            if (path is not null)
            {
                var match = VersionPrefix.Match(path);
                if (match.Success)
                {
                    context.Request.Path = path[match.Length..];
                }
            }

            try
            {
                await next(context);
            }
            finally
            {
                context.Request.Path = originalPath;
            }
        });
    }
}
