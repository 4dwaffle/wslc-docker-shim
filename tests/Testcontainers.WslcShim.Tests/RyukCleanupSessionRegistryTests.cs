using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Tests;

public sealed class RyukCleanupSessionRegistryTests
{
    private const string SessionA = "11111111-1111-1111-1111-111111111111";
    private const string SessionB = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public void TryActivate_requires_a_session_registered_by_a_ryuk_create()
    {
        var registry = new RyukCleanupSessionRegistry();

        Assert.False(registry.TryActivate("caller-a", FiltersFor(SessionA)));
    }

    [Fact]
    public void TryActivate_registers_and_returns_the_callers_active_session()
    {
        var registry = new RyukCleanupSessionRegistry();
        Assert.True(registry.RegisterRyukContainer(RyukCreateRequest(SessionA)));

        Assert.True(registry.TryActivate("caller-a", FiltersFor(SessionA)));
        Assert.True(registry.TryGetActiveSession("caller-a", out var activeSession));
        Assert.Equal(SessionA, activeSession);
    }

    [Fact]
    public void RegisterRyukContainer_uses_the_container_name_when_the_session_label_is_a_placeholder()
    {
        var registry = new RyukCleanupSessionRegistry();
        var request = RyukCreateRequest(SessionA) with
        {
            Labels = new Dictionary<string, string>
            {
                ["org.testcontainers.session-id"] = Guid.Empty.ToString("D")
            }
        };

        Assert.True(registry.RegisterRyukContainer(request));
        Assert.True(registry.TryActivate("caller-a", FiltersFor(SessionA)));
    }

    [Fact]
    public void TryActivate_normalizes_equivalent_guid_filter_values()
    {
        var registry = new RyukCleanupSessionRegistry();
        registry.RegisterRyukContainer(RyukCreateRequest(SessionA));
        var equivalentSession = Guid.Parse(SessionA).ToString("B").ToUpperInvariant();

        Assert.True(registry.TryActivate("caller-a", FiltersFor(equivalentSession)));
        Assert.True(registry.TryGetActiveSession("caller-a", out var activeSession));
        Assert.Equal(SessionA, activeSession);
    }

    [Fact]
    public void TryActivate_does_not_allow_a_caller_to_switch_sessions()
    {
        var registry = new RyukCleanupSessionRegistry();
        registry.RegisterRyukContainer(RyukCreateRequest(SessionA));
        registry.RegisterRyukContainer(RyukCreateRequest(SessionB));
        Assert.True(registry.TryActivate("caller-a", FiltersFor(SessionA)));

        Assert.False(registry.TryActivate("caller-a", FiltersFor(SessionB)));
        Assert.True(registry.TryGetActiveSession("caller-a", out var activeSession));
        Assert.Equal(SessionA, activeSession);
    }

    [Fact]
    public void TryActivate_keeps_concurrent_callers_in_separate_sessions()
    {
        var registry = new RyukCleanupSessionRegistry();
        registry.RegisterRyukContainer(RyukCreateRequest(SessionA));
        registry.RegisterRyukContainer(RyukCreateRequest(SessionB));

        Assert.True(registry.TryActivate("10.0.0.2", FiltersFor(SessionA)));
        Assert.True(registry.TryActivate("10.0.0.3", FiltersFor(SessionB)));
        Assert.True(registry.TryGetActiveSession("10.0.0.2", out var activeSessionA));
        Assert.True(registry.TryGetActiveSession("10.0.0.3", out var activeSessionB));
        Assert.Equal(SessionA, activeSessionA);
        Assert.Equal(SessionB, activeSessionB);
    }

    [Fact]
    public void TryActivate_allows_an_ip_to_be_reused_after_the_active_session_expires()
    {
        var timeProvider = new ManualTimeProvider();
        var registry = new RyukCleanupSessionRegistry(timeProvider, TimeSpan.FromMinutes(1));
        registry.RegisterRyukContainer(RyukCreateRequest(SessionA));
        registry.RegisterRyukContainer(RyukCreateRequest(SessionB));
        Assert.True(registry.TryActivate("10.0.0.2", FiltersFor(SessionA)));

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        Assert.True(registry.TryActivate("10.0.0.2", FiltersFor(SessionB)));
        Assert.True(registry.TryGetActiveSession("10.0.0.2", out var activeSession));
        Assert.Equal(SessionB, activeSession);
    }

    private static DockerContainerCreateRequest RyukCreateRequest(string session)
    {
        return new DockerContainerCreateRequest
        {
            Image = "testcontainers/ryuk:0.14.0",
            Name = $"testcontainers-ryuk-{session}",
            Labels = new Dictionary<string, string>
            {
                ["org.testcontainers.session-id"] = session,
                ["org.testcontainers.resource-reaper-session"] = Guid.Empty.ToString("D")
            }
        };
    }

    private static DockerLabelFilters FiltersFor(string session)
    {
        return DockerLabelFilters.FromDockerFiltersQuery(
            $$$"""{"label":{"org.testcontainers.resource-reaper-session={{{session}}}":true}}""");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return timestamp;
        }

        public void Advance(TimeSpan duration)
        {
            timestamp += duration.Ticks;
        }
    }
}
