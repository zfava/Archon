using ArchonAI.Core.Models.AgentRegistry;
using ArchonAI.Enterprise.Tests.Infrastructure;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Real PostgreSQL integration tests for <see cref="ArchonAI.Persistence.Stores.PostgresAgentRegistryStore"/>.
/// Uses Testcontainers to start an ephemeral PostgreSQL instance.
///
/// These tests prove that agent registry state survives store re-instantiation —
/// the critical multi-instance correctness guarantee that in-memory tests cannot provide.
/// </summary>
[Collection("PostgresAgentRegistryControlPlane")]
[Trait("Category", "Integration")]
[Trait("Database", "PostgreSQL")]
[Trait("Subsystem", "AgentRegistry")]
public sealed class PostgresAgentRegistryPersistenceTests : IAsyncLifetime
{
    private readonly PostgresAgentRegistryControlPlaneFixture _fixture;

    public PostgresAgentRegistryPersistenceTests(PostgresAgentRegistryControlPlaneFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanTablesAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Test 1: Upsert and retrieve agent ───────────────────────────

    [Fact]
    public async Task UpsertAgent_PersistsAndRetrievesCorrectly()
    {
        var store = _fixture.CreateAgentRegistryStore();
        var agent = MakeAgent("finance-agent", "Handles financial operations");

        var result = await store.UpsertAgentAsync(agent);

        Assert.Equal(agent.Id, result.Id);
        Assert.Equal("finance-agent", result.Name);

        var fetched = await store.GetAgentAsync(agent.Id);
        Assert.NotNull(fetched);
        Assert.Equal(agent.Id, fetched.Id);
        Assert.Equal("finance-agent", fetched.Name);
        Assert.Equal("Handles financial operations", fetched.Description);
        Assert.Equal(RegisteredAgentStatus.Active, fetched.Status);
        Assert.Single(fetched.Capabilities);
        Assert.Equal("finance", fetched.Capabilities[0].Name);
    }

    // ── Test 2: Agent survives store re-instantiation (restart proof) ─

    [Fact]
    public async Task Agent_SurvivesStoreReinstantiation()
    {
        var store1 = _fixture.CreateAgentRegistryStore();
        var agent = MakeAgent("restart-agent", "Testing restart persistence");
        await store1.UpsertAgentAsync(agent);

        // Simulate service restart — new store instance, same database
        var store2 = _fixture.CreateAgentRegistryStore();
        var fetched = await store2.GetAgentAsync(agent.Id);

        Assert.NotNull(fetched);
        Assert.Equal(agent.Id, fetched.Id);
        Assert.Equal("restart-agent", fetched.Name);
        Assert.Equal("Testing restart persistence", fetched.Description);
    }

    // ── Test 3: Two instances see same state (multi-instance proof) ──

    [Fact]
    public async Task TwoInstances_SeeSameAgentState()
    {
        var instanceA = _fixture.CreateAgentRegistryStore();
        var instanceB = _fixture.CreateAgentRegistryStore();

        var agent = MakeAgent("shared-agent", "Visible to both instances");
        await instanceA.UpsertAgentAsync(agent);

        // Instance B should see the agent written by instance A
        var fetched = await instanceB.GetAgentAsync(agent.Id);
        Assert.NotNull(fetched);
        Assert.Equal("shared-agent", fetched.Name);

        // Instance B updates the agent
        var updated = agent with { Status = RegisteredAgentStatus.Disabled, DisabledAtUtc = DateTimeOffset.UtcNow };
        await instanceB.UpsertAgentAsync(updated);

        // Instance A should see the updated status
        var reFetched = await instanceA.GetAgentAsync(agent.Id);
        Assert.NotNull(reFetched);
        Assert.Equal(RegisteredAgentStatus.Disabled, reFetched.Status);
        Assert.NotNull(reFetched.DisabledAtUtc);
    }

    // ── Test 4: List agents with status filter ──────────────────────

    [Fact]
    public async Task ListAgents_FiltersByStatus()
    {
        var store = _fixture.CreateAgentRegistryStore();

        var active1 = MakeAgent("active-1", "Active agent 1");
        var active2 = MakeAgent("active-2", "Active agent 2");
        var disabled = MakeAgent("disabled-1", "Disabled agent") with { Status = RegisteredAgentStatus.Disabled };

        await store.UpsertAgentAsync(active1);
        await store.UpsertAgentAsync(active2);
        await store.UpsertAgentAsync(disabled);

        var activeAgents = await store.ListAgentsAsync(RegisteredAgentStatus.Active, null, 0, 100);
        Assert.Equal(2, activeAgents.Count);

        var disabledAgents = await store.ListAgentsAsync(RegisteredAgentStatus.Disabled, null, 0, 100);
        Assert.Single(disabledAgents);
    }

    // ── Test 5: Remove agent cascades to metrics ────────────────────

    [Fact]
    public async Task RemoveAgent_DeletesAgentAndMetrics()
    {
        var store = _fixture.CreateAgentRegistryStore();
        var agent = MakeAgent("removable-agent", "Will be removed");
        await store.UpsertAgentAsync(agent);

        var metric = new AgentMetricSnapshot(
            Guid.NewGuid(), agent.Id, 100, 95, 5, 42.5, 120.0, 99.9, DateTimeOffset.UtcNow);
        await store.AddMetricAsync(metric);

        var removed = await store.RemoveAgentAsync(agent.Id);
        Assert.True(removed);

        var fetched = await store.GetAgentAsync(agent.Id);
        Assert.Null(fetched);

        // Metrics should be cascade-deleted
        var metrics = await store.GetMetricsAsync(agent.Id, 100);
        Assert.Empty(metrics);
    }

    // ── Test 6: CountAgents ─────────────────────────────────────────

    [Fact]
    public async Task CountAgents_ReturnsCorrectCounts()
    {
        var store = _fixture.CreateAgentRegistryStore();

        await store.UpsertAgentAsync(MakeAgent("a1", "Agent 1"));
        await store.UpsertAgentAsync(MakeAgent("a2", "Agent 2") with { Status = RegisteredAgentStatus.Offline });

        var total = await store.CountAgentsAsync();
        Assert.Equal(2, total);

        var activeCount = await store.CountAgentsAsync(RegisteredAgentStatus.Active);
        Assert.Equal(1, activeCount);
    }

    // ── Test 7: Metrics persistence ─────────────────────────────────

    [Fact]
    public async Task Metrics_PersistAndQueryCorrectly()
    {
        var store = _fixture.CreateAgentRegistryStore();
        var agent = MakeAgent("metrics-agent", "Agent with metrics");
        await store.UpsertAgentAsync(agent);

        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 5; i++)
        {
            await store.AddMetricAsync(new AgentMetricSnapshot(
                Guid.NewGuid(), agent.Id,
                i * 10, i * 9, i, 50.0 + i, 100.0 + i, 99.0,
                now.AddMinutes(-i)));
        }

        var agentMetrics = await store.GetMetricsAsync(agent.Id, 3);
        Assert.Equal(3, agentMetrics.Count);

        var recentMetrics = await store.GetRecentMetricsAsync(10);
        Assert.Equal(5, recentMetrics.Count);
    }

    // ── Test 8: Upsert is idempotent ────────────────────────────────

    [Fact]
    public async Task UpsertAgent_IsIdempotent()
    {
        var store = _fixture.CreateAgentRegistryStore();
        var agent = MakeAgent("idempotent-agent", "First version");

        await store.UpsertAgentAsync(agent);
        var updated = agent with { Description = "Updated version", Version = "2.0.0" };
        await store.UpsertAgentAsync(updated);

        var fetched = await store.GetAgentAsync(agent.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Updated version", fetched.Description);
        Assert.Equal("2.0.0", fetched.Version);

        var count = await store.CountAgentsAsync();
        Assert.Equal(1, count);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static RegisteredAgent MakeAgent(string name, string description) =>
        new(
            Id: Guid.NewGuid(),
            Name: name,
            Description: description,
            Version: "1.0.0",
            Status: RegisteredAgentStatus.Active,
            Capabilities: new List<AgentCapabilityRecord>
            {
                new(Guid.NewGuid(), Guid.Empty, "finance", "Financial ops", "domain", "1.0", DateTimeOffset.UtcNow)
            },
            Configuration: new Dictionary<string, string> { ["model"] = "gpt-4.1" },
            RegisteredAtUtc: DateTimeOffset.UtcNow,
            LastHeartbeatUtc: DateTimeOffset.UtcNow,
            DisabledAtUtc: null);
}
