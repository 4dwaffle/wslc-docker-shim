using System.Collections.Concurrent;
using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.Ryuk;

public sealed class RyukCleanupSessionRegistry
{
    private const string RyukContainerNamePrefix = "testcontainers-ryuk-";
    private static readonly TimeSpan DefaultCallerSessionLifetime = TimeSpan.FromMinutes(5);
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan callerSessionLifetime;
    private readonly ConcurrentDictionary<string, byte> allowedSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ActiveSession> activeSessions = new(StringComparer.Ordinal);

    public RyukCleanupSessionRegistry()
        : this(TimeProvider.System, DefaultCallerSessionLifetime)
    {
    }

    internal RyukCleanupSessionRegistry(TimeProvider timeProvider, TimeSpan callerSessionLifetime)
    {
        this.timeProvider = timeProvider;
        this.callerSessionLifetime = callerSessionLifetime;
    }

    public bool RegisterRyukContainer(DockerContainerCreateRequest request)
    {
        if (!TryGetRyukSession(request, out var session))
        {
            return false;
        }

        allowedSessions.TryAdd(session, 0);
        return true;
    }

    public bool TryActivate(string callerIdentity, DockerLabelFilters filters)
    {
        if (!RestrictedRyukCleanupPolicy.TryGetRequestedSession(filters, out var requestedSession) ||
            !allowedSessions.ContainsKey(requestedSession))
        {
            return false;
        }

        while (true)
        {
            var now = timeProvider.GetTimestamp();
            if (!activeSessions.TryGetValue(callerIdentity, out var activeSession))
            {
                if (activeSessions.TryAdd(callerIdentity, new ActiveSession(requestedSession, now)))
                {
                    return true;
                }

                continue;
            }

            if (IsExpired(activeSession, now))
            {
                if (activeSessions.TryUpdate(
                        callerIdentity,
                        new ActiveSession(requestedSession, now),
                        activeSession))
                {
                    return true;
                }

                continue;
            }

            if (!string.Equals(activeSession.Session, requestedSession, StringComparison.Ordinal))
            {
                return false;
            }

            if (activeSessions.TryUpdate(
                    callerIdentity,
                    activeSession with { LastSeen = now },
                    activeSession))
            {
                return true;
            }
        }
    }

    public bool TryGetActiveSession(string callerIdentity, out string session)
    {
        while (activeSessions.TryGetValue(callerIdentity, out var activeSession))
        {
            var now = timeProvider.GetTimestamp();
            if (IsExpired(activeSession, now))
            {
                activeSessions.TryRemove(new KeyValuePair<string, ActiveSession>(callerIdentity, activeSession));
                break;
            }

            if (activeSessions.TryUpdate(
                    callerIdentity,
                    activeSession with { LastSeen = now },
                    activeSession))
            {
                session = activeSession.Session;
                return true;
            }
        }

        session = string.Empty;
        return false;
    }

    private static bool TryGetRyukSession(DockerContainerCreateRequest request, out string session)
    {
        var hasSessionLabel = request.Labels.TryGetValue(
            RestrictedRyukCleanupPolicy.TestcontainersSessionLabel,
            out var sessionLabel);
        var isPlaceholderLabel = Guid.TryParse(sessionLabel, out var parsedLabel) && parsedLabel == Guid.Empty;
        var labelSession = hasSessionLabel && !isPlaceholderLabel ? NormalizeSession(sessionLabel!) : null;
        var nameSession = TryParseSessionName(request.Name);

        if (hasSessionLabel && !isPlaceholderLabel && labelSession is null)
        {
            session = string.Empty;
            return false;
        }

        if (labelSession is not null && nameSession is not null && labelSession != nameSession)
        {
            session = string.Empty;
            return false;
        }

        session = labelSession ?? nameSession ?? string.Empty;
        return session.Length > 0;
    }

    private static string? TryParseSessionName(string? name)
    {
        if (name is null || !name.StartsWith(RyukContainerNamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return NormalizeSession(name[RyukContainerNamePrefix.Length..]);
    }

    private static string? NormalizeSession(string value)
    {
        return RestrictedRyukCleanupPolicy.TryNormalizeSession(value, out var session)
            ? session
            : null;
    }

    private bool IsExpired(ActiveSession activeSession, long now)
    {
        return timeProvider.GetElapsedTime(activeSession.LastSeen, now) >= callerSessionLifetime;
    }

    private sealed record ActiveSession(string Session, long LastSeen);
}
