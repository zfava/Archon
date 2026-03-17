using ArchonAI.Api.Security;
using ArchonAI.Core.Models.Governance;

namespace ArchonAI.Tests;

public class GovernanceServiceTests
{
    private readonly GovernanceService _gov = new();

    [Fact]
    public async Task DefaultApprovalPolicies_AreSeeded()
    {
        var policies = await _gov.ListApprovalPoliciesAsync();
        Assert.NotEmpty(policies);
        Assert.Contains(policies, p => p.ActionType == "workflow.cancel");
        Assert.Contains(policies, p => p.ActionType == "policy.delete");
        Assert.Contains(policies, p => p.ActionType == "strategy.override");
    }

    [Fact]
    public async Task RequiresApproval_ReturnsTrueForConfiguredAction()
    {
        Assert.True(await _gov.RequiresApprovalAsync("workflow.cancel"));
    }

    [Fact]
    public async Task RequiresApproval_ReturnsFalseForUnknownAction()
    {
        Assert.False(await _gov.RequiresApprovalAsync("something.random"));
    }

    [Fact]
    public async Task RequestAndApprove_HappyPath()
    {
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-123", "tenant-1", "user-a", "Needs to stop");

        Assert.Equal(ApprovalStatus.Pending, gate.Status);
        Assert.Equal("user-a", gate.RequestedBy);

        // Different user approves (Admin role satisfies RequiredApproverRole)
        var reviewed = await _gov.ReviewApprovalAsync(
            gate.Id, "tenant-1", "user-b", "Admin", true, "Approved");
        Assert.Equal(ApprovalStatus.Approved, reviewed.Status);
        Assert.Equal("user-b", reviewed.ReviewedBy);
    }

    [Fact]
    public async Task RequestAndDeny_SetsCorrectStatus()
    {
        var gate = await _gov.RequestApprovalAsync(
            "policy.delete", "pol-1", "tenant-1", "user-a", "Cleanup");
        var reviewed = await _gov.ReviewApprovalAsync(
            gate.Id, "tenant-1", "user-b", "Admin", false, "Not now");

        Assert.Equal(ApprovalStatus.Denied, reviewed.Status);
    }

    [Fact]
    public async Task SeparationOfDuties_PreventsSelfreview()
    {
        // workflow.cancel has RequireSeparationOfDuties = true
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-x", "Reason");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _gov.ReviewApprovalAsync(gate.Id, "t1", "user-x", "Admin", true, "Self-approve"));
        Assert.Contains("Separation of duties", ex.Message);
    }

    [Fact]
    public async Task ReviewAlreadyReviewed_Throws()
    {
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "R");
        await _gov.ReviewApprovalAsync(gate.Id, "t1", "user-b", "Admin", true, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _gov.ReviewApprovalAsync(gate.Id, "t1", "user-c", "Admin", false, null));
    }

    [Fact]
    public async Task ListPendingApprovals_FiltersByTenant()
    {
        await _gov.RequestApprovalAsync("workflow.cancel", "wf1", "tenant-A", "u1", "r");
        await _gov.RequestApprovalAsync("workflow.cancel", "wf2", "tenant-B", "u2", "r");

        var tenantA = await _gov.ListPendingApprovalsAsync("tenant-A");
        var tenantB = await _gov.ListPendingApprovalsAsync("tenant-B");

        Assert.Single(tenantA);
        Assert.Single(tenantB);
        Assert.Equal("tenant-A", tenantA[0].TenantId);
        Assert.Equal("tenant-B", tenantB[0].TenantId);
    }

    [Fact]
    public async Task ApprovalHistory_RecordsAuditEntries()
    {
        var gate = await _gov.RequestApprovalAsync(
            "policy.delete", "p1", "t1", "user-a", "cleanup");
        await _gov.ReviewApprovalAsync(gate.Id, "t1", "user-b", "Admin", true, "ok");

        var history = await _gov.GetApprovalHistoryAsync("t1", "policy.delete", 10);
        Assert.Single(history);
        Assert.Equal(gate.Id, history[0].ApprovalGateId);
        Assert.Equal(ApprovalStatus.Approved, history[0].Outcome);
        Assert.Equal("user-a", history[0].RequestedBy);
        Assert.Equal("user-b", history[0].ReviewedBy);
    }

    [Fact]
    public async Task CreateApprovalPolicy_IsQueryable()
    {
        await _gov.CreateApprovalPolicyAsync(
            "custom.action", "Custom test", "Admin", false);

        Assert.True(await _gov.RequiresApprovalAsync("custom.action"));
        var policies = await _gov.ListApprovalPoliciesAsync();
        Assert.Contains(policies, p => p.ActionType == "custom.action");
    }

    // ── Phase 4 Remediation Tests ────────────────────────────────────

    [Fact]
    public async Task CrossTenant_GetApproval_ReturnsNull()
    {
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "tenant-A", "user-a", "reason");

        // Different tenant should not see the gate
        var result = await _gov.GetApprovalAsync(gate.Id, "tenant-B");
        Assert.Null(result);

        // Same tenant can see it
        var same = await _gov.GetApprovalAsync(gate.Id, "tenant-A");
        Assert.NotNull(same);
    }

    [Fact]
    public async Task CrossTenant_ReviewApproval_Throws()
    {
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "tenant-A", "user-a", "reason");

        // Reviewer from different tenant should be rejected
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _gov.ReviewApprovalAsync(gate.Id, "tenant-B", "user-b", "Admin", true, "cross-tenant"));
    }

    [Fact]
    public async Task InsufficientRole_ReviewApproval_Throws()
    {
        // workflow.cancel requires Admin approver
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "reason");

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _gov.ReviewApprovalAsync(gate.Id, "t1", "user-b", "Viewer", true, "not admin"));
        Assert.Contains("Viewer", ex.Message);
        Assert.Contains("Admin", ex.Message);
    }

    [Fact]
    public async Task OperatorRole_CannotApprove_AdminOnlyAction()
    {
        var gate = await _gov.RequestApprovalAsync(
            "connector.disconnect", "conn-1", "t1", "user-a", "need to disconnect");

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _gov.ReviewApprovalAsync(gate.Id, "t1", "user-b", "Operator", true, "trying"));
        Assert.Contains("Operator", ex.Message);
    }

    // ── History Tenant Isolation Tests ────────────────────────────────

    [Fact]
    public async Task History_SameTenant_ReturnsOwnEntries()
    {
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "tenant-X", "user-a", "reason");
        await _gov.ReviewApprovalAsync(gate.Id, "tenant-X", "user-b", "Admin", true, "ok");

        var history = await _gov.GetApprovalHistoryAsync("tenant-X", null, 50);
        Assert.Single(history);
        Assert.Equal("tenant-X", history[0].TenantId);
    }

    [Fact]
    public async Task History_CrossTenant_ReturnsEmpty()
    {
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "tenant-X", "user-a", "reason");
        await _gov.ReviewApprovalAsync(gate.Id, "tenant-X", "user-b", "Admin", true, "ok");

        // A different tenant should see zero entries
        var history = await _gov.GetApprovalHistoryAsync("tenant-Y", null, 50);
        Assert.Empty(history);
    }

    [Fact]
    public async Task History_NullTenant_ReturnsAllEntries()
    {
        // Service-layer null tenant returns all — endpoint must NEVER pass null
        var g1 = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t-A", "u1", "r");
        await _gov.ReviewApprovalAsync(g1.Id, "t-A", "u2", "Admin", true, null);

        var g2 = await _gov.RequestApprovalAsync(
            "policy.delete", "p-1", "t-B", "u3", "r");
        await _gov.ReviewApprovalAsync(g2.Id, "t-B", "u4", "Admin", true, null);

        // Null tenantId at the service layer returns everything (unfiltered)
        var all = await _gov.GetApprovalHistoryAsync(null, null, 50);
        Assert.True(all.Count >= 2);

        // But scoped queries only return their own
        var tA = await _gov.GetApprovalHistoryAsync("t-A", null, 50);
        Assert.All(tA, e => Assert.Equal("t-A", e.TenantId));

        var tB = await _gov.GetApprovalHistoryAsync("t-B", null, 50);
        Assert.All(tB, e => Assert.Equal("t-B", e.TenantId));
    }

    // ── Approval Completion Loop Tests ───────────────────────────────

    [Fact]
    public async Task ApprovedGate_StoresActionPayload()
    {
        var payload = """{"workflowId":"abc-123"}""";
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "reason", payload);

        Assert.Equal(payload, gate.ActionPayload);
        Assert.Equal(GateExecutionStatus.NotExecuted, gate.ExecutionStatus);
    }

    [Fact]
    public async Task RecordExecutionResult_Succeeded()
    {
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "reason", """{"workflowId":"wf-1"}""");
        await _gov.ReviewApprovalAsync(gate.Id, "t1", "user-b", "Admin", true, "ok");

        var updated = await _gov.RecordExecutionResultAsync(gate.Id, GateExecutionStatus.Succeeded, null);

        Assert.Equal(GateExecutionStatus.Succeeded, updated.ExecutionStatus);
        Assert.Null(updated.ExecutionError);
        Assert.NotNull(updated.ExecutedAtUtc);
    }

    [Fact]
    public async Task RecordExecutionResult_Failed_StoresError()
    {
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "reason", """{"workflowId":"wf-1"}""");
        await _gov.ReviewApprovalAsync(gate.Id, "t1", "user-b", "Admin", true, "ok");

        var updated = await _gov.RecordExecutionResultAsync(
            gate.Id, GateExecutionStatus.Failed, "Workflow not found");

        Assert.Equal(GateExecutionStatus.Failed, updated.ExecutionStatus);
        Assert.Equal("Workflow not found", updated.ExecutionError);
        Assert.NotNull(updated.ExecutedAtUtc);
    }

    [Fact]
    public async Task DeniedGate_HasNoExecutionPayloadTriggered()
    {
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "reason", """{"workflowId":"wf-1"}""");
        var denied = await _gov.ReviewApprovalAsync(gate.Id, "t1", "user-b", "Admin", false, "nope");

        Assert.Equal(ApprovalStatus.Denied, denied.Status);
        Assert.Equal(GateExecutionStatus.NotExecuted, denied.ExecutionStatus);
        Assert.Null(denied.ExecutedAtUtc);
    }

    // ── Deduplication Tests ──────────────────────────────────────────

    [Fact]
    public async Task DuplicateRequest_ReturnsSamePendingGate()
    {
        var gate1 = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "first request");
        var gate2 = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-b", "second request");

        // Same gate ID returned — no duplicate created
        Assert.Equal(gate1.Id, gate2.Id);
    }

    [Fact]
    public async Task DuplicateRequest_DifferentResource_CreatesSeparateGates()
    {
        var gate1 = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "reason");
        var gate2 = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-2", "t1", "user-a", "reason");

        Assert.NotEqual(gate1.Id, gate2.Id);
    }

    [Fact]
    public async Task DuplicateRequest_DifferentTenant_CreatesSeparateGates()
    {
        var gate1 = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t-A", "user-a", "reason");
        var gate2 = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t-B", "user-a", "reason");

        Assert.NotEqual(gate1.Id, gate2.Id);
    }

    [Fact]
    public async Task DuplicateRequest_AfterApproval_CreatesNewGate()
    {
        var gate1 = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "first");
        await _gov.ReviewApprovalAsync(gate1.Id, "t1", "user-b", "Admin", true, "ok");

        // After approval, a new request for the same action should create a new gate
        var gate2 = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "second");

        Assert.NotEqual(gate1.Id, gate2.Id);
        Assert.Equal(ApprovalStatus.Pending, gate2.Status);
    }

    [Fact]
    public async Task DuplicateRequest_AfterDenial_CreatesNewGate()
    {
        var gate1 = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "first");
        await _gov.ReviewApprovalAsync(gate1.Id, "t1", "user-b", "Admin", false, "no");

        var gate2 = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "retry");

        Assert.NotEqual(gate1.Id, gate2.Id);
    }
}
