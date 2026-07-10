using Microsoft.AspNetCore.Http;
using Testcontainers.WslcShim.Docker.Models;

namespace Testcontainers.WslcShim.Ryuk;

public sealed class RyukCleanupSessionRegistry
{
    private const string RyukContainerNamePrefix = "testcontainers-ryuk-";
    private const string InProcessCaller = "in-process-test-server";
    private const int DefaultMaximumAllowedSessions = 1024;
    private const int DefaultMaximumActiveSessions = 1024;
    private static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromMinutes(5);
    private readonly object syncRoot = new();
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan allowedSessionLifetime;
    private readonly TimeSpan callerSessionLifetime;
    private readonly int maximumAllowedSessions;
    private readonly int maximumActiveSessions;
    private readonly Dictionary<string, AllowedSession> allowedSessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActiveSession> activeSessions = new(StringComparer.Ordinal);

    public RyukCleanupSessionRegistry()
        : this(
            TimeProvider.System,
            DefaultSessionLifetime,
            DefaultSessionLifetime,
            DefaultMaximumAllowedSessions,
            DefaultMaximumActiveSessions)
    {
    }

    internal RyukCleanupSessionRegistry(TimeProvider timeProvider, TimeSpan callerSessionLifetime)
        : this(
            timeProvider,
            DefaultSessionLifetime,
            callerSessionLifetime,
            DefaultMaximumAllowedSessions,
            DefaultMaximumActiveSessions)
    {
    }

    internal RyukCleanupSessionRegistry(
        TimeProvider timeProvider,
        TimeSpan allowedSessionLifetime,
        TimeSpan callerSessionLifetime,
        int maximumAllowedSessions,
        int maximumActiveSessions)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(allowedSessionLifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(callerSessionLifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumAllowedSessions, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumActiveSessions, 0);

        this.timeProvider = timeProvider;
        this.allowedSessionLifetime = allowedSessionLifetime;
        this.callerSessionLifetime = callerSessionLifetime;
        this.maximumAllowedSessions = maximumAllowedSessions;
        this.maximumActiveSessions = maximumActiveSessions;
    }

    internal int AllowedSessionCount
    {
        get
        {
            lock (syncRoot)
            {
                return allowedSessions.Count;
            }
        }
    }

    internal int ActiveSessionCount
    {
        get
        {
            lock (syncRoot)
            {
                return activeSessions.Count;
            }
        }
    }

    public bool RegisterRyukContainer(DockerContainerCreateRequest request)
    {
        if (!TryGetRyukSession(request, out var session))
        {
            return false;
        }

        lock (syncRoot)
        {
            var now = timeProvider.GetTimestamp();
            PruneExpiredSessions(now);

            if (!allowedSessions.ContainsKey(session))
            {
                EnsureAllowedSessionCapacity();
            }

            allowedSessions[session] = new AllowedSession(now);
        }

        return true;
    }

    public bool TryActivate(HttpContext context, DockerLabelFilters filters)
    {
        return TryActivate(GetCallerIdentity(context), filters);
    }

    public bool TryActivate(string callerIdentity, DockerLabelFilters filters)
    {
        if (!RestrictedRyukCleanupPolicy.TryGetRequestedSession(filters, out var requestedSession))
        {
            return false;
        }

        lock (syncRoot)
        {
            var now = timeProvider.GetTimestamp();
            PruneExpiredSessions(now);

            if (!allowedSessions.ContainsKey(requestedSession))
            {
                return false;
            }

            if (!activeSessions.TryGetValue(callerIdentity, out var activeSession))
            {
                EnsureActiveSessionCapacity();
                activeSessions.Add(callerIdentity, new ActiveSession(requestedSession, now));
                return true;
            }

            if (!string.Equals(activeSession.Session, requestedSession, StringComparison.Ordinal))
            {
                return false;
            }

            activeSessions[callerIdentity] = activeSession with { LastSeen = now };
            return true;
        }
    }

    public bool TryGetActiveSession(HttpContext context, out string session)
    {
        return TryGetActiveSession(GetCallerIdentity(context), out session);
    }

    public bool TryGetActiveSession(string callerIdentity, out string session)
    {
        lock (syncRoot)
        {
            var now = timeProvider.GetTimestamp();
            PruneExpiredSessions(now);

            if (activeSessions.TryGetValue(callerIdentity, out var activeSession))
            {
                activeSessions[callerIdentity] = activeSession with { LastSeen = now };
                session = activeSession.Session;
                return true;
            }
        }

        session = string.Empty;
        return false;
    }

    private static string GetCallerIdentity(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? InProcessCaller;
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

    private void PruneExpiredSessions(long now)
    {
        var expiredAllowedSessions = new List<string>();
        foreach (var allowedSession in allowedSessions)
        {
            if (IsExpired(allowedSession.Value.RegisteredAt, now, allowedSessionLifetime))
            {
                expiredAllowedSessions.Add(allowedSession.Key);
            }
        }

        foreach (var session in expiredAllowedSessions)
        {
            allowedSessions.Remove(session);
        }

        var expiredActiveSessions = new List<string>();
        foreach (var activeSession in activeSessions)
        {
            if (IsExpired(activeSession.Value.LastSeen, now, callerSessionLifetime) ||
                !allowedSessions.ContainsKey(activeSession.Value.Session))
            {
                expiredActiveSessions.Add(activeSession.Key);
            }
        }

        foreach (var caller in expiredActiveSessions)
        {
            activeSessions.Remove(caller);
        }
    }

    private void EnsureAllowedSessionCapacity()
    {
        while (allowedSessions.Count >= maximumAllowedSessions)
        {
            var sessionToRemove = FindOldestAllowedSession();
            allowedSessions.Remove(sessionToRemove);
        }

        RemoveActiveSessionsWithoutAllowedRegistration();
    }

    private void EnsureActiveSessionCapacity()
    {
        while (activeSessions.Count >= maximumActiveSessions)
        {
            activeSessions.Remove(FindOldestActiveSession());
        }
    }

    private string FindOldestAllowedSession()
    {
        string? oldestSession = null;
        var oldestTimestamp = long.MaxValue;

        foreach (var allowedSession in allowedSessions)
        {
            if (allowedSession.Value.RegisteredAt < oldestTimestamp ||
                allowedSession.Value.RegisteredAt == oldestTimestamp &&
                (oldestSession is null || string.CompareOrdinal(allowedSession.Key, oldestSession) < 0))
            {
                oldestSession = allowedSession.Key;
                oldestTimestamp = allowedSession.Value.RegisteredAt;
            }
        }

        return oldestSession!;
    }

    private string FindOldestActiveSession()
    {
        string? oldestCaller = null;
        var oldestTimestamp = long.MaxValue;

        foreach (var activeSession in activeSessions)
        {
            if (activeSession.Value.LastSeen < oldestTimestamp ||
                activeSession.Value.LastSeen == oldestTimestamp &&
                (oldestCaller is null || string.CompareOrdinal(activeSession.Key, oldestCaller) < 0))
            {
                oldestCaller = activeSession.Key;
                oldestTimestamp = activeSession.Value.LastSeen;
            }
        }

        return oldestCaller!;
    }

    private void RemoveActiveSessionsWithoutAllowedRegistration()
    {
        var callersToRemove = new List<string>();
        foreach (var activeSession in activeSessions)
        {
            if (!allowedSessions.ContainsKey(activeSession.Value.Session))
            {
                callersToRemove.Add(activeSession.Key);
            }
        }

        foreach (var caller in callersToRemove)
        {
            activeSessions.Remove(caller);
        }
    }

    private bool IsExpired(long timestamp, long now, TimeSpan lifetime)
    {
        return timeProvider.GetElapsedTime(timestamp, now) >= lifetime;
    }

    private sealed record AllowedSession(long RegisteredAt);

    private sealed record ActiveSession(string Session, long LastSeen);
}
