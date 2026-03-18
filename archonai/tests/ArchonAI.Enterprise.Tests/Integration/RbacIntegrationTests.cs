using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Integration tests for RBAC service: role hierarchy, permission evaluation,
/// system role immutability, assignment lifecycle, and access decision logic.
/// </summary>
public sealed class RbacIntegrationTests
{
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();

    private RbacService CreateService() => new(_eventBus);

    // ── System Role Seeding ───────────────────────────────────────────

    [Fact]
    public async Task DefaultRoles_AdminOperatorViewer_AreSeeded()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();

        Assert.Equal(3, roles.Count);
        Assert.Contains(roles, r => r.Name == "Admin");
        Assert.Contains(roles, r => r.Name == "Operator");
        Assert.Contains(roles, r => r.Name == "Viewer");
    }

    [Fact]
    public async Task SystemRoles_CannotBeModified()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var admin = roles.First(r => r.Name == "Admin");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateRoleAsync(admin.Id, "hacked", new[] { "everything" }));
    }

    [Fact]
    public async Task SystemRoles_CannotBeDeleted()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var admin = roles.First(r => r.Name == "Admin");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.DeleteRoleAsync(admin.Id));
    }

    // ── Permission Evaluation ─────────────────────────────────────────

    [Fact]
    public async Task AdminRole_HasAllPermissions()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var admin = roles.First(r => r.Name == "Admin");

        await svc.AssignRoleAsync("user-1", "user", admin.Id, "system");
        var perms = await svc.GetEffectivePermissionsAsync("user-1");

        Assert.Contains("agents:read", perms);
        Assert.Contains("agents:write", perms);
        Assert.Contains("agents:execute", perms);
        Assert.Contains("admin:write", perms);
        Assert.Contains("rbac:write", perms);
        Assert.Contains("policy:write", perms);
    }

    [Fact]
    public async Task ViewerRole_LacksWritePermissions()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var viewer = roles.First(r => r.Name == "Viewer");

        await svc.AssignRoleAsync("viewer-1", "user", viewer.Id, "system");
        var perms = await svc.GetEffectivePermissionsAsync("viewer-1");

        Assert.DoesNotContain("agents:write", perms);
        Assert.DoesNotContain("admin:write", perms);
        Assert.DoesNotContain("rbac:write", perms);
        Assert.DoesNotContain("workflows:write", perms);
        Assert.Contains("agents:read", perms);
    }

    [Fact]
    public async Task OperatorRole_HasExecuteButNotWrite()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var op = roles.First(r => r.Name == "Operator");

        await svc.AssignRoleAsync("op-1", "user", op.Id, "system");
        var perms = await svc.GetEffectivePermissionsAsync("op-1");

        Assert.Contains("agents:execute", perms);
        Assert.DoesNotContain("agents:write", perms);
        Assert.DoesNotContain("admin:write", perms);
    }

    // ── Access Decision Logic ─────────────────────────────────────────

    [Fact]
    public async Task EvaluateAccess_AdminUser_Granted()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var admin = roles.First(r => r.Name == "Admin");
        await svc.AssignRoleAsync("admin-u", "user", admin.Id, "system");

        var decision = await svc.EvaluateAccessAsync("admin-u", "agents", "agents:write");

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public async Task EvaluateAccess_ViewerUser_DeniedWrite()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var viewer = roles.First(r => r.Name == "Viewer");
        await svc.AssignRoleAsync("viewer-u", "user", viewer.Id, "system");

        var decision = await svc.EvaluateAccessAsync("viewer-u", "agents", "agents:write");

        Assert.False(decision.IsAllowed);
        Assert.Contains("denied", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateAccess_UnassignedUser_Denied()
    {
        var svc = CreateService();

        var decision = await svc.EvaluateAccessAsync("nobody", "agents", "agents:read");

        Assert.False(decision.IsAllowed);
    }

    // ── Custom Role Lifecycle ─────────────────────────────────────────

    [Fact]
    public async Task CreateCustomRole_AssignAndEvaluate()
    {
        var svc = CreateService();
        var role = await svc.CreateRoleAsync("Auditor", "Read-only audit access",
            new[] { "monitoring:read", "policy:read" });

        await svc.AssignRoleAsync("auditor-1", "user", role.Id, "admin");
        var perms = await svc.GetEffectivePermissionsAsync("auditor-1");

        Assert.Contains("monitoring:read", perms);
        Assert.Contains("policy:read", perms);
        Assert.DoesNotContain("agents:write", perms);
    }

    [Fact]
    public async Task DeleteCustomRole_CascadesAssignments()
    {
        var svc = CreateService();
        var role = await svc.CreateRoleAsync("Temp", "Temporary role", new[] { "agents:read" });
        await svc.AssignRoleAsync("temp-user", "user", role.Id, "admin");

        await svc.DeleteRoleAsync(role.Id);

        var perms = await svc.GetEffectivePermissionsAsync("temp-user");
        Assert.Empty(perms);
    }

    // ── Assignment Deduplication / Multiple Roles ──────────────────────

    [Fact]
    public async Task MultipleRoles_PermissionsAreMerged()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var viewer = roles.First(r => r.Name == "Viewer");
        var custom = await svc.CreateRoleAsync("Writer", "Write access", new[] { "agents:write", "workflows:write" });

        await svc.AssignRoleAsync("multi-user", "user", viewer.Id, "admin");
        await svc.AssignRoleAsync("multi-user", "user", custom.Id, "admin");

        var perms = await svc.GetEffectivePermissionsAsync("multi-user");

        Assert.Contains("agents:read", perms);   // From Viewer
        Assert.Contains("agents:write", perms);   // From Writer
        Assert.Contains("workflows:write", perms); // From Writer
    }

    // ── Status Tracking ───────────────────────────────────────────────

    [Fact]
    public async Task AccessChecks_IncrementCounters()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var admin = roles.First(r => r.Name == "Admin");
        await svc.AssignRoleAsync("count-user", "user", admin.Id, "system");

        await svc.EvaluateAccessAsync("count-user", "agents", "agents:read");
        await svc.EvaluateAccessAsync("nobody", "agents", "agents:read"); // denial

        var status = svc.GetStatus();

        Assert.Equal(2, status.AccessChecks);
        Assert.Equal(1, status.AccessDenials);
    }

    // ── Revoke Assignment ─────────────────────────────────────────────

    [Fact]
    public async Task RevokeAssignment_RemovesPermissions()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var admin = roles.First(r => r.Name == "Admin");
        var assignment = await svc.AssignRoleAsync("revoke-user", "user", admin.Id, "system");

        await svc.RevokeRoleAsync(assignment.Id);

        var perms = await svc.GetEffectivePermissionsAsync("revoke-user");
        Assert.Empty(perms);
    }

    // ── Policy-Based Access Control ───────────────────────────────────

    [Fact]
    public async Task DenyPolicy_OverridesAllowPermission()
    {
        var svc = CreateService();
        var roles = await svc.GetRolesAsync();
        var admin = roles.First(r => r.Name == "Admin");
        await svc.AssignRoleAsync("deny-test", "user", admin.Id, "system");

        // Create a deny policy that matches admin permissions on a specific resource
        await svc.CreatePolicyAsync(
            "BlockAgentWrites", "Deny agent writes for compliance",
            new[] { "agents:write" }, "agents", "deny",
            new Dictionary<string, string>());

        var decision = await svc.EvaluateAccessAsync("deny-test", "agents", "agents:write");

        Assert.False(decision.IsAllowed);
        Assert.Contains("denied", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
