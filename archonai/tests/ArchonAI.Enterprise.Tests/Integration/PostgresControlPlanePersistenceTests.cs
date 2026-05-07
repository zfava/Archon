using ArchonAI.Core.Models.ControlPlane;
using ArchonAI.Enterprise.Tests.Infrastructure;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Real PostgreSQL integration tests for <see cref="ArchonAI.Persistence.Stores.PostgresControlPlaneStore"/>.
/// Uses Testcontainers to start an ephemeral PostgreSQL instance.
///
/// These tests prove that control plane state survives store re-instantiation and
/// that multiple instances see the same state — eliminating the file-backed JSON
/// single-instance limitation.
/// </summary>
[Collection("PostgresAgentRegistryControlPlane")]
[Trait("Category", "Integration")]
[Trait("Database", "PostgreSQL")]
[Trait("Subsystem", "ControlPlane")]
public sealed class PostgresControlPlanePersistenceTests : IAsyncLifetime
{
    private readonly PostgresAgentRegistryControlPlaneFixture _fixture;

    public PostgresControlPlanePersistenceTests(PostgresAgentRegistryControlPlaneFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanTablesAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Tenant Tests ────────────────────────────────────────────────

    [Fact]
    public async Task UpsertTenant_PersistsAndRetrievesCorrectly()
    {
        var store = _fixture.CreateControlPlaneStore();
        var tenant = MakeTenant("acme-corp", "Acme Corporation");

        await store.UpsertTenantAsync(tenant);

        var fetched = await store.GetTenantAsync(tenant.Id);
        Assert.NotNull(fetched);
        Assert.Equal(tenant.Id, fetched.Id);
        Assert.Equal("acme-corp", fetched.Name);
        Assert.Equal("Acme Corporation", fetched.DisplayName);
        Assert.Equal(TenantStatus.Active, fetched.Status);
        Assert.Equal(TenantTier.Professional, fetched.Tier);
        Assert.Equal(10, fetched.ResourceQuota.MaxAgents);
    }

    [Fact]
    public async Task GetTenantByName_Works()
    {
        var store = _fixture.CreateControlPlaneStore();
        var tenant = MakeTenant("lookup-corp", "Lookup Corp");
        await store.UpsertTenantAsync(tenant);

        var fetched = await store.GetTenantByNameAsync("lookup-corp");
        Assert.NotNull(fetched);
        Assert.Equal(tenant.Id, fetched.Id);
    }

    [Fact]
    public async Task Tenant_SurvivesStoreReinstantiation()
    {
        var store1 = _fixture.CreateControlPlaneStore();
        var tenant = MakeTenant("restart-tenant", "Restart Tenant");
        await store1.UpsertTenantAsync(tenant);

        var store2 = _fixture.CreateControlPlaneStore();
        var fetched = await store2.GetTenantAsync(tenant.Id);

        Assert.NotNull(fetched);
        Assert.Equal("restart-tenant", fetched.Name);
    }

    [Fact]
    public async Task TwoInstances_SeeSameTenantState()
    {
        var instanceA = _fixture.CreateControlPlaneStore();
        var instanceB = _fixture.CreateControlPlaneStore();

        var tenant = MakeTenant("shared-tenant", "Shared Tenant");
        await instanceA.UpsertTenantAsync(tenant);

        var fetched = await instanceB.GetTenantAsync(tenant.Id);
        Assert.NotNull(fetched);
        Assert.Equal("shared-tenant", fetched.Name);

        // Instance B suspends the tenant
        var suspended = tenant with { Status = TenantStatus.Suspended, SuspendedAtUtc = DateTimeOffset.UtcNow };
        await instanceB.UpsertTenantAsync(suspended);

        // Instance A sees the suspension
        var reFetched = await instanceA.GetTenantAsync(tenant.Id);
        Assert.NotNull(reFetched);
        Assert.Equal(TenantStatus.Suspended, reFetched.Status);
        Assert.NotNull(reFetched.SuspendedAtUtc);
    }

    [Fact]
    public async Task ListTenants_FiltersByStatus()
    {
        var store = _fixture.CreateControlPlaneStore();
        await store.UpsertTenantAsync(MakeTenant("active-t1", "Active 1"));
        await store.UpsertTenantAsync(MakeTenant("active-t2", "Active 2"));
        await store.UpsertTenantAsync(MakeTenant("suspended-t1", "Suspended 1") with { Status = TenantStatus.Suspended });

        var active = await store.ListTenantsAsync(TenantStatus.Active, 0, 100);
        Assert.Equal(2, active.Count);

        var all = await store.ListTenantsAsync(null, 0, 100);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task RemoveTenant_DeletesTenant()
    {
        var store = _fixture.CreateControlPlaneStore();
        var tenant = MakeTenant("removable-tenant", "Removable");
        await store.UpsertTenantAsync(tenant);

        var removed = await store.RemoveTenantAsync(tenant.Id);
        Assert.True(removed);

        var fetched = await store.GetTenantAsync(tenant.Id);
        Assert.Null(fetched);
    }

    // ── Workflow Tests ──────────────────────────────────────────────

    [Fact]
    public async Task UpsertWorkflow_PersistsAndRetrieves()
    {
        var store = _fixture.CreateControlPlaneStore();
        var workflow = MakeWorkflow("tenant-1", "onboarding-flow");

        await store.UpsertWorkflowAsync(workflow);

        var fetched = await store.GetWorkflowAsync(workflow.Id);
        Assert.NotNull(fetched);
        Assert.Equal("onboarding-flow", fetched.Name);
        Assert.Equal("tenant-1", fetched.TenantId);
        Assert.Equal(ManagedWorkflowStatus.Active, fetched.Status);
    }

    [Fact]
    public async Task Workflow_SurvivesStoreReinstantiation()
    {
        var store1 = _fixture.CreateControlPlaneStore();
        var workflow = MakeWorkflow("tenant-restart", "restart-flow");
        await store1.UpsertWorkflowAsync(workflow);

        var store2 = _fixture.CreateControlPlaneStore();
        var fetched = await store2.GetWorkflowAsync(workflow.Id);

        Assert.NotNull(fetched);
        Assert.Equal("restart-flow", fetched.Name);
    }

    // ── Policy Tests ────────────────────────────────────────────────

    [Fact]
    public async Task UpsertPolicy_PersistsAndRetrieves()
    {
        var store = _fixture.CreateControlPlaneStore();
        var policy = MakePolicy("tenant-1", "rate-limit-api");

        await store.UpsertPolicyAsync(policy);

        var fetched = await store.GetPolicyAsync(policy.Id);
        Assert.NotNull(fetched);
        Assert.Equal("rate-limit-api", fetched.Name);
        Assert.Equal(PlatformPolicyType.RateLimit, fetched.PolicyType);
        Assert.True(fetched.IsEnabled);
    }

    [Fact]
    public async Task ListPolicies_FiltersCorrectly()
    {
        var store = _fixture.CreateControlPlaneStore();
        await store.UpsertPolicyAsync(MakePolicy("t1", "p1"));
        await store.UpsertPolicyAsync(MakePolicy("t1", "p2") with { IsEnabled = false });
        await store.UpsertPolicyAsync(MakePolicy("t2", "p3"));

        var t1Policies = await store.ListPoliciesAsync("t1", null, null, 0, 100);
        Assert.Equal(2, t1Policies.Count);

        var enabledOnly = await store.ListPoliciesAsync(null, null, true, 0, 100);
        Assert.Equal(2, enabledOnly.Count);
    }

    // ── Configuration Tests ─────────────────────────────────────────

    [Fact]
    public async Task UpsertConfiguration_PersistsAndRetrieves()
    {
        var store = _fixture.CreateControlPlaneStore();
        var config = MakeConfiguration("tenant-1", "ai", "default_model", "gpt-4.1");

        await store.UpsertConfigurationAsync(config);

        var fetched = await store.GetConfigurationAsync("tenant-1", "ai", "default_model");
        Assert.NotNull(fetched);
        Assert.Equal("gpt-4.1", fetched.Value);
        Assert.False(fetched.IsSecret);
    }

    [Fact]
    public async Task Configuration_UpsertOnConflictUpdatesValue()
    {
        var store = _fixture.CreateControlPlaneStore();
        var config = MakeConfiguration("tenant-1", "ai", "model", "gpt-4.1-mini");
        await store.UpsertConfigurationAsync(config);

        var updated = config with { Value = "gpt-4.1", UpdatedAtUtc = DateTimeOffset.UtcNow };
        await store.UpsertConfigurationAsync(updated);

        var fetched = await store.GetConfigurationAsync("tenant-1", "ai", "model");
        Assert.NotNull(fetched);
        Assert.Equal("gpt-4.1", fetched.Value);
    }

    [Fact]
    public async Task RemoveConfiguration_Works()
    {
        var store = _fixture.CreateControlPlaneStore();
        await store.UpsertConfigurationAsync(MakeConfiguration("t1", "scope", "key", "val"));

        var removed = await store.RemoveConfigurationAsync("t1", "scope", "key");
        Assert.True(removed);

        var fetched = await store.GetConfigurationAsync("t1", "scope", "key");
        Assert.Null(fetched);
    }

    // ── Count Tests ─────────────────────────────────────────────────

    [Fact]
    public async Task Counts_ReturnCorrectValues()
    {
        var store = _fixture.CreateControlPlaneStore();

        await store.UpsertTenantAsync(MakeTenant("count-t1", "Count T1"));
        await store.UpsertTenantAsync(MakeTenant("count-t2", "Count T2"));
        await store.UpsertWorkflowAsync(MakeWorkflow("count-t1", "wf1"));
        await store.UpsertPolicyAsync(MakePolicy("count-t1", "pol1"));
        await store.UpsertConfigurationAsync(MakeConfiguration("count-t1", "s", "k", "v"));

        Assert.Equal(2, await store.CountTenantsAsync());
        Assert.Equal(1, await store.CountWorkflowsAsync());
        Assert.Equal(1, await store.CountWorkflowsAsync("count-t1"));
        Assert.Equal(0, await store.CountWorkflowsAsync("nonexistent"));
        Assert.Equal(1, await store.CountPoliciesAsync());
        Assert.Equal(1, await store.CountConfigurationsAsync());
    }

    // ── Multi-instance correctness (cross-cutting) ──────────────────

    [Fact]
    public async Task MultiInstance_FullLifecycleAcrossInstances()
    {
        var instanceA = _fixture.CreateControlPlaneStore();
        var instanceB = _fixture.CreateControlPlaneStore();

        // Instance A creates tenant and workflow
        var tenant = MakeTenant("multi-tenant", "Multi Tenant");
        await instanceA.UpsertTenantAsync(tenant);
        var workflow = MakeWorkflow(tenant.Id.ToString(), "multi-workflow");
        await instanceA.UpsertWorkflowAsync(workflow);

        // Instance B sees both and adds a policy
        var tenantFromB = await instanceB.GetTenantAsync(tenant.Id);
        Assert.NotNull(tenantFromB);
        var workflowFromB = await instanceB.GetWorkflowAsync(workflow.Id);
        Assert.NotNull(workflowFromB);

        var policy = MakePolicy(tenant.Id.ToString(), "multi-policy");
        await instanceB.UpsertPolicyAsync(policy);

        // Instance A sees the policy
        var policyFromA = await instanceA.GetPolicyAsync(policy.Id);
        Assert.NotNull(policyFromA);
        Assert.Equal("multi-policy", policyFromA.Name);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static Tenant MakeTenant(string name, string displayName) =>
        new(
            Id: Guid.NewGuid(),
            Name: name,
            DisplayName: displayName,
            Status: TenantStatus.Active,
            Tier: TenantTier.Professional,
            ResourceQuota: new TenantResourceQuota(10, 20, 5, 1_073_741_824, 100),
            Metadata: new Dictionary<string, string> { ["region"] = "us-east-1" },
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ActivatedAtUtc: DateTimeOffset.UtcNow,
            SuspendedAtUtc: null);

    private static ManagedWorkflow MakeWorkflow(string tenantId, string name) =>
        new(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            Name: name,
            Description: $"Test workflow: {name}",
            Status: ManagedWorkflowStatus.Active,
            Strategy: "sequential",
            StepCount: 3,
            Metadata: new Dictionary<string, string> { ["env"] = "test" },
            CreatedAtUtc: DateTimeOffset.UtcNow,
            LastExecutedAtUtc: null,
            ExecutionCount: 0,
            FailureCount: 0);

    private static PlatformPolicy MakePolicy(string tenantId, string name) =>
        new(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            Name: name,
            Description: $"Test policy: {name}",
            PolicyType: PlatformPolicyType.RateLimit,
            TargetResource: "api/*",
            Rules: new Dictionary<string, string> { ["maxRequests"] = "1000" },
            IsEnabled: true,
            Priority: 10,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: null);

    private static PlatformConfiguration MakeConfiguration(
        string tenantId, string scope, string key, string value) =>
        new(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            Scope: scope,
            Key: key,
            Value: value,
            Description: null,
            IsSecret: false,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: null);
}
