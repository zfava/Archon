using ArchonAI.Core.Models.Governance;
using ArchonAI.Enterprise.Tests.Infrastructure;
using ArchonAI.Persistence.Stores;
using Xunit;

namespace ArchonAI.Enterprise.Tests.RuntimeProof;

/// <summary>
/// Runtime-proof tests for the governance approval lifecycle.
/// These tests go beyond basic CRUD to prove operational governance behavior:
///   1. Separation of duties enforcement (requester cannot approve own request)
///   2. Role-based approval constraint enforcement
///   3. Full lifecycle transitions persisted across store restarts
///   4. Concurrent multi-tenant isolation under governance operations
///   5. Execution result tracking through failure and success states
///
/// All tests run against a real PostgreSQL container via Testcontainers.
/// </summary>
[Collection("PostgresGovernance")]
[Trait("Category", "RuntimeProof")]
[Trait("Subsystem", "Governance")]
[Trait("Database", "PostgreSQL")]
public sealed class GovernanceLifecycleRuntimeTests : IAsyncLifetime
{
    private readonly PostgresGovernanceFixture _fixture;

    public GovernanceLifecycleRuntimeTests(PostgresGovernanceFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanTablesAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Test 1: Separation of duties enforcement ────────────────────────

    [Fact]
    public async Task SeparationOfDuties_RequesterCannotApproveOwnRequest()
    {
        var store = _fixture.CreateStore();

        // Create a policy that requires separation of duties
        await store.CreateApprovalPolicyAsync(
            "dangerous.action", "Dangerous action", "Admin", requireSeparationOfDuties: true);

        var gate = await store.RequestApprovalAsync(
            "dangerous.action", "res-1", "tenant-sod", "alice", "Need to do dangerous thing");

        // Alice (requester) tries to approve her own request
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ReviewApprovalAsync(gate.Id, "tenant-sod", "alice", "Admin", true, "Self-approve"));

        Assert.Contains("Separation of duties", ex.Message);

        // Gate should remain pending after the failed self-approval attempt
        var stillPending = await store.GetApprovalAsync(gate.Id, "tenant-sod");
        Assert.NotNull(stillPending);
        Assert.Equal(ApprovalStatus.Pending, stillPending.Status);
        Assert.Null(stillPending.ReviewedBy);
    }

    // ── Test 2: Role-based approval enforcement ─────────────────────────

    [Fact]
    public async Task RoleEnforcement_WrongRoleCantApprove()
    {
        var store = _fixture.CreateStore();

        await store.CreateApprovalPolicyAsync(
            "role.test.action", "Requires Admin role", "Admin", requireSeparationOfDuties: false);

        var gate = await store.RequestApprovalAsync(
            "role.test.action", "res-2", "tenant-role", "bob", "Need admin approval");

        // Reviewer has 'Viewer' role, but policy requires 'Admin'
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.ReviewApprovalAsync(gate.Id, "tenant-role", "charlie", "Viewer", true, "Trying as viewer"));

        Assert.Contains("does not satisfy", ex.Message);

        // Gate remains pending
        var stillPending = await store.GetApprovalAsync(gate.Id, "tenant-role");
        Assert.Equal(ApprovalStatus.Pending, stillPending!.Status);
    }

    // ── Test 3: Double-review prevention ────────────────────────────────

    [Fact]
    public async Task DoubleReview_AlreadyApproved_ThrowsInvalidOp()
    {
        var store = _fixture.CreateStore();

        await store.CreateApprovalPolicyAsync(
            "double.review", "Double review test", "Admin", requireSeparationOfDuties: false);

        var gate = await store.RequestApprovalAsync(
            "double.review", "res-3", "tenant-double", "dave", "First request");

        // Approve successfully
        await store.ReviewApprovalAsync(
            gate.Id, "tenant-double", "admin-eve", "Admin", true, "Approved");

        // Try to approve again — should fail
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ReviewApprovalAsync(gate.Id, "tenant-double", "admin-frank", "Admin", true, "Second approval"));
    }

    // ── Test 4: Full lifecycle: Request → Approve → Execute → Verify ────

    [Fact]
    public async Task FullLifecycle_RequestApproveExecute_SurvivesRestart()
    {
        // Instance 1
        var store1 = _fixture.CreateStore();

        await store1.CreateApprovalPolicyAsync(
            "lifecycle.full", "Full lifecycle test", "Admin", requireSeparationOfDuties: true);

        // Step 1: Request
        var gate = await store1.RequestApprovalAsync(
            "lifecycle.full", "res-lifecycle", "tenant-lc", "grace",
            "Full lifecycle verification", "{\"action\":\"terminate\"}");

        Assert.Equal(ApprovalStatus.Pending, gate.Status);
        Assert.Equal(GateExecutionStatus.NotExecuted, gate.ExecutionStatus);

        // Step 2: Approve by different user
        var approved = await store1.ReviewApprovalAsync(
            gate.Id, "tenant-lc", "admin-hank", "Admin", true, "Lifecycle approved");

        Assert.Equal(ApprovalStatus.Approved, approved.Status);

        // Step 3: Record successful execution
        var executed = await store1.RecordExecutionResultAsync(
            gate.Id, GateExecutionStatus.Succeeded, null);

        Assert.Equal(GateExecutionStatus.Succeeded, executed.ExecutionStatus);
        Assert.Null(executed.ExecutionError);
        Assert.NotNull(executed.ExecutedAtUtc);

        // ── Instance 2: Verify all state survives store restart ──────
        var store2 = _fixture.CreateStore();
        var fetched = await store2.GetApprovalAsync(gate.Id, "tenant-lc");

        Assert.NotNull(fetched);
        Assert.Equal(ApprovalStatus.Approved, fetched.Status);
        Assert.Equal("admin-hank", fetched.ReviewedBy);
        Assert.Equal(GateExecutionStatus.Succeeded, fetched.ExecutionStatus);
        Assert.Equal("{\"action\":\"terminate\"}", fetched.ActionPayload);

        // Audit trail should exist
        var history = await store2.GetApprovalHistoryAsync("tenant-lc", "lifecycle.full", 50);
        Assert.Single(history);
        Assert.Equal(ApprovalStatus.Approved, history[0].Outcome);
    }

    // ── Test 5: Execution failure persists with error message ───────────

    [Fact]
    public async Task ExecutionFailure_ErrorMessagePersistedAcrossRestart()
    {
        var store1 = _fixture.CreateStore();

        await store1.CreateApprovalPolicyAsync(
            "exec.fail", "Execution failure test", "Admin", false);

        var gate = await store1.RequestApprovalAsync(
            "exec.fail", "res-fail", "tenant-ef", "ivan", "Test execution failure");

        await store1.ReviewApprovalAsync(
            gate.Id, "tenant-ef", "admin-judy", "Admin", true, "Go ahead");

        // Record failure
        var failed = await store1.RecordExecutionResultAsync(
            gate.Id, GateExecutionStatus.Failed, "Downstream service returned HTTP 503 Service Unavailable");

        Assert.Equal(GateExecutionStatus.Failed, failed.ExecutionStatus);
        Assert.Equal("Downstream service returned HTTP 503 Service Unavailable", failed.ExecutionError);

        // Verify via new store instance
        var store2 = _fixture.CreateStore();
        var fetched = await store2.GetApprovalAsync(gate.Id, "tenant-ef");

        Assert.NotNull(fetched);
        Assert.Equal(GateExecutionStatus.Failed, fetched.ExecutionStatus);
        Assert.Equal("Downstream service returned HTTP 503 Service Unavailable", fetched.ExecutionError);
        Assert.NotNull(fetched.ExecutedAtUtc);
    }

    // ── Test 6: Concurrent multi-tenant governance isolation ────────────

    [Fact]
    public async Task ConcurrentTenants_IsolatedGovernanceOperations()
    {
        var store = _fixture.CreateStore();

        // Create gates for multiple tenants simultaneously
        var tasks = Enumerable.Range(0, 5).Select(i =>
            store.RequestApprovalAsync(
                "multi.tenant", $"res-mt-{i}", $"tenant-mt-{i}",
                $"user-{i}", $"Request from tenant {i}"));

        var gates = await Task.WhenAll(tasks);

        // Each tenant should see exactly one pending gate
        for (int i = 0; i < 5; i++)
        {
            var pending = await store.ListPendingApprovalsAsync($"tenant-mt-{i}");
            Assert.Single(pending);
            Assert.Equal(gates[i].Id, pending[0].Id);

            // Cross-tenant access should fail
            var crossAccess = await store.GetApprovalAsync(gates[i].Id, $"tenant-mt-{(i + 1) % 5}");
            Assert.Null(crossAccess);
        }
    }

    // ── Test 7: Policy listing after restart ────────────────────────────

    [Fact]
    public async Task PolicyPersistence_MultiplePolicies_SurviveRestart()
    {
        var store1 = _fixture.CreateStore();

        await store1.CreateApprovalPolicyAsync("action.one", "First action", "Admin", true);
        await store1.CreateApprovalPolicyAsync("action.two", "Second action", "Operator", false);
        await store1.CreateApprovalPolicyAsync("action.three", "Third action", "Admin", true);

        // New store instance
        var store2 = _fixture.CreateStore();
        var policies = await store2.ListApprovalPoliciesAsync();

        Assert.True(policies.Count >= 3);
        Assert.Contains(policies, p => p.ActionType == "action.one" && p.RequireSeparationOfDuties);
        Assert.Contains(policies, p => p.ActionType == "action.two" && p.RequiredApproverRole == "Operator");
        Assert.Contains(policies, p => p.ActionType == "action.three");

        // RequiresApproval works after restart
        Assert.True(await store2.RequiresApprovalAsync("action.one"));
        Assert.True(await store2.RequiresApprovalAsync("action.two"));
        Assert.False(await store2.RequiresApprovalAsync("action.nonexistent"));
    }
}
