using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Security;

/// <summary>
/// Security tests for permission boundary enforcement:
/// privilege escalation prevention, system role immutability,
/// permission inheritance, and least-privilege verification.
/// </summary>
public sealed class PermissionBoundaryTests
{
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();

    private RbacService CreateService() => new(_eventBus);

    // ── Privilege Escalation Prevention ────────────────────────────────

    [Fact]
    public async Task ViewerUser_CannotAssignAdminRole()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var viewer = roles.First(r => r.Name == "Viewer");
        var admin = roles.First(r => r.Name == "Admin");

        // Assign viewer role to a user
        await svc.AssignRoleAsync("viewer-user", "user", viewer.Id, "system");

        // Verify the viewer lacks rbac:write permission
        var perms = await svc.GetEffectivePermissionsAsync("viewer-user");
        Assert.DoesNotContain("rbac:write", perms);

        // The API layer would enforce this; the service trusts its caller
        // This test documents that viewers lack the permission to modify roles
    }

    [Fact]
    public async Task OperatorUser_LacksAdminWritePermission()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var op = roles.First(r => r.Name == "Operator");

        await svc.AssignRoleAsync("op-user", "user", op.Id, "system");
        var perms = await svc.GetEffectivePermissionsAsync("op-user");

        Assert.DoesNotContain("admin:write", perms);
        Assert.DoesNotContain("rbac:write", perms);
        Assert.DoesNotContain("policy:write", perms);
        Assert.DoesNotContain("agents:write", perms);
    }

    // ── System Role Protection ────────────────────────────────────────

    [Fact]
    public async Task SystemRole_CannotBeUpdated()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();

        foreach (var role in roles)
        {
            Assert.True(role.IsSystem);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.UpdateRoleAsync(role.Id, "hacked", new[] { "everything:all" }));
        }
    }

    [Fact]
    public async Task SystemRole_CannotBeDeleted()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();

        foreach (var role in roles)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.DeleteRoleAsync(role.Id));
        }
    }

    // ── Custom Role Boundaries ────────────────────────────────────────

    [Fact]
    public async Task CustomRole_CannotExceedGrantedPermissions()
    {
        var svc = CreateService();

        // Create a restricted custom role
        var restricted = await svc.CreateRoleAsync("Restricted", "Very limited",
            new[] { "monitoring:read" });

        await svc.AssignRoleAsync("restricted-user", "user", restricted.Id, "admin");
        var perms = await svc.GetEffectivePermissionsAsync("restricted-user");

        // Should only have the explicitly granted permission
        Assert.Single(perms);
        Assert.Contains("monitoring:read", perms);
    }

    // ── Access Decision Enforcement ───────────────────────────────────

    [Fact]
    public async Task UnassignedSubject_DeniedAll()
    {
        var svc = CreateService();

        var resources = new[] { "agents", "workflows", "connectors", "admin", "policy" };
        var actions = new[] { "agents:read", "workflows:write", "admin:write", "connectors:execute" };

        foreach (var action in actions)
        {
            var decision = await svc.EvaluateAccessAsync("unknown-user", "any-resource", action);
            Assert.False(decision.IsAllowed,
                $"Expected denied for unassigned user on action '{action}'");
        }
    }

    [Fact]
    public async Task RevokedUser_LosesAllAccess()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var admin = roles.First(r => r.Name == "Admin");

        var assignment = await svc.AssignRoleAsync("temp-admin", "user", admin.Id, "system");

        // Verify access
        var before = await svc.EvaluateAccessAsync("temp-admin", "agents", "agents:write");
        Assert.True(before.IsAllowed);

        // Revoke
        await svc.RevokeRoleAsync(assignment.Id);

        // Verify denied
        var after = await svc.EvaluateAccessAsync("temp-admin", "agents", "agents:write");
        Assert.False(after.IsAllowed);
    }

    // ── Separation of Duties in Approval ──────────────────────────────

    [Fact]
    public async Task SeparationOfDuties_RequesterCannotSelfApprove()
    {
        var gov = new GovernanceService();
        var gate = await gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-x", "reason");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gov.ReviewApprovalAsync(gate.Id, "t1", "user-x", "Admin", true, "self-approve"));

        Assert.Contains("Separation of duties", ex.Message);
    }

    [Fact]
    public async Task InsufficientRole_CannotApprove()
    {
        var gov = new GovernanceService();
        var gate = await gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "t1", "user-a", "reason");

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            gov.ReviewApprovalAsync(gate.Id, "t1", "user-b", "Viewer", true, "lacking role"));

        Assert.Contains("Viewer", ex.Message);
        Assert.Contains("Admin", ex.Message);
    }

    // ── RBAC Event Auditing ───────────────────────────────────────────

    [Fact]
    public async Task RoleCreation_EmitsAuditEvent()
    {
        var svc = CreateService();
        await svc.CreateRoleAsync("TestRole", "desc", new[] { "agents:read" });

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<Core.Models.SystemEvent>(e => e.EventType == "rbac.role.created"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RoleAssignment_EmitsAuditEvent()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var admin = roles.First(r => r.Name == "Admin");

        await svc.AssignRoleAsync("user-1", "user", admin.Id, "system");

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<Core.Models.SystemEvent>(e => e.EventType == "rbac.role.assigned"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RoleRevocation_EmitsAuditEvent()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var admin = roles.First(r => r.Name == "Admin");
        var assignment = await svc.AssignRoleAsync("user-1", "user", admin.Id, "system");

        await svc.RevokeRoleAsync(assignment.Id);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<Core.Models.SystemEvent>(e => e.EventType == "rbac.role.revoked"),
            Arg.Any<CancellationToken>());
    }
}
