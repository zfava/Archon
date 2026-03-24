using ArchonAI.Api.Security;
using ArchonAI.Core.Models.Governance;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Tests for governance approval gate persistence, lifecycle, and cross-tenant isolation.
/// </summary>
public sealed class GovernancePersistenceTests
{
    // ── Test 1: Approval gate round-trip persistence ─────────────────────

    [Fact]
    public async Task ApprovalGate_FullLifecycle_PersistsCorrectly()
    {
        var gov = new GovernanceService();
        var tenantId = "tenant-persist";

        // 1. Create gate
        var gate = await gov.RequestApprovalAsync(
            "policy.delete", "policy-42", tenantId, "user-requester", "Need to remove stale policy");

        Assert.Equal(ApprovalStatus.Pending, gate.Status);
        Assert.Equal("policy.delete", gate.ActionType);
        Assert.Equal("policy-42", gate.ResourceId);
        Assert.Equal(tenantId, gate.TenantId);
        Assert.Equal("user-requester", gate.RequestedBy);
        Assert.Null(gate.ReviewedBy);
        Assert.Null(gate.ReviewedAtUtc);

        // 2. Read it back
        var fetched = await gov.GetApprovalAsync(gate.Id, tenantId);
        Assert.NotNull(fetched);
        Assert.Equal(gate.Id, fetched.Id);
        Assert.Equal(ApprovalStatus.Pending, fetched.Status);

        // 3. Approve it
        var approved = await gov.ReviewApprovalAsync(
            gate.Id, tenantId, "user-reviewer", "Admin", true, "Looks good");

        Assert.Equal(ApprovalStatus.Approved, approved.Status);
        Assert.Equal("user-reviewer", approved.ReviewedBy);
        Assert.Equal("Looks good", approved.ReviewNotes);
        Assert.NotNull(approved.ReviewedAtUtc);

        // 4. Verify it's no longer pending
        var pending = await gov.ListPendingApprovalsAsync(tenantId);
        Assert.DoesNotContain(pending, g => g.Id == gate.Id);

        // 5. Verify audit history recorded it
        var history = await gov.GetApprovalHistoryAsync(tenantId, "policy.delete", 50);
        Assert.Single(history);
        Assert.Equal(gate.Id, history[0].ApprovalGateId);
        Assert.Equal(ApprovalStatus.Approved, history[0].Outcome);

        // 6. Record execution result
        var executed = await gov.RecordExecutionResultAsync(gate.Id, GateExecutionStatus.Succeeded, null);
        Assert.Equal(GateExecutionStatus.Succeeded, executed.ExecutionStatus);
        Assert.NotNull(executed.ExecutedAtUtc);
    }

    // ── Test 2: Cross-tenant isolation ───────────────────────────────────

    [Fact]
    public async Task ApprovalGate_CrossTenantIsolation_PreventsCrossAccess()
    {
        var gov = new GovernanceService();

        // Create gates for two tenants
        var gateA = await gov.RequestApprovalAsync(
            "workflow.cancel", "wf-alpha", "tenant-alpha", "user-a", "Cancel stuck workflow");
        var gateB = await gov.RequestApprovalAsync(
            "workflow.cancel", "wf-beta", "tenant-beta", "user-b", "Cancel slow workflow");

        // Verify each tenant can only see their own gate
        Assert.NotNull(await gov.GetApprovalAsync(gateA.Id, "tenant-alpha"));
        Assert.Null(await gov.GetApprovalAsync(gateA.Id, "tenant-beta"));
        Assert.NotNull(await gov.GetApprovalAsync(gateB.Id, "tenant-beta"));
        Assert.Null(await gov.GetApprovalAsync(gateB.Id, "tenant-alpha"));

        // Verify pending lists are isolated
        var pendingA = await gov.ListPendingApprovalsAsync("tenant-alpha");
        var pendingB = await gov.ListPendingApprovalsAsync("tenant-beta");
        Assert.Single(pendingA);
        Assert.Single(pendingB);
        Assert.Equal(gateA.Id, pendingA[0].Id);
        Assert.Equal(gateB.Id, pendingB[0].Id);

        // Verify cross-tenant review is rejected
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            gov.ReviewApprovalAsync(gateA.Id, "tenant-beta", "attacker", "Admin", true, "pwned"));

        // Verify the gate was NOT modified by the failed cross-tenant attempt
        var gateAStill = await gov.GetApprovalAsync(gateA.Id, "tenant-alpha");
        Assert.NotNull(gateAStill);
        Assert.Equal(ApprovalStatus.Pending, gateAStill.Status);

        // Approve via correct tenant, verify history isolation
        await gov.ReviewApprovalAsync(gateA.Id, "tenant-alpha", "admin-a", "Admin", true, "ok");

        var historyAlpha = await gov.GetApprovalHistoryAsync("tenant-alpha", null, 50);
        var historyBeta = await gov.GetApprovalHistoryAsync("tenant-beta", null, 50);
        Assert.Single(historyAlpha);
        Assert.Empty(historyBeta);
    }
}
