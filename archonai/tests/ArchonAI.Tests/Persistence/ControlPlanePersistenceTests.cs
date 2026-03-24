using ArchonAI.ControlPlane;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ControlPlane;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Tests.Persistence;

public sealed class ControlPlanePersistenceTests : IDisposable
{
    private readonly string _tempDir;

    public ControlPlanePersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"archon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private DurableControlPlaneRepository CreateStore() =>
        new(Options.Create(new ControlPlaneOptions { PersistencePath = Path.Combine(_tempDir, "cp.json") }),
            NullLogger<DurableControlPlaneRepository>.Instance);

    [Fact]
    public async global::System.Threading.Tasks.Task Tenants_SurviveRestart()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant(tenantId, "acme", "Acme Corp", TenantStatus.Active, TenantTier.Enterprise,
            new TenantResourceQuota(10, 50, 5, 1_073_741_824, 1000),
            new Dictionary<string, string> { ["region"] = "us-east" },
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);

        // Write with first instance
        using (var store1 = CreateStore())
        {
            await store1.UpsertTenantAsync(tenant);
            await store1.FlushAsync();
        }

        // Read with second instance (simulates restart)
        using var store2 = CreateStore();
        var loaded = await store2.GetTenantAsync(tenantId);

        Assert.NotNull(loaded);
        Assert.Equal("acme", loaded!.Name);
        Assert.Equal(TenantStatus.Active, loaded.Status);
        Assert.Equal(TenantTier.Enterprise, loaded.Tier);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Policies_SurviveRestart()
    {
        var policyId = Guid.NewGuid();
        var policy = new PlatformPolicy(policyId, "tenant-1", "rate-limit-api", "Rate limit API calls",
            PlatformPolicyType.RateLimit, "api/*",
            new Dictionary<string, string> { ["maxRequestsPerMinute"] = "100" },
            true, 10, DateTimeOffset.UtcNow, null);

        using (var store1 = CreateStore())
        {
            await store1.UpsertPolicyAsync(policy);
            await store1.FlushAsync();
        }

        using var store2 = CreateStore();
        var loaded = await store2.GetPolicyAsync(policyId);

        Assert.NotNull(loaded);
        Assert.Equal("rate-limit-api", loaded!.Name);
        Assert.Equal(PlatformPolicyType.RateLimit, loaded.PolicyType);
        Assert.True(loaded.IsEnabled);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Configurations_SurviveRestart()
    {
        var config = new PlatformConfiguration(Guid.NewGuid(), "tenant-1", "runtime", "maxRetries",
            "3", "Max retries for failed tasks", false, DateTimeOffset.UtcNow, null);

        using (var store1 = CreateStore())
        {
            await store1.UpsertConfigurationAsync(config);
            await store1.FlushAsync();
        }

        using var store2 = CreateStore();
        var loaded = await store2.GetConfigurationAsync("tenant-1", "runtime", "maxRetries");

        Assert.NotNull(loaded);
        Assert.Equal("3", loaded!.Value);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ManagedAgents_SurviveRestart()
    {
        var agentId = Guid.NewGuid();
        var agent = new ManagedAgent(agentId, "tenant-1", "SalesBot", "1.0.0",
            ManagedAgentStatus.Active, ["crm", "email"],
            new Dictionary<string, string> { ["model"] = "gpt-4" },
            DateTimeOffset.UtcNow, null, 42, 2);

        using (var store1 = CreateStore())
        {
            await store1.UpsertAgentAsync(agent);
            await store1.FlushAsync();
        }

        using var store2 = CreateStore();
        var loaded = await store2.GetAgentAsync(agentId);

        Assert.NotNull(loaded);
        Assert.Equal("SalesBot", loaded!.Name);
        Assert.Equal(ManagedAgentStatus.Active, loaded.Status);
        Assert.Equal(42, loaded.ExecutionCount);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task EmptyStore_StartsClean()
    {
        using var store = CreateStore();
        Assert.Equal(0, await store.CountTenantsAsync());
        Assert.Equal(0, await store.CountWorkflowsAsync());
        Assert.Equal(0, await store.CountAgentsAsync());
        Assert.Equal(0, await store.CountPoliciesAsync());
        Assert.Equal(0, await store.CountConfigurationsAsync());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Remove_PersistsAcrossRestart()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant(tenantId, "del-test", "Delete Test", TenantStatus.Active, TenantTier.Free,
            new TenantResourceQuota(1, 1, 1, 1024, 10),
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow, null, null);

        using (var store1 = CreateStore())
        {
            await store1.UpsertTenantAsync(tenant);
            await store1.FlushAsync();
        }

        using (var store2 = CreateStore())
        {
            await store2.RemoveTenantAsync(tenantId);
            await store2.FlushAsync();
        }

        using var store3 = CreateStore();
        Assert.Null(await store3.GetTenantAsync(tenantId));
    }
}
