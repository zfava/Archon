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

        // Different user approves
        var reviewed = await _gov.ReviewApprovalAsync(gate.Id, "user-b", true, "Approved");
        Assert.Equal(ApprovalStatus.Approved, reviewed.Status);
        Assert.Equal("user-b", reviewed.ReviewedBy);
    }

    [Fact]
    public async Task RequestAndDeny_SetsCorrectStatus()
    {
        var gate = await _gov.RequestApprovalAsync(
            "policy.delete", "pol-1", "tenant-1", "user-a", "Cleanup");
        var reviewed = await _gov.ReviewApprovalAsync(gate.Id, "user-b", false, "Not now");

        Assert.Equal(ApprovalStatus.Denied, reviewed.Status);
    }

    [Fact]
    public async Task SeparationOfDuties_PreventsSelfreview()
    {
        // workflow.cancel has RequireSeparationOfDuties = true
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-x", "Reason");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _gov.ReviewApprovalAsync(gate.Id, "user-x", true, "Self-approve"));
        Assert.Contains("Separation of duties", ex.Message);
    }

    [Fact]
    public async Task ReviewAlreadyReviewed_Throws()
    {
        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "R");
        await _gov.ReviewApprovalAsync(gate.Id, "user-b", true, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _gov.ReviewApprovalAsync(gate.Id, "user-c", false, null));
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
        await _gov.ReviewApprovalAsync(gate.Id, "user-b", true, "ok");

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
}
