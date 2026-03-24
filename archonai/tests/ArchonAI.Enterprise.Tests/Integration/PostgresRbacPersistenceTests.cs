using ArchonAI.Core.Models.Rbac;
using ArchonAI.Enterprise.Tests.Infrastructure;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Real PostgreSQL integration tests for <see cref="ArchonAI.Persistence.Stores.PostgresRbacStore"/>.
/// Uses Testcontainers to start an ephemeral PostgreSQL instance.
///
/// These tests prove that RBAC state (roles, assignments, policies, access decisions)
/// survives store re-instantiation — the critical persistence guarantee.
/// </summary>
[Collection("PostgresRbacTrustTier")]
[Trait("Category", "Integration")]
[Trait("Database", "PostgreSQL")]
public sealed class PostgresRbacPersistenceTests : IAsyncLifetime
{
    private readonly PostgresRbacTrustTierFixture _fixture;

    public PostgresRbacPersistenceTests(PostgresRbacTrustTierFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanTablesAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Test 1: Role CRUD round-trip ──────────────────────────────────────

    [Fact]
    public async Task CreateRole_PersistsAndRetrievesCorrectly()
    {
        var store = _fixture.CreateRbacStore();

        var role = await store.CreateRoleAsync(
            "Auditor", "Read-only audit access",
            new[] { "monitoring:read", "policy:read" });

        Assert.NotEqual(Guid.Empty, role.Id);
        Assert.Equal("Auditor", role.Name);
        Assert.Equal("Read-only audit access", role.Description);
        Assert.Contains("monitoring:read", role.Permissions);
        Assert.Contains("policy:read", role.Permissions);
        Assert.False(role.IsSystem);

        // Retrieve via same store
        var fetched = await store.GetRoleAsync(role.Id);
        Assert.NotNull(fetched);
        Assert.Equal(role.Id, fetched.Id);
        Assert.Equal(role.Name, fetched.Name);
        Assert.Equal(role.Permissions.Count, fetched.Permissions.Count);
    }

    // ── Test 2: Role survives store re-instantiation ─────────────────────

    [Fact]
    public async Task Role_SurvivesStoreReinstantiation()
    {
        var store1 = _fixture.CreateRbacStore();
        var role = await store1.CreateRoleAsync(
            "DevOps", "CI/CD pipeline access",
            new[] { "workflows:read", "workflows:execute", "connectors:read" });

        // New instance — simulates service restart
        var store2 = _fixture.CreateRbacStore();
        var fetched = await store2.GetRoleAsync(role.Id);

        Assert.NotNull(fetched);
        Assert.Equal(role.Id, fetched.Id);
        Assert.Equal("DevOps", fetched.Name);
        Assert.Equal("CI/CD pipeline access", fetched.Description);
        Assert.Equal(3, fetched.Permissions.Count);
        Assert.Contains("workflows:execute", fetched.Permissions);
    }

    // ── Test 3: Role update persists ─────────────────────────────────────

    [Fact]
    public async Task RoleUpdate_PersistsAcrossInstances()
    {
        var store1 = _fixture.CreateRbacStore();
        var role = await store1.CreateRoleAsync(
            "Analyst", "Data analysis",
            new[] { "agents:read" });

        await store1.UpdateRoleAsync(role.Id, "Enhanced data analysis",
            new[] { "agents:read", "workflows:read", "monitoring:read" });

        // New instance — verify update persisted
        var store2 = _fixture.CreateRbacStore();
        var fetched = await store2.GetRoleAsync(role.Id);

        Assert.NotNull(fetched);
        Assert.Equal("Enhanced data analysis", fetched.Description);
        Assert.Equal(3, fetched.Permissions.Count);
        Assert.Contains("monitoring:read", fetched.Permissions);
    }

    // ── Test 4: Role deletion persists ───────────────────────────────────

    [Fact]
    public async Task RoleDelete_PersistsAcrossInstances()
    {
        var store1 = _fixture.CreateRbacStore();
        var role = await store1.CreateRoleAsync(
            "Temporary", "Short-lived role", new[] { "agents:read" });

        await store1.DeleteRoleAsync(role.Id);

        // New instance — role should be gone
        var store2 = _fixture.CreateRbacStore();
        var fetched = await store2.GetRoleAsync(role.Id);
        Assert.Null(fetched);
    }

    // ── Test 5: System role immutability ──────────────────────────────────

    [Fact]
    public async Task SystemRole_CannotBeModifiedOrDeleted()
    {
        var store = _fixture.CreateRbacStore();

        // Manually insert a system role to test immutability
        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var roleId = Guid.NewGuid();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO archonai.rbac_roles (id, name, description, permissions, is_system, created_at_utc) " +
            "VALUES (@id, 'Admin', 'Full access', '[\"admin:read\",\"admin:write\"]'::jsonb, true, now());", conn);
        cmd.Parameters.AddWithValue("id", roleId);
        await cmd.ExecuteNonQueryAsync();

        // Update should be blocked
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.UpdateRoleAsync(roleId, "Hacked", new[] { "agents:read" }));

        // Delete should be blocked
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.DeleteRoleAsync(roleId));

        // Verify role is unchanged via new instance
        var store2 = _fixture.CreateRbacStore();
        var fetched = await store2.GetRoleAsync(roleId);
        Assert.NotNull(fetched);
        Assert.Equal("Admin", fetched.Name);
        Assert.True(fetched.IsSystem);
    }

    // ── Test 6: Assignment lifecycle ─────────────────────────────────────

    [Fact]
    public async Task AssignmentLifecycle_AssignAndRevoke_PersistsAcrossInstances()
    {
        var store1 = _fixture.CreateRbacStore();
        var role = await store1.CreateRoleAsync(
            "Engineer", "Engineering access",
            new[] { "agents:read", "workflows:read", "workflows:execute" });

        // Assign role to a user
        var assignment = await store1.AssignRoleAsync(
            "user-alice", "user", role.Id, "admin-bob");

        Assert.NotEqual(Guid.Empty, assignment.Id);
        Assert.Equal("user-alice", assignment.SubjectId);
        Assert.Equal("user", assignment.SubjectType);
        Assert.Equal(role.Id, assignment.RoleId);
        Assert.Equal("admin-bob", assignment.AssignedBy);

        // New instance — assignment survives restart
        var store2 = _fixture.CreateRbacStore();
        var assignments = await store2.GetAssignmentsAsync("user-alice");

        Assert.Single(assignments);
        Assert.Equal(assignment.Id, assignments[0].Id);
        Assert.Equal(role.Id, assignments[0].RoleId);

        // Revoke the assignment
        await store2.RevokeRoleAsync(assignment.Id);

        // New instance — revocation persisted
        var store3 = _fixture.CreateRbacStore();
        var postRevoke = await store3.GetAssignmentsAsync("user-alice");
        Assert.Empty(postRevoke);
    }

    // ── Test 7: Assignment cascades on role deletion ─────────────────────

    [Fact]
    public async Task AssignmentCascade_DeletingRoleClearsAssignments()
    {
        var store = _fixture.CreateRbacStore();
        var role = await store.CreateRoleAsync(
            "Ephemeral", "Short-lived", new[] { "agents:read" });

        await store.AssignRoleAsync("user-x", "user", role.Id, "admin");
        await store.AssignRoleAsync("agent-y", "agent", role.Id, "admin");

        // Verify assignments exist
        var allAssignments = await store.GetAssignmentsAsync();
        Assert.Equal(2, allAssignments.Count);

        // Delete role — assignments should cascade
        await store.DeleteRoleAsync(role.Id);

        var store2 = _fixture.CreateRbacStore();
        var postDelete = await store2.GetAssignmentsAsync("user-x");
        Assert.Empty(postDelete);

        var postDeleteAgent = await store2.GetAssignmentsAsync("agent-y");
        Assert.Empty(postDeleteAgent);
    }

    // ── Test 8: Policy CRUD persists ─────────────────────────────────────

    [Fact]
    public async Task PolicyCrud_PersistsAcrossInstances()
    {
        var store1 = _fixture.CreateRbacStore();

        var policy = await store1.CreatePolicyAsync(
            "deny-agent-write",
            "Block agent write access to connectors",
            new[] { "connectors:write" },
            "connectors",
            "deny",
            new Dictionary<string, string> { ["env"] = "production" });

        Assert.NotEqual(Guid.Empty, policy.Id);
        Assert.Equal("deny", policy.Effect);
        Assert.True(policy.IsEnabled);

        // New instance — policy survives
        var store2 = _fixture.CreateRbacStore();
        var policies = await store2.GetPoliciesAsync();
        Assert.Single(policies);
        Assert.Equal("deny-agent-write", policies[0].Name);
        Assert.Equal("connectors", policies[0].Resource);
        Assert.Contains("connectors:write", policies[0].RequiredPermissions);
        Assert.Equal("production", policies[0].Conditions["env"]);

        // Disable policy
        await store2.UpdatePolicyAsync(policy.Id, false);

        var store3 = _fixture.CreateRbacStore();
        var updated = (await store3.GetPoliciesAsync()).First(p => p.Id == policy.Id);
        Assert.False(updated.IsEnabled);

        // Delete policy
        await store3.DeletePolicyAsync(policy.Id);

        var store4 = _fixture.CreateRbacStore();
        var remaining = await store4.GetPoliciesAsync();
        Assert.Empty(remaining);
    }

    // ── Test 9: Access evaluation with policies across instances ─────────

    [Fact]
    public async Task AccessEvaluation_PolicyDenyTakesPrecedence_AcrossInstances()
    {
        var store1 = _fixture.CreateRbacStore();

        // Create role with connector permissions
        var role = await store1.CreateRoleAsync(
            "ConnectorAdmin", "Full connector access",
            new[] { "connectors:read", "connectors:write", "connectors:execute" });

        // Assign to a user
        await store1.AssignRoleAsync("user-charlie", "user", role.Id, "admin");

        // Create allow policy for connectors
        await store1.CreatePolicyAsync(
            "allow-connector-ops", "Allow connector operations",
            new[] { "connectors:read" }, "connectors", "allow",
            new Dictionary<string, string>());

        // Create deny policy for connectors (deny takes precedence)
        await store1.CreatePolicyAsync(
            "deny-connector-write", "Block connector writes",
            new[] { "connectors:write" }, "connectors", "deny",
            new Dictionary<string, string>());

        // New instance — evaluate access
        var store2 = _fixture.CreateRbacStore();

        // User has connectors:write permission, but deny policy should block
        var decision = await store2.EvaluateAccessAsync(
            "user-charlie", "connectors", "connectors:write");

        Assert.False(decision.IsAllowed);
        Assert.Contains("denied", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 10: Effective permissions across instances ───────────────────

    [Fact]
    public async Task EffectivePermissions_AggregateAcrossRoles_SurvivesRestart()
    {
        var store1 = _fixture.CreateRbacStore();

        var roleA = await store1.CreateRoleAsync(
            "RoleA", "First role", new[] { "agents:read", "agents:write" });
        var roleB = await store1.CreateRoleAsync(
            "RoleB", "Second role", new[] { "workflows:read", "agents:read" });

        await store1.AssignRoleAsync("user-multi", "user", roleA.Id, "admin");
        await store1.AssignRoleAsync("user-multi", "user", roleB.Id, "admin");

        // New instance
        var store2 = _fixture.CreateRbacStore();
        var permissions = await store2.GetEffectivePermissionsAsync("user-multi");

        // Should be union: agents:read, agents:write, workflows:read
        Assert.Contains("agents:read", permissions);
        Assert.Contains("agents:write", permissions);
        Assert.Contains("workflows:read", permissions);
        Assert.Equal(3, permissions.Count);
    }

    // ── Test 11: GetRoles lists all roles ────────────────────────────────

    [Fact]
    public async Task GetRoles_ListsAllRoles_AcrossInstances()
    {
        var store1 = _fixture.CreateRbacStore();
        await store1.CreateRoleAsync("Alpha", "First", new[] { "agents:read" });
        await store1.CreateRoleAsync("Beta", "Second", new[] { "workflows:read" });

        var store2 = _fixture.CreateRbacStore();
        var roles = await store2.GetRolesAsync();

        Assert.Equal(2, roles.Count);
        Assert.Contains(roles, r => r.Name == "Alpha");
        Assert.Contains(roles, r => r.Name == "Beta");
    }

    // ── Test 12: Status reflects accurate counts ─────────────────────────

    [Fact]
    public async Task GetStatus_ReflectsAccurateCounts()
    {
        var store = _fixture.CreateRbacStore();

        var role = await store.CreateRoleAsync(
            "Counter", "For counting", new[] { "agents:read" });
        await store.AssignRoleAsync("user-count", "user", role.Id, "admin");
        await store.CreatePolicyAsync(
            "count-policy", "For counting", new[] { "agents:read" },
            "agents", "allow", new Dictionary<string, string>());

        var status = store.GetStatus();

        Assert.True(status.IsActive);
        Assert.Equal(1, status.TotalRoles);
        Assert.Equal(1, status.TotalAssignments);
        Assert.Equal(1, status.TotalPolicies);
    }
}
