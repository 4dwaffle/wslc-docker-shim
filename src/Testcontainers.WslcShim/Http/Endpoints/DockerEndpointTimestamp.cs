using System.Globalization;
using Testcontainers.WslcShim.Docker;

namespace Testcontainers.WslcShim.Http.Endpoints;

internal static class DockerEndpointTimestamp
{
    public static DateTimeOffset GetCreationTime(DockerResourceSnapshot resource)
    {
        return resource.CreatedAt ?? DateTimeOffset.UnixEpoch;
    }

    public static string Format(DateTimeOffset createdAt)
    {
        return createdAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    }
}
