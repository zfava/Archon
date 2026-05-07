using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.Rbac;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Contract;

/// <summary>
/// API contract tests verifying that service layer contracts remain stable:
/// return type shapes, required fields, and behavioral guarantees
/// that API consumers depend on.
/// </summary>
public sealed class ApiContractTests
{
    // ── RBAC Contract ─────────────────────────────────────────────────

    [Fact]
    public async Task RbacRoles_ContractShape()
    {
        var svc = new RbacService(Substitute.For<IEventBus>());
        var roles = await svc.GetRolesAsync();

        foreach (var role in roles)
        {
            Assert.NotEqual(Guid.Empty, role.Id);
            Assert.False(string.IsNullOrWhiteSpace(role.Name));
            Assert.False(string.IsNullOrWhiteSpace(role.Description));
            Assert.NotNull(role.Permissions);
            Assert.NotEmpty(role.Permissions);
        }
    }

    [Fact]
    public async Task RbacAccessDecision_ContractShape()
    {
        var svc = new RbacService(Substitute.For<IEventBus>());
        var roles = await svc.GetRolesAsync();
        var admin = roles.First(r => r.Name == "Admin");
        await svc.AssignRoleAsync("u1", "user", admin.Id, "system");

        var decision = await svc.EvaluateAccessAsync("u1", "resource", "agents:read");

        Assert.NotNull(decision.SubjectId);
        Assert.NotNull(decision.Resource);
        Assert.NotNull(decision.Action);
        Assert.NotNull(decision.Reason);
        Assert.NotNull(decision.MatchedPolicies);
        Assert.True(decision.EvaluatedAtUtc > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task RbacStatus_ContractShape()
    {
        var svc = new RbacService(Substitute.For<IEventBus>());
        var status = svc.GetStatus();

        Assert.True(status.IsActive);
        Assert.True(status.TotalRoles >= 3); // Admin, Operator, Viewer
        Assert.True(status.StatusAsOfUtc > DateTimeOffset.MinValue);
    }

    // ── Approval Gate Contract ────────────────────────────────────────

    [Fact]
    public async Task ApprovalGate_ContractShape()
    {
        var gov = new GovernanceService();
        var gate = await gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "reason", """{"key":"value"}""");

        Assert.NotEqual(Guid.Empty, gate.Id);
        Assert.Equal("workflow.cancel", gate.ActionType);
        Assert.Equal("wf-1", gate.ResourceId);
        Assert.Equal("t1", gate.TenantId);
        Assert.Equal("user-a", gate.RequestedBy);
        Assert.Equal(ApprovalStatus.Pending, gate.Status);
        Assert.Equal("""{"key":"value"}""", gate.ActionPayload);
        Assert.Null(gate.ReviewedBy);
        Assert.Null(gate.ReviewedAtUtc);
        Assert.True(gate.RequestedAtUtc > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task ApprovedGate_ContractShape()
    {
        var gov = new GovernanceService();
        var gate = await gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "reason");
        var approved = await gov.ReviewApprovalAsync(
            gate.Id, "t1", "user-b", "Admin", true, "looks good");

        Assert.Equal(ApprovalStatus.Approved, approved.Status);
        Assert.Equal("user-b", approved.ReviewedBy);
        Assert.Equal("looks good", approved.ReviewNotes);
        Assert.NotNull(approved.ReviewedAtUtc);
    }

    // ── Approval Policy Contract ──────────────────────────────────────

    [Fact]
    public async Task ApprovalPolicy_ContractShape()
    {
        var gov = new GovernanceService();
        var policies = await gov.ListApprovalPoliciesAsync();

        foreach (var policy in policies)
        {
            Assert.NotEqual(Guid.Empty, policy.Id);
            Assert.False(string.IsNullOrWhiteSpace(policy.ActionType));
            Assert.False(string.IsNullOrWhiteSpace(policy.Description));
            Assert.False(string.IsNullOrWhiteSpace(policy.RequiredApproverRole));
            Assert.True(policy.IsEnabled);
        }
    }

    // ── Audit Entry Contract ──────────────────────────────────────────

    [Fact]
    public async Task AuditEntry_ContractShape()
    {
        var svc = new AuditLogService(NullLogger<AuditLogService>.Instance);
        var entry = await svc.RecordAsync(
            "test.event", "agent", "TestSource", "subject-1", "agent",
            "execute", "task", "task-1", "Test description",
            new Dictionary<string, string> { ["extra"] = "data" });

        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Equal("test.event", entry.EventType);
        Assert.Equal("agent", entry.Category);
        Assert.Equal("TestSource", entry.Source);
        Assert.Equal("subject-1", entry.SubjectId);
        Assert.Equal("agent", entry.SubjectType);
        Assert.Equal("execute", entry.Action);
        Assert.Equal("task", entry.ResourceType);
        Assert.Equal("task-1", entry.ResourceId);
        Assert.Equal("Test description", entry.Description);
        Assert.NotEmpty(entry.Checksum);
        Assert.True(entry.OccurredAtUtc > DateTimeOffset.MinValue);
        Assert.Equal("data", entry.Metadata["extra"]);
    }

    [Fact]
    public async Task AuditQueryResult_ContractShape()
    {
        var svc = new AuditLogService(NullLogger<AuditLogService>.Instance);
        await svc.RecordAsync("e1", "agent", "s", "s1", "a", "a1", "r", "r1", "d");

        var result = await svc.QueryAsync();

        Assert.NotNull(result.Entries);
        Assert.True(result.TotalCount >= 1);
        Assert.True(result.QueriedAtUtc > DateTimeOffset.MinValue);
    }

    // ── Audit Log Status Contract ─────────────────────────────────────

    [Fact]
    public async Task AuditLogStatus_ContractShape()
    {
        var svc = new AuditLogService(NullLogger<AuditLogService>.Instance);
        await svc.RecordAsync("e1", "agent", "s", "s1", "a", "a1", "r", "r1", "d");

        var status = svc.GetStatus();

        Assert.True(status.IsActive);
        Assert.True(status.TotalEntries >= 1);
        Assert.NotEmpty(status.LatestChecksum);
        Assert.True(status.StatusAsOfUtc > DateTimeOffset.MinValue);
    }
}
