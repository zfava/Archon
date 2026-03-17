using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Workflow;
using ArchonAI.WorkflowRuntime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Tests;

/// <summary>
/// Integration tests for the durable workflow execution engine:
/// - Full lifecycle (create → execute → succeed/fail)
/// - Step-level state transitions
/// - Retry and dead-letter behavior
/// - Cancellation propagation
/// - Persistence survival (file-backed store)
/// - Resumability after restart simulation
/// - Idempotent step tracking
/// - Audit event recording
/// </summary>
public class DurableWorkflowTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DurableWorkflowStore _store;
    private readonly DurableWorkflowExecutionEngine _engine;

    public DurableWorkflowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"archon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var opts = Options.Create(new WorkflowRuntimeOptions
        {
            PersistencePath = Path.Combine(_tempDir, "workflows.json"),
            MaxRetries = 3,
            BaseRetryDelayMs = 10, // Fast for tests
            StepTimeoutSeconds = 5,
        });

        _store = new DurableWorkflowStore(opts, NullLogger<DurableWorkflowStore>.Instance);

        // DurableWorkflowExecutionEngine needs IWorkflowExecutionEngine - use a minimal stub
        var innerEngine = new StubWorkflowExecutionEngine();

        _engine = new DurableWorkflowExecutionEngine(
            innerEngine, _store, opts,
            NullLogger<DurableWorkflowExecutionEngine>.Instance);
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* Best-effort temp cleanup */ }
    }

    private static IReadOnlyList<CoreTask> MakeTasks(int count) =>
        Enumerable.Range(1, count).Select(i => new CoreTask(
            Id: Guid.NewGuid(),
            ObjectiveId: Guid.NewGuid(),
            Order: i,
            Name: $"Step-{i}",
            Description: $"Test step {i}",
            RequiredCapability: "test",
            Inputs: new Dictionary<string, string> { ["key"] = $"value-{i}" },
            CreatedAtUtc: DateTimeOffset.UtcNow,
            StartedAtUtc: null,
            CompletedAtUtc: null
        )).ToList();

    private static Task<ExecutionResult> SuccessExecutor(CoreTask task, CancellationToken ct) =>
        Task.FromResult(new ExecutionResult(
            task.Id, true, "OK",
            new Dictionary<string, string> { ["result"] = "done" },
            Array.Empty<string>(), Array.Empty<string>(),
            DateTimeOffset.UtcNow));

    private static Task<ExecutionResult> FailExecutor(CoreTask task, CancellationToken ct) =>
        Task.FromResult(new ExecutionResult(
            task.Id, false, "Failed",
            new Dictionary<string, string>(),
            Array.Empty<string>(), new[] { "Test failure" },
            DateTimeOffset.UtcNow));

    // ── Full Lifecycle Tests ─────────────────────────────────────────

    [Fact]
    public async Task Workflow_Create_Execute_Succeed()
    {
        var tasks = MakeTasks(3);
        var record = await _engine.CreateWorkflowAsync("test-wf", "1.0", "t1", "user-a", tasks);

        Assert.Equal(WorkflowExecutionStatus.Queued, record.Status);
        Assert.Equal(3, record.Steps.Count);
        Assert.All(record.Steps, s => Assert.Equal(StepExecutionStatus.Pending, s.Status));

        var result = await _engine.ExecuteAsync(record.Id, SuccessExecutor);

        Assert.Equal(WorkflowExecutionStatus.Succeeded, result.Status);
        Assert.NotNull(result.CompletedAtUtc);
        Assert.All(result.Steps, s => Assert.Equal(StepExecutionStatus.Succeeded, s.Status));
        Assert.All(result.Steps, s => Assert.NotNull(s.CompletedAtUtc));
    }

    [Fact]
    public async Task Workflow_FailedStep_MarksWorkflowFailed()
    {
        var tasks = MakeTasks(2);
        var record = await _engine.CreateWorkflowAsync("fail-wf", "1.0", "t1", "user-a", tasks);

        var result = await _engine.ExecuteAsync(record.Id, FailExecutor);

        Assert.Equal(WorkflowExecutionStatus.Failed, result.Status);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("Test failure", result.FailureReason);
        // First step failed, second never ran
        Assert.Equal(StepExecutionStatus.Failed, result.Steps[0].Status);
        Assert.Equal(StepExecutionStatus.Pending, result.Steps[1].Status);
    }

    // ── Step-Level State Tracking ────────────────────────────────────

    [Fact]
    public async Task Steps_TrackAttemptCount()
    {
        var tasks = MakeTasks(1);
        var record = await _engine.CreateWorkflowAsync("wf", "1.0", "t1", "u", tasks);

        await _engine.ExecuteAsync(record.Id, SuccessExecutor);

        var stored = await _store.GetAsync(record.Id);
        Assert.Equal(1, stored!.Steps[0].AttemptCount);
    }

    [Fact]
    public async Task Steps_CaptureOutputsOnSuccess()
    {
        var tasks = MakeTasks(1);
        var record = await _engine.CreateWorkflowAsync("wf", "1.0", "t1", "u", tasks);

        await _engine.ExecuteAsync(record.Id, SuccessExecutor);

        var stored = await _store.GetAsync(record.Id);
        Assert.Equal("done", stored!.Steps[0].Outputs["result"]);
    }

    [Fact]
    public async Task Steps_CaptureErrorOnFailure()
    {
        var tasks = MakeTasks(1);
        var record = await _engine.CreateWorkflowAsync("wf", "1.0", "t1", "u", tasks);

        await _engine.ExecuteAsync(record.Id, FailExecutor);

        var stored = await _store.GetAsync(record.Id);
        Assert.Contains("Test failure", stored!.Steps[0].ErrorMessage);
    }

    // ── Dead-Letter Tests ────────────────────────────────────────────

    [Fact]
    public async Task Workflow_DeadLettered_AfterMaxRetries()
    {
        var opts = Options.Create(new WorkflowRuntimeOptions
        {
            PersistencePath = Path.Combine(_tempDir, "dl-test.json"),
            MaxRetries = 1, // Only 1 attempt
        });
        var store = new DurableWorkflowStore(opts, NullLogger<DurableWorkflowStore>.Instance);
        var engine = new DurableWorkflowExecutionEngine(
            new StubWorkflowExecutionEngine(), store, opts,
            NullLogger<DurableWorkflowExecutionEngine>.Instance);

        var tasks = MakeTasks(1);
        var record = await engine.CreateWorkflowAsync("dl-wf", "1.0", "t1", "u", tasks);
        await engine.ExecuteAsync(record.Id, FailExecutor);

        var stored = await store.GetAsync(record.Id);
        Assert.Equal(WorkflowExecutionStatus.DeadLettered, stored!.Status);
        Assert.Contains("workflow.dead_lettered", stored.Events.Select(e => e.EventType));

        store.Dispose();
    }

    // ── Cancellation Tests ───────────────────────────────────────────

    [Fact]
    public async Task CancelWorkflow_SetsCancelledStatus()
    {
        var tasks = MakeTasks(3);
        var record = await _engine.CreateWorkflowAsync("wf", "1.0", "t1", "u", tasks);

        var cancelled = await _engine.CancelWorkflowAsync(record.Id, "User requested");

        Assert.Equal(WorkflowExecutionStatus.Cancelled, cancelled.Status);
        Assert.All(cancelled.Steps, s => Assert.Equal(StepExecutionStatus.Cancelled, s.Status));
        Assert.Contains(cancelled.Events, e => e.EventType == "workflow.cancelled");
    }

    [Fact]
    public async Task CancelWorkflow_AlreadyCompleted_NoOp()
    {
        var tasks = MakeTasks(1);
        var record = await _engine.CreateWorkflowAsync("wf", "1.0", "t1", "u", tasks);
        await _engine.ExecuteAsync(record.Id, SuccessExecutor);

        var result = await _engine.CancelWorkflowAsync(record.Id, "Too late");
        Assert.Equal(WorkflowExecutionStatus.Succeeded, result.Status); // Unchanged
    }

    // ── Retry Tests ──────────────────────────────────────────────────

    [Fact]
    public async Task RetryWorkflow_ResetsFailedSteps()
    {
        var tasks = MakeTasks(2);
        var record = await _engine.CreateWorkflowAsync("wf", "1.0", "t1", "u", tasks);
        await _engine.ExecuteAsync(record.Id, FailExecutor); // Fails first step

        // Now retry with success
        var result = await _engine.RetryWorkflowAsync(record.Id, SuccessExecutor);

        Assert.Equal(WorkflowExecutionStatus.Succeeded, result.Status);
        Assert.Equal(1, result.RetryCount);
        Assert.All(result.Steps, s => Assert.Equal(StepExecutionStatus.Succeeded, s.Status));
    }

    [Fact]
    public async Task RetryWorkflow_OnlyRetryableStatuses()
    {
        var tasks = MakeTasks(1);
        var record = await _engine.CreateWorkflowAsync("wf", "1.0", "t1", "u", tasks);
        await _engine.ExecuteAsync(record.Id, SuccessExecutor);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _engine.RetryWorkflowAsync(record.Id, SuccessExecutor));
    }

    // ── Persistence Survival Tests ───────────────────────────────────

    [Fact]
    public async Task Store_SurvivesRestart()
    {
        var filePath = Path.Combine(_tempDir, "restart-test.json");
        var opts = Options.Create(new WorkflowRuntimeOptions { PersistencePath = filePath });

        // Create store and write data
        var store1 = new DurableWorkflowStore(opts, NullLogger<DurableWorkflowStore>.Instance);
        var record = new WorkflowExecutionRecord
        {
            Id = Guid.NewGuid(),
            Name = "persist-test",
            Version = "2.0",
            Status = WorkflowExecutionStatus.Running,
            TenantId = "tenant-1",
            InitiatedBy = "user-a",
        };
        await store1.CreateAsync(record);
        store1.Dispose();

        // Give file time to flush
        await Task.Delay(100);

        // Simulate restart: new store instance loads from disk
        var store2 = new DurableWorkflowStore(opts, NullLogger<DurableWorkflowStore>.Instance);
        var loaded = await store2.GetAsync(record.Id);

        Assert.NotNull(loaded);
        Assert.Equal("persist-test", loaded!.Name);
        Assert.Equal("2.0", loaded.Version);
        Assert.Equal(WorkflowExecutionStatus.Running, loaded.Status);
        Assert.Equal("tenant-1", loaded.TenantId);
        store2.Dispose();
    }

    [Fact]
    public async Task GetResumable_FindsRunningAndQueuedWorkflows()
    {
        var tasks = MakeTasks(1);
        var wf1 = await _engine.CreateWorkflowAsync("queued", "1.0", "t1", "u", tasks);
        var wf2 = await _engine.CreateWorkflowAsync("done", "1.0", "t1", "u", tasks);
        await _engine.ExecuteAsync(wf2.Id, SuccessExecutor);

        var resumable = await _store.GetResumableAsync();

        Assert.Single(resumable);
        Assert.Equal(wf1.Id, resumable[0].Id);
    }

    // ── Resume After Restart Tests ───────────────────────────────────

    [Fact]
    public async Task ResumeAfterRestart_CompletesQueuedWorkflows()
    {
        var tasks = MakeTasks(2);
        var record = await _engine.CreateWorkflowAsync("resume-wf", "1.0", "t1", "u", tasks);
        // Don't execute — simulating restart finding it queued

        var resumed = await _engine.ResumeAfterRestartAsync(SuccessExecutor);

        Assert.Single(resumed);
        Assert.Equal(WorkflowExecutionStatus.Succeeded, resumed[0].Status);
        Assert.Contains(resumed[0].Events, e => e.EventType == "workflow.resumed_after_restart");
    }

    // ── Audit Event Tests ────────────────────────────────────────────

    [Fact]
    public async Task AuditEvents_RecordedForAllTransitions()
    {
        var tasks = MakeTasks(2);
        var record = await _engine.CreateWorkflowAsync("audit-wf", "1.0", "t1", "user-a", tasks);
        await _engine.ExecuteAsync(record.Id, SuccessExecutor);

        var stored = await _store.GetAsync(record.Id);
        var eventTypes = stored!.Events.Select(e => e.EventType).ToList();

        Assert.Contains("workflow.created", eventTypes);
        Assert.Contains("workflow.started", eventTypes);
        Assert.Contains("step.started", eventTypes);
        Assert.Contains("step.succeeded", eventTypes);
        Assert.Contains("workflow.succeeded", eventTypes);
    }

    [Fact]
    public async Task AuditEvents_IncludeActorOnCreate()
    {
        var tasks = MakeTasks(1);
        var record = await _engine.CreateWorkflowAsync("wf", "1.0", "t1", "admin-user", tasks);

        var createEvent = record.Events.First(e => e.EventType == "workflow.created");
        Assert.Equal("admin-user", createEvent.Actor);
    }

    // ── Idempotent Execution Tests ───────────────────────────────────

    [Fact]
    public async Task ExecuteCompletedWorkflow_IsNoop()
    {
        var tasks = MakeTasks(1);
        var record = await _engine.CreateWorkflowAsync("wf", "1.0", "t1", "u", tasks);
        await _engine.ExecuteAsync(record.Id, SuccessExecutor);

        // Execute again — should be no-op
        var result = await _engine.ExecuteAsync(record.Id, FailExecutor);
        Assert.Equal(WorkflowExecutionStatus.Succeeded, result.Status);
    }

    // ── Store Query Tests ────────────────────────────────────────────

    [Fact]
    public async Task ListByTenant_FiltersByTenant()
    {
        var tasks = MakeTasks(1);
        await _engine.CreateWorkflowAsync("wf1", "1.0", "tenant-A", "u", tasks);
        await _engine.CreateWorkflowAsync("wf2", "1.0", "tenant-B", "u", tasks);

        var tenantA = await _store.ListAsync(tenantId: "tenant-A");
        var tenantB = await _store.ListAsync(tenantId: "tenant-B");

        Assert.Single(tenantA);
        Assert.Single(tenantB);
        Assert.Equal("tenant-A", tenantA[0].TenantId);
    }

    [Fact]
    public async Task ListByStatus_FiltersCorrectly()
    {
        var tasks = MakeTasks(1);
        var wf1 = await _engine.CreateWorkflowAsync("wf1", "1.0", "t1", "u", tasks);
        var wf2 = await _engine.CreateWorkflowAsync("wf2", "1.0", "t1", "u", tasks);
        await _engine.ExecuteAsync(wf2.Id, SuccessExecutor);

        var queued = await _store.ListAsync(status: WorkflowExecutionStatus.Queued);
        var succeeded = await _store.ListAsync(status: WorkflowExecutionStatus.Succeeded);

        Assert.Single(queued);
        Assert.Single(succeeded);
    }

    // ── Version Awareness Tests ──────────────────────────────────────

    [Fact]
    public async Task Workflow_TracksVersion()
    {
        var tasks = MakeTasks(1);
        var record = await _engine.CreateWorkflowAsync("versioned-wf", "2.5.1", "t1", "u", tasks);

        Assert.Equal("2.5.1", record.Version);
        var stored = await _store.GetAsync(record.Id);
        Assert.Equal("2.5.1", stored!.Version);
    }

    // ── Minimal stub for IWorkflowExecutionEngine ────────────────────

    private sealed class StubWorkflowExecutionEngine : IWorkflowExecutionEngine
    {
        public global::System.Threading.Tasks.Task InitializeWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
            => global::System.Threading.Tasks.Task.CompletedTask;

        public global::System.Threading.Tasks.Task CreateTaskQueueAsync(Guid workflowId, IReadOnlyList<CoreTask> tasks, CancellationToken cancellationToken = default)
            => global::System.Threading.Tasks.Task.CompletedTask;

        public global::System.Threading.Tasks.Task<ArchonAI.Core.Models.Scheduler.SchedulePlan> DispatchTasksToSchedulerAsync(Guid workflowId, int requestedMaxParallelism, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public global::System.Threading.Tasks.Task UpdateExecutionResultsAsync(Guid workflowId, IReadOnlyList<ExecutionResult> results, CancellationToken cancellationToken = default)
            => global::System.Threading.Tasks.Task.CompletedTask;

        public string GetWorkflowState(Guid workflowId) => "Created";
    }
}
