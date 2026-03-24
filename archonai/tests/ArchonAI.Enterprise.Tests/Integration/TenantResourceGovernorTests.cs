using ArchonAI.MultiTenant;
using Microsoft.Extensions.Options;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Integration tests for tenant resource quotas: planning slot limits,
/// task count limits, and cross-tenant isolation of resource consumption.
/// </summary>
public sealed class TenantResourceGovernorTests
{
    private TenantResourceGovernor CreateGovernor(int maxPlans = 10, int maxTasks = 500) =>
        new(Options.Create(new MultiTenantOptions
        {
            MaxConcurrentPlansPerTenant = maxPlans,
            MaxTasksPerPlanPerTenant = maxTasks,
        }));

    // ── Planning Slot Acquisition ─────────────────────────────────────

    [Fact]
    public async Task AcquireSlot_WithinLimit_Succeeds()
    {
        var gov = CreateGovernor(maxPlans: 3);
        Assert.True(await gov.TryAcquirePlanningSlotAsync("tenant-1", CancellationToken.None));
        Assert.True(await gov.TryAcquirePlanningSlotAsync("tenant-1", CancellationToken.None));
        Assert.True(await gov.TryAcquirePlanningSlotAsync("tenant-1", CancellationToken.None));
    }

    [Fact]
    public async Task AcquireSlot_AtLimit_Fails()
    {
        var gov = CreateGovernor(maxPlans: 2);
        Assert.True(await gov.TryAcquirePlanningSlotAsync("tenant-1", CancellationToken.None));
        Assert.True(await gov.TryAcquirePlanningSlotAsync("tenant-1", CancellationToken.None));
        Assert.False(await gov.TryAcquirePlanningSlotAsync("tenant-1", CancellationToken.None));
    }

    [Fact]
    public async Task ReleaseSlot_FreesCapacity()
    {
        var gov = CreateGovernor(maxPlans: 1);
        Assert.True(await gov.TryAcquirePlanningSlotAsync("tenant-1", CancellationToken.None));
        Assert.False(await gov.TryAcquirePlanningSlotAsync("tenant-1", CancellationToken.None));

        gov.ReleasePlanningSlot("tenant-1");

        Assert.True(await gov.TryAcquirePlanningSlotAsync("tenant-1", CancellationToken.None));
    }

    // ── Cross-Tenant Isolation ────────────────────────────────────────

    [Fact]
    public async Task DifferentTenants_HaveIndependentSlots()
    {
        var gov = CreateGovernor(maxPlans: 1);

        Assert.True(await gov.TryAcquirePlanningSlotAsync("tenant-A", CancellationToken.None));
        Assert.True(await gov.TryAcquirePlanningSlotAsync("tenant-B", CancellationToken.None));

        // Each tenant is at their limit independently
        Assert.False(await gov.TryAcquirePlanningSlotAsync("tenant-A", CancellationToken.None));
        Assert.False(await gov.TryAcquirePlanningSlotAsync("tenant-B", CancellationToken.None));
    }

    // ── Task Count Validation ─────────────────────────────────────────

    [Fact]
    public void TaskCount_WithinLimit_Allowed()
    {
        var gov = CreateGovernor(maxTasks: 100);
        Assert.True(gov.CanCreateTaskCount("tenant-1", 50));
        Assert.True(gov.CanCreateTaskCount("tenant-1", 100));
    }

    [Fact]
    public void TaskCount_ExceedsLimit_Rejected()
    {
        var gov = CreateGovernor(maxTasks: 100);
        Assert.False(gov.CanCreateTaskCount("tenant-1", 101));
        Assert.False(gov.CanCreateTaskCount("tenant-1", 1000));
    }

    [Fact]
    public void TaskCount_ZeroOrNegative_Allowed()
    {
        var gov = CreateGovernor(maxTasks: 100);
        Assert.True(gov.CanCreateTaskCount("tenant-1", 0));
    }
}
