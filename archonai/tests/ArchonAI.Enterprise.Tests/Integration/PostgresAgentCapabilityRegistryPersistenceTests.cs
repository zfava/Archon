using ArchonAI.Core.Models;
using ArchonAI.Enterprise.Tests.Infrastructure;
using ArchonAI.Registry;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Real PostgreSQL integration tests for <see cref="ArchonAI.Persistence.Stores.PostgresAgentCapabilityRegistryStore"/>.
/// Uses Testcontainers to start an ephemeral PostgreSQL instance.
///
/// These tests prove that agent capability profiles, execution samples, and
/// performance aggregates survive store re-instantiation and are consistent
/// across multiple instances — eliminating the ConcurrentDictionary-based
/// single-instance limitation of <see cref="InMemoryAgentCapabilityRegistry"/>.
/// </summary>
[Collection("PostgresAgentRegistryControlPlane")]
[Trait("Category", "Integration")]
[Trait("Database", "PostgreSQL")]
[Trait("Subsystem", "AgentCapabilityRegistry")]
public sealed class PostgresAgentCapabilityRegistryPersistenceTests : IAsyncLifetime
{
    private readonly PostgresAgentRegistryControlPlaneFixture _fixture;

    public PostgresAgentCapabilityRegistryPersistenceTests(PostgresAgentRegistryControlPlaneFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanTablesAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Test 1: Register agent and retrieve profile ───────────────

    [Fact]
    public async Task RegisterAgent_PersistsAndRetrievesProfile()
    {
        var store = _fixture.CreateAgentCapabilityRegistryStore();
        var agent = MakeAgent("cap-agent-1");

        await store.RegisterOrUpdateAgentAsync(
            agent,
            new[] { "code-review", "summarize" },
            new[] { "read:repo", "write:comments" });

        var profile = await store.GetAgentAsync(agent.Id);
        Assert.NotNull(profile);
        Assert.Equal(agent.Id, profile.AgentId);
        Assert.Equal("cap-agent-1", profile.AgentName);
        Assert.Equal("1.0.0", profile.Version);
        Assert.Contains("analysis", profile.Capabilities);
        Assert.Contains("code-review", profile.Tools);
        Assert.Contains("read:repo", profile.Permissions);
    }

    // ── Test 2: Profile survives store re-instantiation ───────────

    [Fact]
    public async Task Profile_SurvivesStoreReinstantiation()
    {
        var store1 = _fixture.CreateAgentCapabilityRegistryStore();
        var agent = MakeAgent("restart-cap-agent");

        await store1.RegisterOrUpdateAgentAsync(
            agent, new[] { "tool-a" }, new[] { "perm-a" });

        var store2 = _fixture.CreateAgentCapabilityRegistryStore();
        var profile = await store2.GetAgentAsync(agent.Id);

        Assert.NotNull(profile);
        Assert.Equal("restart-cap-agent", profile.AgentName);
    }

    // ── Test 3: Two instances see same profile state ──────────────

    [Fact]
    public async Task TwoInstances_SeeSameProfileState()
    {
        var instanceA = _fixture.CreateAgentCapabilityRegistryStore();
        var instanceB = _fixture.CreateAgentCapabilityRegistryStore();

        var agent = MakeAgent("shared-cap-agent");
        await instanceA.RegisterOrUpdateAgentAsync(
            agent, new[] { "tool-x" }, new[] { "perm-x" });

        // Instance B sees the profile
        var profile = await instanceB.GetAgentAsync(agent.Id);
        Assert.NotNull(profile);
        Assert.Equal("shared-cap-agent", profile.AgentName);

        // Instance B suspends the agent
        await instanceB.SuspendAgentAsync(agent.Id, "maintenance");

        // Instance A sees the suspension
        var suspended = await instanceA.GetAgentAsync(agent.Id);
        Assert.NotNull(suspended);
        Assert.True(suspended.IsSuspended);
        Assert.Equal("maintenance", suspended.SuspendReason);
    }

    // ── Test 4: Execution reporting updates aggregates ────────────

    [Fact]
    public async Task ReportExecution_UpdatesAggregates()
    {
        var store = _fixture.CreateAgentCapabilityRegistryStore();
        var agent = MakeAgent("exec-agent");
        await store.RegisterOrUpdateAgentAsync(
            agent, new[] { "tool" }, new[] { "perm" });

        // Report several executions
        await store.ReportExecutionAsync(agent.Id, "code-review", true, 100.0, 0.01m);
        await store.ReportExecutionAsync(agent.Id, "code-review", true, 200.0, 0.02m);
        await store.ReportExecutionAsync(agent.Id, "code-review", false, 500.0, 0.05m);

        var profile = await store.GetAgentAsync(agent.Id);
        Assert.NotNull(profile);
        Assert.Equal(3, profile.Executions);
        Assert.Equal(2, profile.SuccessCount);
        Assert.Equal(1, profile.FailureCount);
        Assert.True(profile.AverageLatencyMs > 0);
    }

    // ── Test 5: Execution data visible across instances ───────────

    [Fact]
    public async Task ExecutionData_VisibleAcrossInstances()
    {
        var instanceA = _fixture.CreateAgentCapabilityRegistryStore();
        var instanceB = _fixture.CreateAgentCapabilityRegistryStore();

        var agent = MakeAgent("multi-exec-agent");
        await instanceA.RegisterOrUpdateAgentAsync(
            agent, new[] { "tool" }, new[] { "perm" });

        // Instance A reports executions
        await instanceA.ReportExecutionAsync(agent.Id, "summarize", true, 150.0, 0.02m);
        await instanceA.ReportExecutionAsync(agent.Id, "summarize", true, 250.0, 0.03m);

        // Instance B sees the updated performance
        var profile = await instanceB.GetAgentAsync(agent.Id);
        Assert.NotNull(profile);
        Assert.Equal(2, profile.Executions);
        Assert.Equal(2, profile.SuccessCount);
    }

    // ── Test 6: Suspend and reinstate across instances ────────────

    [Fact]
    public async Task SuspendAndReinstate_SharedAcrossInstances()
    {
        var instanceA = _fixture.CreateAgentCapabilityRegistryStore();
        var instanceB = _fixture.CreateAgentCapabilityRegistryStore();

        var agent = MakeAgent("suspend-agent");
        await instanceA.RegisterOrUpdateAgentAsync(
            agent, new[] { "tool" }, new[] { "perm" });

        // Instance A suspends
        await instanceA.SuspendAgentAsync(agent.Id, "high failure rate");

        // Instance B sees suspension
        var suspended = await instanceB.GetAgentAsync(agent.Id);
        Assert.NotNull(suspended);
        Assert.True(suspended.IsSuspended);

        // Instance B reinstates
        await instanceB.ReinstateAgentAsync(agent.Id);

        // Instance A sees reinstatement
        var reinstated = await instanceA.GetAgentAsync(agent.Id);
        Assert.NotNull(reinstated);
        Assert.False(reinstated.IsSuspended);
        Assert.Null(reinstated.SuspendReason);
    }

    // ── Test 7: Query by capability ──────────────────────────────

    [Fact]
    public async Task QueryByCapability_ReturnsMatchingAgents()
    {
        var store = _fixture.CreateAgentCapabilityRegistryStore();

        var agent1 = MakeAgent("finance-agent", "analysis", "reporting");
        var agent2 = MakeAgent("ops-agent", "monitoring", "alerting");
        var agent3 = MakeAgent("hybrid-agent", "analysis", "monitoring");

        await store.RegisterOrUpdateAgentAsync(agent1, Array.Empty<string>(), Array.Empty<string>());
        await store.RegisterOrUpdateAgentAsync(agent2, Array.Empty<string>(), Array.Empty<string>());
        await store.RegisterOrUpdateAgentAsync(agent3, Array.Empty<string>(), Array.Empty<string>());

        var analysisAgents = await store.QueryByCapabilityAsync("analysis");
        Assert.Equal(2, analysisAgents.Count);
        Assert.All(analysisAgents, a => Assert.Contains("analysis", a.Capabilities));
    }

    // ── Test 8: Query by task type ───────────────────────────────

    [Fact]
    public async Task QueryByTaskType_ReturnsMatchingAgents()
    {
        var store = _fixture.CreateAgentCapabilityRegistryStore();

        var agent = MakeAgent("task-agent");
        await store.RegisterOrUpdateAgentAsync(agent, Array.Empty<string>(), Array.Empty<string>());
        await store.RegisterSupportedTaskTypesAsync(agent.Id, new[] { "code-review", "summarize" });

        var codeReviewAgents = await store.QueryByTaskTypeAsync("code-review");
        Assert.Single(codeReviewAgents);
        Assert.Equal(agent.Id, codeReviewAgents[0].AgentId);
    }

    // ── Test 9: GetAll returns all registered agents ─────────────

    [Fact]
    public async Task GetAll_ReturnsAllAgents()
    {
        var store = _fixture.CreateAgentCapabilityRegistryStore();

        await store.RegisterOrUpdateAgentAsync(
            MakeAgent("all-1"), Array.Empty<string>(), Array.Empty<string>());
        await store.RegisterOrUpdateAgentAsync(
            MakeAgent("all-2"), Array.Empty<string>(), Array.Empty<string>());

        var all = await store.GetAllAsync();
        Assert.Equal(2, all.Count);
    }

    // ── Test 10: Performance snapshot ────────────────────────────

    [Fact]
    public async Task GetPerformanceSnapshot_ReturnsCorrectData()
    {
        var store = _fixture.CreateAgentCapabilityRegistryStore();
        var agent = MakeAgent("perf-agent");
        await store.RegisterOrUpdateAgentAsync(
            agent, new[] { "tool" }, new[] { "perm" });

        await store.ReportExecutionAsync(agent.Id, "task", true, 100.0, 0.01m);
        await store.ReportExecutionAsync(agent.Id, "task", true, 200.0, 0.02m);

        var snapshot = await store.GetPerformanceSnapshotAsync(agent.Id);
        Assert.NotNull(snapshot);
        Assert.Equal(agent.Id, snapshot.AgentId);
        Assert.Equal(2, snapshot.Executions);
        Assert.Equal(1.0, snapshot.SuccessRate);
        Assert.True(snapshot.AverageLatencyMs > 0);
    }

    // ── Test 11: SelectBestAgent picks highest-scoring ────────────

    [Fact]
    public async Task SelectBestAgent_PicksHighestScoringAgent()
    {
        var store = _fixture.CreateAgentCapabilityRegistryStore();

        var goodAgent = MakeAgent("good-agent", "analysis");
        var poorAgent = MakeAgent("poor-agent", "analysis");

        await store.RegisterOrUpdateAgentAsync(goodAgent, Array.Empty<string>(), Array.Empty<string>());
        await store.RegisterOrUpdateAgentAsync(poorAgent, Array.Empty<string>(), Array.Empty<string>());

        // Good agent: fast, cheap, reliable
        for (int i = 0; i < 5; i++)
            await store.ReportExecutionAsync(goodAgent.Id, "analysis", true, 50.0, 0.01m);

        // Poor agent: slow, expensive, unreliable
        for (int i = 0; i < 5; i++)
            await store.ReportExecutionAsync(poorAgent.Id, "analysis", i < 2, 500.0, 0.10m);

        var best = await store.SelectBestAgentAsync("analysis", null);
        Assert.NotNull(best);
        Assert.Equal(goodAgent.Id, best.AgentId);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static Agent MakeAgent(string name, params string[] capabilities)
    {
        var caps = capabilities.Length > 0
            ? capabilities.Select(c => new AgentCapability(c, $"{c} capability", "domain", "1.0")).ToList()
            : new List<AgentCapability> { new("analysis", "Analysis capability", "domain", "1.0") };

        return new Agent(
            Id: Guid.NewGuid(),
            Name: name,
            Version: "1.0.0",
            Capabilities: caps,
            IsEnabled: true,
            RegisteredAtUtc: DateTimeOffset.UtcNow);
    }
}
