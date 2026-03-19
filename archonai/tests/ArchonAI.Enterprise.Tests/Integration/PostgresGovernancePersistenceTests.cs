using ArchonAI.Core.Models.Governance;
using ArchonAI.Enterprise.Tests.Infrastructure;
using ArchonAI.Persistence.Stores;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Real PostgreSQL integration tests for <see cref="PostgresGovernanceStore"/>.
/// Uses Testcontainers to start an ephemeral PostgreSQL instance.
///
/// These tests prove that governance state survives store re-instantiation —
/// the critical persistence guarantee that in-memory tests cannot provide.
///
/// Each test creates a new store instance via the shared fixture, ensuring
/// we are testing the real PostgresGovernanceStore, never the in-memory fallback.
/// </summary>
[Collection("PostgresGovernance")]
[Trait("Category", "Integration")]
[Trait("Database", "PostgreSQL")]
public sealed class PostgresGovernancePersistenceTests : IAsyncLifetime
{
    private readonly PostgresGovernanceFixture _fixture;

    public PostgresGovernancePersistenceTests(PostgresGovernanceFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanTablesAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Test 1: Create and retrieve approval gate ───────────────────────

    [Fact]
    public async Task CreateApprovalGate_PersistsAndRetrievesCorrectly()
    {
        var store = _fixture.CreateStore();

        var gate = await store.RequestApprovalAsync(
            "workflow.cancel", "wf-123", "tenant-a", "alice", "Workflow is stuck");

        Assert.NotEqual(Guid.Empty, gate.Id);
        Assert.Equal("workflow.cancel", gate.ActionType);
        Assert.Equal("wf-123", gate.ResourceId);
        Assert.Equal("tenant-a", gate.TenantId);
        Assert.Equal("alice", gate.RequestedBy);
        Assert.Equal("Workflow is stuck", gate.Justification);
        Assert.Equal(ApprovalStatus.Pending, gate.Status);
        Assert.Null(gate.ReviewedBy);
        Assert.Null(gate.ReviewedAtUtc);
        Assert.Equal(GateExecutionStatus.NotExecuted, gate.ExecutionStatus);

        // Retrieve via the same store instance
        var fetched = await store.GetApprovalAsync(gate.Id, "tenant-a");
        Assert.NotNull(fetched);
        Assert.Equal(gate.Id, fetched.Id);
        Assert.Equal(gate.ActionType, fetched.ActionType);
        Assert.Equal(gate.Justification, fetched.Justification);
    }

    // ── Test 2: Retrieval after new store instantiation (restart proof) ─

    [Fact]
    public async Task ApprovalGate_SurvivesStoreReinstantiation()
    {
        // Instance 1: create the gate
        var store1 = _fixture.CreateStore();
        var gate = await store1.RequestApprovalAsync(
            "policy.delete", "policy-99", "tenant-restart", "bob",
            "Testing restart persistence");

        // Instance 2: simulate service restart — new store, same database
        var store2 = _fixture.CreateStore();
        var fetched = await store2.GetApprovalAsync(gate.Id, "tenant-restart");

        Assert.NotNull(fetched);
        Assert.Equal(gate.Id, fetched.Id);
        Assert.Equal("policy.delete", fetched.ActionType);
        Assert.Equal("policy-99", fetched.ResourceId);
        Assert.Equal("tenant-restart", fetched.TenantId);
        Assert.Equal("bob", fetched.RequestedBy);
        Assert.Equal("Testing restart persistence", fetched.Justification);
        Assert.Equal(ApprovalStatus.Pending, fetched.Status);
    }

    // ── Test 3: Status transitions persist ──────────────────────────────

    [Fact]
    public async Task StatusTransition_Approve_PersistsAcrossInstances()
    {
        // Seed policy so ReviewApprovalAsync can enforce role checks
        var store1 = _fixture.CreateStore();
        await store1.CreateApprovalPolicyAsync(
            "connector.disconnect", "Disconnect integration", "Admin", true);

        var gate = await store1.RequestApprovalAsync(
            "connector.disconnect", "conn-42", "tenant-status", "charlie",
            "Deprecated connector");

        // Approve with a different reviewer (separation of duties)
        var approved = await store1.ReviewApprovalAsync(
            gate.Id, "tenant-status", "admin-dave", "Admin", true, "Approved for cleanup");

        Assert.Equal(ApprovalStatus.Approved, approved.Status);
        Assert.Equal("admin-dave", approved.ReviewedBy);
        Assert.Equal("Approved for cleanup", approved.ReviewNotes);
        Assert.NotNull(approved.ReviewedAtUtc);

        // Instance 2: verify the transition persisted
        var store2 = _fixture.CreateStore();
        var fetched = await store2.GetApprovalAsync(gate.Id, "tenant-status");

        Assert.NotNull(fetched);
        Assert.Equal(ApprovalStatus.Approved, fetched.Status);
        Assert.Equal("admin-dave", fetched.ReviewedBy);
        Assert.Equal("Approved for cleanup", fetched.ReviewNotes);
        Assert.NotNull(fetched.ReviewedAtUtc);
    }

    [Fact]
    public async Task StatusTransition_Deny_PersistsCorrectly()
    {
        var store = _fixture.CreateStore();
        await store.CreateApprovalPolicyAsync(
            "rbac.role.delete", "Delete RBAC role", "Admin", false);

        var gate = await store.RequestApprovalAsync(
            "rbac.role.delete", "role-viewer", "tenant-deny", "eve", "Remove unused role");

        var denied = await store.ReviewApprovalAsync(
            gate.Id, "tenant-deny", "admin-frank", "Admin", false, "Role still in use");

        Assert.Equal(ApprovalStatus.Denied, denied.Status);

        // Verify via new instance
        var store2 = _fixture.CreateStore();
        var fetched = await store2.GetApprovalAsync(gate.Id, "tenant-deny");
        Assert.NotNull(fetched);
        Assert.Equal(ApprovalStatus.Denied, fetched.Status);
        Assert.Equal("admin-frank", fetched.ReviewedBy);
        Assert.Equal("Role still in use", fetched.ReviewNotes);
    }

    // ── Test 4: Approval lifecycle persistence (full round-trip) ────────

    [Fact]
    public async Task FullLifecycle_CreateReviewExecute_AllPersistedAcrossInstances()
    {
        var store1 = _fixture.CreateStore();
        await store1.CreateApprovalPolicyAsync(
            "strategy.override", "Override strategy", "Admin", true);

        // Step 1: Request
        var gate = await store1.RequestApprovalAsync(
            "strategy.override", "strat-7", "tenant-lifecycle", "grace",
            "Need to override AI strategy", "{\"newStrategy\":\"manual\"}");

        Assert.Equal(ApprovalStatus.Pending, gate.Status);

        // Step 2: Approve (different user — separation of duties)
        var approved = await store1.ReviewApprovalAsync(
            gate.Id, "tenant-lifecycle", "admin-hank", "Admin", true, "Override approved");

        // Step 3: Record execution result
        var executed = await store1.RecordExecutionResultAsync(
            gate.Id, GateExecutionStatus.Succeeded, null);

        Assert.Equal(GateExecutionStatus.Succeeded, executed.ExecutionStatus);
        Assert.NotNull(executed.ExecutedAtUtc);

        // Step 4: Verify everything via a NEW store instance
        var store2 = _fixture.CreateStore();
        var fetched = await store2.GetApprovalAsync(gate.Id, "tenant-lifecycle");

        Assert.NotNull(fetched);
        Assert.Equal(ApprovalStatus.Approved, fetched.Status);
        Assert.Equal("admin-hank", fetched.ReviewedBy);
        Assert.Equal("Override approved", fetched.ReviewNotes);
        Assert.Equal(GateExecutionStatus.Succeeded, fetched.ExecutionStatus);
        Assert.Null(fetched.ExecutionError);
        Assert.NotNull(fetched.ExecutedAtUtc);
        Assert.Equal("{\"newStrategy\":\"manual\"}", fetched.ActionPayload);

        // Step 5: Audit trail persisted
        var history = await store2.GetApprovalHistoryAsync("tenant-lifecycle", "strategy.override", 50);
        Assert.Single(history);
        Assert.Equal(gate.Id, history[0].ApprovalGateId);
        Assert.Equal(ApprovalStatus.Approved, history[0].Outcome);
        Assert.Equal("grace", history[0].RequestedBy);
        Assert.Equal("admin-hank", history[0].ReviewedBy);
    }

    // ── Test 5: Execution failure persistence ───────────────────────────

    [Fact]
    public async Task ExecutionFailure_ErrorPersistsAcrossInstances()
    {
        var store1 = _fixture.CreateStore();
        await store1.CreateApprovalPolicyAsync(
            "workflow.cancel", "Cancel workflow", "Admin", false);

        var gate = await store1.RequestApprovalAsync(
            "workflow.cancel", "wf-fail", "tenant-fail", "ivan", "Cancel broken workflow");

        await store1.ReviewApprovalAsync(
            gate.Id, "tenant-fail", "admin-judy", "Admin", true, "Go ahead");

        await store1.RecordExecutionResultAsync(
            gate.Id, GateExecutionStatus.Failed, "Workflow engine returned 503");

        // Verify via new instance
        var store2 = _fixture.CreateStore();
        var fetched = await store2.GetApprovalAsync(gate.Id, "tenant-fail");

        Assert.NotNull(fetched);
        Assert.Equal(GateExecutionStatus.Failed, fetched.ExecutionStatus);
        Assert.Equal("Workflow engine returned 503", fetched.ExecutionError);
        Assert.NotNull(fetched.ExecutedAtUtc);
    }

    // ── Test 6: Tenant isolation in persistence ─────────────────────────

    [Fact]
    public async Task TenantIsolation_CrossTenantAccess_Blocked()
    {
        var store = _fixture.CreateStore();

        var gateA = await store.RequestApprovalAsync(
            "workflow.cancel", "wf-alpha", "tenant-iso-a", "alice", "Cancel alpha");
        var gateB = await store.RequestApprovalAsync(
            "workflow.cancel", "wf-beta", "tenant-iso-b", "bob", "Cancel beta");

        // Each tenant can only see their own gate
        Assert.NotNull(await store.GetApprovalAsync(gateA.Id, "tenant-iso-a"));
        Assert.Null(await store.GetApprovalAsync(gateA.Id, "tenant-iso-b"));
        Assert.NotNull(await store.GetApprovalAsync(gateB.Id, "tenant-iso-b"));
        Assert.Null(await store.GetApprovalAsync(gateB.Id, "tenant-iso-a"));

        // Pending lists are isolated
        var pendingA = await store.ListPendingApprovalsAsync("tenant-iso-a");
        var pendingB = await store.ListPendingApprovalsAsync("tenant-iso-b");
        Assert.Single(pendingA);
        Assert.Single(pendingB);
        Assert.Equal(gateA.Id, pendingA[0].Id);
        Assert.Equal(gateB.Id, pendingB[0].Id);

        // Cross-tenant review is blocked
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            store.ReviewApprovalAsync(gateA.Id, "tenant-iso-b", "attacker", "Admin", true, "pwned"));

        // Verify gate was NOT modified
        var still = await store.GetApprovalAsync(gateA.Id, "tenant-iso-a");
        Assert.NotNull(still);
        Assert.Equal(ApprovalStatus.Pending, still.Status);
    }

    // ── Test 7: Tenant isolation persists across store instances ─────────

    [Fact]
    public async Task TenantIsolation_PersistsAcrossInstances()
    {
        var store1 = _fixture.CreateStore();

        await store1.RequestApprovalAsync(
            "policy.delete", "p-1", "tenant-cross-a", "alice", "Remove policy");
        await store1.RequestApprovalAsync(
            "policy.delete", "p-2", "tenant-cross-b", "bob", "Remove policy");

        // New instance — tenant isolation must still hold
        var store2 = _fixture.CreateStore();

        var pendingA = await store2.ListPendingApprovalsAsync("tenant-cross-a");
        var pendingB = await store2.ListPendingApprovalsAsync("tenant-cross-b");

        Assert.Single(pendingA);
        Assert.Single(pendingB);
        Assert.Equal("tenant-cross-a", pendingA[0].TenantId);
        Assert.Equal("tenant-cross-b", pendingB[0].TenantId);
    }

    // ── Test 8: Deduplication persists ──────────────────────────────────

    [Fact]
    public async Task Deduplication_SameActionResourceTenant_ReturnsSameGate()
    {
        var store = _fixture.CreateStore();

        var gate1 = await store.RequestApprovalAsync(
            "workflow.cancel", "wf-dedup", "tenant-dedup", "alice", "First request");
        var gate2 = await store.RequestApprovalAsync(
            "workflow.cancel", "wf-dedup", "tenant-dedup", "bob", "Second request");

        // Same pending gate returned (deduplication)
        Assert.Equal(gate1.Id, gate2.Id);

        // Verify via new instance — still only one
        var store2 = _fixture.CreateStore();
        var pending = await store2.ListPendingApprovalsAsync("tenant-dedup");
        Assert.Single(pending);
        Assert.Equal(gate1.Id, pending[0].Id);
    }

    // ── Test 9: Approval policy persistence ─────────────────────────────

    [Fact]
    public async Task ApprovalPolicy_PersistsAcrossInstances()
    {
        var store1 = _fixture.CreateStore();

        var policy = await store1.CreateApprovalPolicyAsync(
            "custom.action", "Custom action requiring approval", "Operator", true);

        Assert.NotEqual(Guid.Empty, policy.Id);
        Assert.Equal("custom.action", policy.ActionType);
        Assert.True(policy.RequireSeparationOfDuties);
        Assert.True(policy.IsEnabled);

        // New instance
        var store2 = _fixture.CreateStore();

        var requiresApproval = await store2.RequiresApprovalAsync("custom.action");
        Assert.True(requiresApproval);

        var policies = await store2.ListApprovalPoliciesAsync();
        Assert.Contains(policies, p => p.ActionType == "custom.action" && p.RequiredApproverRole == "Operator");
    }

    // ── Test 10: Audit history persists across instances ─────────────────

    [Fact]
    public async Task AuditHistory_PersistsAcrossInstances()
    {
        var store1 = _fixture.CreateStore();
        await store1.CreateApprovalPolicyAsync(
            "connector.disconnect", "Disconnect connector", "Admin", false);

        var gate = await store1.RequestApprovalAsync(
            "connector.disconnect", "conn-audit", "tenant-audit", "alice", "Audit test");

        await store1.ReviewApprovalAsync(
            gate.Id, "tenant-audit", "admin-bob", "Admin", true, "Audited");

        // New instance — verify audit trail survived
        var store2 = _fixture.CreateStore();
        var history = await store2.GetApprovalHistoryAsync("tenant-audit", null, 50);

        Assert.Single(history);
        Assert.Equal(gate.Id, history[0].ApprovalGateId);
        Assert.Equal("connector.disconnect", history[0].ActionType);
        Assert.Equal("tenant-audit", history[0].TenantId);
        Assert.Equal("alice", history[0].RequestedBy);
        Assert.Equal("admin-bob", history[0].ReviewedBy);
        Assert.Equal(ApprovalStatus.Approved, history[0].Outcome);
    }
}
