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
    public async Task Workflow_FailedStep_DeadLettersAfterRetries()
    {
        var tasks = MakeTasks(2);
        var record = await _engine.CreateWorkflowAsync("fail-wf", "1.0", "t1", "user-a", tasks);

        var result = await _engine.ExecuteAsync(record.Id, FailExecutor);

        // Step-level retry exhausted all 3 attempts → dead-lettered
        Assert.Equal(WorkflowExecutionStatus.DeadLettered, result.Status);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("Test failure", result.FailureReason);
        Assert.Equal(StepExecutionStatus.Failed, result.Steps[0].Status);
        Assert.Equal(3, result.Steps[0].AttemptCount); // All retries used
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
        Assert.Equal(3, stored.Steps[0].AttemptCount); // All retries exhausted
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
        await _engine.ExecuteAsync(record.Id, FailExecutor); // Exhausts retries → dead-lettered

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
        await store1.FlushPendingAsync(); // Deterministically wait for fire-and-forget flush
        store1.Dispose();

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

    // ── Step-Level Retry With Backoff Tests ─────────────────────────

    [Fact]
    public async Task StepRetry_SucceedsAfterTransientFailures()
    {
        int callCount = 0;
        Task<ExecutionResult> TransientThenSuccess(CoreTask task, CancellationToken ct)
        {
            callCount++;
            bool success = callCount >= 2; // Fail first attempt, succeed on second
            return Task.FromResult(new ExecutionResult(
                task.Id, success, success ? "OK" : "Transient error",
                success ? new Dictionary<string, string> { ["result"] = "recovered" } : new Dictionary<string, string>(),
                Array.Empty<string>(),
                success ? Array.Empty<string>() : new[] { "Transient failure" },
                DateTimeOffset.UtcNow));
        }

        var tasks = MakeTasks(1);
        var record = await _engine.CreateWorkflowAsync("retry-wf", "1.0", "t1", "u", tasks);

        var result = await _engine.ExecuteAsync(record.Id, TransientThenSuccess);

        Assert.Equal(WorkflowExecutionStatus.Succeeded, result.Status);
        Assert.Equal(2, result.Steps[0].AttemptCount); // Took 2 attempts
        Assert.Equal("recovered", result.Steps[0].Outputs["result"]);

        // Verify backoff events were recorded
        var eventTypes = result.Events.Select(e => e.EventType).ToList();
        Assert.Contains("step.retry_backoff", eventTypes);
    }

    [Fact]
    public async Task StepRetry_ExhaustsAllAttempts_DeadLetters()
    {
        var tasks = MakeTasks(1);
        var record = await _engine.CreateWorkflowAsync("exhaust-wf", "1.0", "t1", "u", tasks);

        var result = await _engine.ExecuteAsync(record.Id, FailExecutor);

        Assert.Equal(WorkflowExecutionStatus.DeadLettered, result.Status);
        Assert.Equal(3, result.Steps[0].AttemptCount); // MaxRetries = 3

        // Verify each attempt was recorded
        var startEvents = result.Events.Where(e => e.EventType == "step.started").ToList();
        Assert.Equal(3, startEvents.Count);
    }

    [Fact]
    public async Task StepRetry_ExceptionRetriedWithBackoff()
    {
        int callCount = 0;
        Task<ExecutionResult> ThrowThenSucceed(CoreTask task, CancellationToken ct)
        {
            callCount++;
            if (callCount < 3)
                throw new InvalidOperationException("Transient exception");

            return Task.FromResult(new ExecutionResult(
                task.Id, true, "OK",
                new Dictionary<string, string>(),
                Array.Empty<string>(), Array.Empty<string>(),
                DateTimeOffset.UtcNow));
        }

        var tasks = MakeTasks(1);
        var record = await _engine.CreateWorkflowAsync("exc-retry-wf", "1.0", "t1", "u", tasks);

        var result = await _engine.ExecuteAsync(record.Id, ThrowThenSucceed);

        Assert.Equal(WorkflowExecutionStatus.Succeeded, result.Status);
        Assert.Equal(3, result.Steps[0].AttemptCount);

        // Verify error events were recorded for the failed attempts
        var errorEvents = result.Events.Where(e => e.EventType == "step.error").ToList();
        Assert.Equal(2, errorEvents.Count);
    }

    // ── Idempotency Guard Tests ──────────────────────────────────────

    [Fact]
    public async Task IdempotencyGuard_SkipsAlreadySucceededSteps()
    {
        var tasks = MakeTasks(3);
        var record = await _engine.CreateWorkflowAsync("idem-wf", "1.0", "t1", "u", tasks);

        var callsByTask = new Dictionary<Guid, int>();
        Task<ExecutionResult> SucceedFirstTwoFailThird(CoreTask task, CancellationToken ct)
        {
            if (!callsByTask.ContainsKey(task.Id))
                callsByTask[task.Id] = 0;
            callsByTask[task.Id]++;

            // Steps 1 and 2 succeed, step 3 always fails
            if (task.Order <= 2)
                return SuccessExecutor(task, ct);
            return FailExecutor(task, ct);
        }

        var result = await _engine.ExecuteAsync(record.Id, SucceedFirstTwoFailThird);

        // Steps 1-2 succeeded, step 3 dead-lettered
        Assert.Equal(WorkflowExecutionStatus.DeadLettered, result.Status);
        Assert.Equal(StepExecutionStatus.Succeeded, result.Steps[0].Status);
        Assert.Equal(StepExecutionStatus.Succeeded, result.Steps[1].Status);
        Assert.Equal(StepExecutionStatus.Failed, result.Steps[2].Status);

        // Now retry — steps 1 and 2 should be skipped via idempotency guard
        callsByTask.Clear();
        var retried = await _engine.RetryWorkflowAsync(record.Id, SuccessExecutor);

        Assert.Equal(WorkflowExecutionStatus.Succeeded, retried.Status);

        // Step 3 was re-executed, steps 1-2 were skipped
        var skipEvents = retried.Events.Where(e => e.EventType == "step.skipped_idempotent").ToList();
        Assert.Empty(skipEvents); // Steps 1-2 keep their succeeded status, only step 3 was reset to pending
        // The key behavior: RetryWorkflowAsync only resets Failed steps, not Succeeded ones
        Assert.Equal(StepExecutionStatus.Succeeded, retried.Steps[0].Status);
        Assert.Equal(StepExecutionStatus.Succeeded, retried.Steps[1].Status);
        Assert.Equal(StepExecutionStatus.Succeeded, retried.Steps[2].Status);
    }

    [Fact]
    public async Task IdempotencyGuard_DirectlySkipsSucceededStep()
    {
        // Manually set a step as succeeded and verify ExecuteAsync skips it
        var tasks = MakeTasks(2);
        var record = await _engine.CreateWorkflowAsync("idem-direct", "1.0", "t1", "u", tasks);

        // Manually mark step 1 as succeeded
        record.Steps[0].Status = StepExecutionStatus.Succeeded;
        record.Steps[0].CompletedAtUtc = DateTimeOffset.UtcNow;
        record.Steps[0].Outputs = new Dictionary<string, string> { ["pre"] = "existing" };
        await _store.UpdateAsync(record);

        // Execute — step 1 should be skipped, step 2 should run
        int executorCalls = 0;
        Task<ExecutionResult> CountingExecutor(CoreTask task, CancellationToken ct)
        {
            executorCalls++;
            return SuccessExecutor(task, ct);
        }

        var result = await _engine.ExecuteAsync(record.Id, CountingExecutor);

        Assert.Equal(WorkflowExecutionStatus.Succeeded, result.Status);
        // Only step 2 should have been called by the executor (step 1 skipped)
        Assert.Equal(1, executorCalls);
        // Step 1 should retain its pre-existing outputs
        Assert.Equal("existing", result.Steps[0].Outputs["pre"]);
    }

    // ── Restart Recovery Survival Tests ───────────────────────────────

    [Fact]
    public async Task RestartRecovery_PartiallyCompleteWorkflow_ResumesFromLastPending()
    {
        var filePath = Path.Combine(_tempDir, "partial-resume.json");
        var opts = Options.Create(new WorkflowRuntimeOptions
        {
            PersistencePath = filePath,
            MaxRetries = 3,
            BaseRetryDelayMs = 10,
            StepTimeoutSeconds = 5,
        });

        // First "process lifetime": create workflow, execute step 1, then "crash"
        var store1 = new DurableWorkflowStore(opts, NullLogger<DurableWorkflowStore>.Instance);
        var engine1 = new DurableWorkflowExecutionEngine(
            new StubWorkflowExecutionEngine(), store1, opts,
            NullLogger<DurableWorkflowExecutionEngine>.Instance);

        var tasks = MakeTasks(3);
        var record = await engine1.CreateWorkflowAsync("partial-wf", "1.0", "t1", "u", tasks);

        // Manually simulate partial completion: step 1 succeeded, step 2 running (interrupted)
        record.Steps[0].Status = StepExecutionStatus.Succeeded;
        record.Steps[0].CompletedAtUtc = DateTimeOffset.UtcNow;
        record.Steps[0].AttemptCount = 1;
        record.Steps[1].Status = StepExecutionStatus.Failed; // Was running, crashed → mark as failed
        record.Steps[1].AttemptCount = 1;
        record.Status = WorkflowExecutionStatus.Running;
        record.StartedAtUtc = DateTimeOffset.UtcNow;
        await store1.UpdateAsync(record);
        await store1.FlushPendingAsync(); // Deterministically wait for fire-and-forget flush
        store1.Dispose();

        // Second "process lifetime": new store, resume
        var store2 = new DurableWorkflowStore(opts, NullLogger<DurableWorkflowStore>.Instance);
        var engine2 = new DurableWorkflowExecutionEngine(
            new StubWorkflowExecutionEngine(), store2, opts,
            NullLogger<DurableWorkflowExecutionEngine>.Instance);

        int executorCalls = 0;
        Task<ExecutionResult> CountingSuccess(CoreTask task, CancellationToken ct)
        {
            executorCalls++;
            return SuccessExecutor(task, ct);
        }

        var resumed = await engine2.ResumeAfterRestartAsync(CountingSuccess);

        Assert.Single(resumed);
        Assert.Equal(WorkflowExecutionStatus.Succeeded, resumed[0].Status);
        // Step 1 was already succeeded → skipped by idempotency guard or filter
        // Steps 2 and 3 should have been executed
        Assert.Equal(2, executorCalls);
        Assert.Contains(resumed[0].Events, e => e.EventType == "workflow.resumed_after_restart");

        store2.Dispose();
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
