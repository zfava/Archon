using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.Workflow;
using ArchonAI.MultiTenant;
using ArchonAI.WorkflowRuntime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Enterprise.Tests.EndToEnd;

/// <summary>
/// End-to-end tests exercising cross-cutting enterprise scenarios:
/// workflow + governance + RBAC + audit + tenant isolation together.
/// </summary>
public sealed class WorkflowGovernanceE2ETests : IDisposable
{
    private readonly string _tempDir;
    private readonly DurableWorkflowStore _store;
    private readonly DurableWorkflowExecutionEngine _engine;
    private readonly GovernanceService _gov;
    private readonly RbacService _rbac;
    private readonly AuditLogService _audit;

    public WorkflowGovernanceE2ETests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"archon-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var opts = Options.Create(new WorkflowRuntimeOptions
        {
            PersistencePath = Path.Combine(_tempDir, "workflows.json"),
            MaxRetries = 2,
            BaseRetryDelayMs = 10,
            StepTimeoutSeconds = 5,
        });

        _store = new DurableWorkflowStore(opts, NullLogger<DurableWorkflowStore>.Instance);
        _engine = new DurableWorkflowExecutionEngine(
            new StubWorkflowExecutionEngine(), _store, opts,
            NullLogger<DurableWorkflowExecutionEngine>.Instance);

        _gov = new GovernanceService();
        _rbac = new RbacService(Substitute.For<IEventBus>());
        _audit = new AuditLogService(NullLogger<AuditLogService>.Instance);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    private static IReadOnlyList<CoreTask> MakeTasks(int count) =>
        Enumerable.Range(1, count).Select(i => new CoreTask(
            Guid.NewGuid(), Guid.NewGuid(), i, $"Step-{i}", $"Test step {i}",
            "test", new Dictionary<string, string> { ["k"] = $"v{i}" },
            DateTimeOffset.UtcNow, null, null)).ToList();

    private static Task<ExecutionResult> SuccessExecutor(CoreTask task, CancellationToken ct) =>
        Task.FromResult(new ExecutionResult(task.Id, true, "OK",
            new Dictionary<string, string> { ["result"] = "done" },
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow));

    // ── Workflow + Approval Gate Flow ──────────────────────────────────

    [Fact]
    public async Task WorkflowCancel_RequiresApproval_ThenExecutes()
    {
        // 1. Create and run a workflow
        var tasks = MakeTasks(2);
        var wf = await _engine.CreateWorkflowAsync("cancel-flow", "1.0", "tenant-1", "user-a", tasks);

        // 2. Request cancellation - requires approval
        Assert.True(await _gov.RequiresApprovalAsync("workflow.cancel"));

        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", wf.Id.ToString(), "tenant-1", "user-a", "Need to cancel");
        Assert.Equal(ApprovalStatus.Pending, gate.Status);

        // 3. Different user with Admin role approves
        var approved = await _gov.ReviewApprovalAsync(
            gate.Id, "tenant-1", "user-b", "Admin", true, "Approved");
        Assert.Equal(ApprovalStatus.Approved, approved.Status);

        // 4. Execute the cancellation
        var cancelled = await _engine.CancelWorkflowAsync(wf.Id, "Approved by governance");
        Assert.Equal(WorkflowExecutionStatus.Cancelled, cancelled.Status);

        // 5. Record execution result
        var executed = await _gov.RecordExecutionResultAsync(
            gate.Id, GateExecutionStatus.Succeeded, null);
        Assert.Equal(GateExecutionStatus.Succeeded, executed.ExecutionStatus);
    }

    // ── Workflow + RBAC + Audit ────────────────────────────────────────

    [Fact]
    public async Task AdminUser_CanExecuteWorkflow_AuditRecorded()
    {
        // 1. Assign admin role
        var roles = await _rbac.GetRolesAsync();
        var adminRole = roles.First(r => r.Name == "Admin");
        await _rbac.AssignRoleAsync("admin-user", "user", adminRole.Id, "system");

        // 2. Verify access
        var access = await _rbac.EvaluateAccessAsync("admin-user", "workflows", "workflows:execute");
        Assert.True(access.IsAllowed);

        // 3. Create and execute workflow
        var tasks = MakeTasks(3);
        var wf = await _engine.CreateWorkflowAsync("admin-wf", "1.0", "tenant-1", "admin-user", tasks);

        // 4. Record audit entry
        await _audit.RecordAsync(
            "workflow.created", "workflow", "WorkflowEngine",
            "admin-user", "user", "create", "workflow", wf.Id.ToString(),
            "Admin created workflow");

        var result = await _engine.ExecuteAsync(wf.Id, SuccessExecutor);
        Assert.Equal(WorkflowExecutionStatus.Succeeded, result.Status);

        // 5. Record completion audit
        await _audit.RecordAsync(
            "workflow.completed", "workflow", "WorkflowEngine",
            "admin-user", "user", "execute", "workflow", wf.Id.ToString(),
            "Workflow completed successfully");

        // 6. Verify audit trail
        var auditResult = await _audit.QueryAsync(category: "workflow");
        Assert.Equal(2, auditResult.Entries.Count);

        // 7. Verify integrity chain
        Assert.True(await _audit.VerifyIntegrityAsync());
    }

    [Fact]
    public async Task ViewerUser_DeniedWorkflowExecution()
    {
        var roles = await _rbac.GetRolesAsync();
        var viewerRole = roles.First(r => r.Name == "Viewer");
        await _rbac.AssignRoleAsync("viewer-user", "user", viewerRole.Id, "system");

        var access = await _rbac.EvaluateAccessAsync("viewer-user", "workflows", "workflows:execute");

        Assert.False(access.IsAllowed);
    }

    // ── Cross-Tenant Workflow Isolation ────────────────────────────────

    [Fact]
    public async Task WorkflowExecution_TenantIsolated()
    {
        var tasks = MakeTasks(1);

        var wfA = await _engine.CreateWorkflowAsync("wf-a", "1.0", "tenant-A", "user-a", tasks);
        var wfB = await _engine.CreateWorkflowAsync("wf-b", "1.0", "tenant-B", "user-b", tasks);

        var tenantA = await _store.ListAsync(tenantId: "tenant-A");
        var tenantB = await _store.ListAsync(tenantId: "tenant-B");

        Assert.Single(tenantA);
        Assert.Single(tenantB);
        Assert.Equal("tenant-A", tenantA[0].TenantId);
        Assert.Equal("tenant-B", tenantB[0].TenantId);
    }

    // ── Workflow Retry + Governance ────────────────────────────────────

    [Fact]
    public async Task FailedWorkflow_RetrySucceeds_AuditTrailComplete()
    {
        var tasks = MakeTasks(1);
        var wf = await _engine.CreateWorkflowAsync("retry-wf", "1.0", "t1", "user-a", tasks);

        // Fail it
        var failed = await _engine.ExecuteAsync(wf.Id,
            (t, ct) => Task.FromResult(new ExecutionResult(
                t.Id, false, "Fail", new Dictionary<string, string>(),
                Array.Empty<string>(), new[] { "transient error" }, DateTimeOffset.UtcNow)));

        Assert.Equal(WorkflowExecutionStatus.DeadLettered, failed.Status);

        // Audit the failure
        await _audit.RecordAsync("workflow.failed", "workflow", "Engine",
            "user-a", "user", "execute", "workflow", wf.Id.ToString(),
            "Workflow dead-lettered");

        // Retry succeeds
        var retried = await _engine.RetryWorkflowAsync(wf.Id, SuccessExecutor);
        Assert.Equal(WorkflowExecutionStatus.Succeeded, retried.Status);

        // Audit the retry
        await _audit.RecordAsync("workflow.retried", "workflow", "Engine",
            "user-a", "user", "retry", "workflow", wf.Id.ToString(),
            "Workflow retried successfully");

        // Verify full audit trail
        var trail = await _audit.QueryAsync(category: "workflow");
        Assert.Equal(2, trail.Entries.Count);
        Assert.True(await _audit.VerifyIntegrityAsync());
    }

    // ── Approval Denied → Workflow Stays Running ──────────────────────

    [Fact]
    public async Task ApprovalDenied_WorkflowNotCancelled()
    {
        var tasks = MakeTasks(2);
        var wf = await _engine.CreateWorkflowAsync("keep-running", "1.0", "t1", "user-a", tasks);

        var gate = await _gov.RequestApprovalAsync(
            "workflow.cancel", wf.Id.ToString(), "t1", "user-a", "Want to cancel");

        // Deny the cancellation
        var denied = await _gov.ReviewApprovalAsync(
            gate.Id, "t1", "user-b", "Admin", false, "Keep running");

        Assert.Equal(ApprovalStatus.Denied, denied.Status);

        // Workflow should still be executable
        var result = await _engine.ExecuteAsync(wf.Id, SuccessExecutor);
        Assert.Equal(WorkflowExecutionStatus.Succeeded, result.Status);
    }

    // ── Stub ──────────────────────────────────────────────────────────

    private sealed class StubWorkflowExecutionEngine : IWorkflowExecutionEngine
    {
        public global::System.Threading.Tasks.Task InitializeWorkflowAsync(Guid workflowId, CancellationToken ct = default) =>
            global::System.Threading.Tasks.Task.CompletedTask;
        public global::System.Threading.Tasks.Task CreateTaskQueueAsync(Guid workflowId, IReadOnlyList<CoreTask> tasks, CancellationToken ct = default) =>
            global::System.Threading.Tasks.Task.CompletedTask;
        public global::System.Threading.Tasks.Task<ArchonAI.Core.Models.Scheduler.SchedulePlan> DispatchTasksToSchedulerAsync(Guid workflowId, int maxP, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public global::System.Threading.Tasks.Task UpdateExecutionResultsAsync(Guid workflowId, IReadOnlyList<ExecutionResult> results, CancellationToken ct = default) =>
            global::System.Threading.Tasks.Task.CompletedTask;
        public string GetWorkflowState(Guid workflowId) => "Created";
    }
}
