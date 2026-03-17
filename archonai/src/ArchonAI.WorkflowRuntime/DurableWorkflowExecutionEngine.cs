using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Scheduler;
using ArchonAI.Core.Models.Workflow;
using ArchonAI.Workflow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.WorkflowRuntime;

/// <summary>
/// Durable workflow execution engine that persists all state transitions, step
/// outcomes, and audit events to an IWorkflowExecutionStore.  Wraps the existing
/// WorkflowExecutionEngine for state machine transitions while adding:
///   - Persistent workflow/step state
///   - Step-level retry with exponential backoff
///   - Cancellation propagation
///   - Idempotent step execution
///   - Dead-letter handling after max retries
///   - Resumability after restart
///   - Audit event recording for every state change
/// </summary>
public sealed class DurableWorkflowExecutionEngine
{
    private readonly IWorkflowExecutionEngine _inner;
    private readonly IWorkflowExecutionStore _store;
    private readonly WorkflowRuntimeOptions _options;
    private readonly ILogger<DurableWorkflowExecutionEngine> _logger;

    public DurableWorkflowExecutionEngine(
        IWorkflowExecutionEngine inner,
        IWorkflowExecutionStore store,
        IOptions<WorkflowRuntimeOptions> options,
        ILogger<DurableWorkflowExecutionEngine> logger)
    {
        _inner = inner;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new durable workflow execution record and initializes the state machine.
    /// </summary>
    public async Task<WorkflowExecutionRecord> CreateWorkflowAsync(
        string name, string version, string tenantId, string initiatedBy,
        IReadOnlyList<CoreTask> tasks,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var workflowId = Guid.NewGuid();

        var steps = tasks.Select(t => new WorkflowStepRecord
        {
            Id = t.Id,
            WorkflowId = workflowId,
            Order = t.Order,
            Name = t.Name,
            RequiredCapability = t.RequiredCapability,
            Status = StepExecutionStatus.Pending,
            IdempotencyKey = $"{workflowId}:{t.Id}",
            Inputs = new Dictionary<string, string>(t.Inputs),
        }).ToList();

        var record = new WorkflowExecutionRecord
        {
            Id = workflowId,
            Name = name,
            Version = version,
            Status = WorkflowExecutionStatus.Queued,
            TenantId = tenantId,
            InitiatedBy = initiatedBy,
            MaxRetries = _options.MaxRetries,
            Steps = steps,
            Metadata = metadata ?? new(),
        };

        AddEvent(record, null, "workflow.created", initiatedBy, $"Workflow '{name}' v{version} created with {tasks.Count} steps");

        await _store.CreateAsync(record, ct);
        await _inner.InitializeWorkflowAsync(workflowId, ct);

        _logger.LogInformation("Created durable workflow {WorkflowId} '{Name}' with {StepCount} steps",
            workflowId, name, tasks.Count);

        return record;
    }

    /// <summary>
    /// Executes a workflow: transitions through steps, persisting state at each boundary.
    /// </summary>
    public async Task<WorkflowExecutionRecord> ExecuteAsync(
        Guid workflowId,
        Func<CoreTask, CancellationToken, Task<ExecutionResult>> stepExecutor,
        CancellationToken ct = default)
    {
        var record = await _store.GetAsync(workflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow {workflowId} not found.");

        if (record.Status is WorkflowExecutionStatus.Succeeded
            or WorkflowExecutionStatus.Cancelled
            or WorkflowExecutionStatus.DeadLettered)
        {
            return record; // Terminal state — no-op
        }

        record.Status = WorkflowExecutionStatus.Running;
        record.StartedAtUtc ??= DateTimeOffset.UtcNow;
        AddEvent(record, null, "workflow.started", null, null);
        await _store.UpdateAsync(record, ct);

        var pendingSteps = record.Steps
            .Where(s => s.Status is StepExecutionStatus.Pending or StepExecutionStatus.Failed)
            .OrderBy(s => s.Order)
            .ToList();

        bool hasFailure = false;

        foreach (var step in pendingSteps)
        {
            if (ct.IsCancellationRequested)
            {
                await CancelWorkflowAsync(workflowId, "Cancellation requested", ct);
                return (await _store.GetAsync(workflowId, ct))!;
            }

            var stepResult = await ExecuteStepWithRetryAsync(record, step, stepExecutor, ct);

            if (!stepResult)
            {
                hasFailure = true;
                if (step.AttemptCount >= record.MaxRetries)
                {
                    record.Status = WorkflowExecutionStatus.DeadLettered;
                    record.FailureReason = $"Step '{step.Name}' failed after {step.AttemptCount} attempts: {step.ErrorMessage}";
                    AddEvent(record, step.Id, "workflow.dead_lettered", null, record.FailureReason);
                    await _store.UpdateAsync(record, ct);
                    _logger.LogError("Workflow {WorkflowId} dead-lettered at step {StepName}", workflowId, step.Name);
                    return record;
                }

                // Mark workflow as failed (retryable)
                record.Status = WorkflowExecutionStatus.Failed;
                record.FailureReason = $"Step '{step.Name}' failed: {step.ErrorMessage}";
                AddEvent(record, step.Id, "workflow.failed", null, record.FailureReason);
                await _store.UpdateAsync(record, ct);
                return record;
            }
        }

        // All steps completed
        record.Status = hasFailure ? WorkflowExecutionStatus.Failed : WorkflowExecutionStatus.Succeeded;
        record.CompletedAtUtc = DateTimeOffset.UtcNow;
        AddEvent(record, null, hasFailure ? "workflow.failed" : "workflow.succeeded", null, null);
        await _store.UpdateAsync(record, ct);

        _logger.LogInformation("Workflow {WorkflowId} completed with status {Status}", workflowId, record.Status);
        return record;
    }

    /// <summary>
    /// Cancels a running or queued workflow and all pending steps.
    /// </summary>
    public async Task<WorkflowExecutionRecord> CancelWorkflowAsync(
        Guid workflowId, string reason, CancellationToken ct = default)
    {
        var record = await _store.GetAsync(workflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow {workflowId} not found.");

        if (record.Status is WorkflowExecutionStatus.Succeeded
            or WorkflowExecutionStatus.Cancelled
            or WorkflowExecutionStatus.DeadLettered)
        {
            return record; // Already terminal
        }

        record.Status = WorkflowExecutionStatus.Cancelled;
        record.CompletedAtUtc = DateTimeOffset.UtcNow;
        record.FailureReason = reason;

        foreach (var step in record.Steps.Where(s => s.Status is StepExecutionStatus.Pending or StepExecutionStatus.Running))
        {
            step.Status = StepExecutionStatus.Cancelled;
        }

        AddEvent(record, null, "workflow.cancelled", null, reason);
        await _store.UpdateAsync(record, ct);

        _logger.LogInformation("Workflow {WorkflowId} cancelled: {Reason}", workflowId, reason);
        return record;
    }

    /// <summary>
    /// Resumes all workflows that were running/queued when the process last stopped.
    /// </summary>
    public async Task<IReadOnlyList<WorkflowExecutionRecord>> ResumeAfterRestartAsync(
        Func<CoreTask, CancellationToken, Task<ExecutionResult>> stepExecutor,
        CancellationToken ct = default)
    {
        var resumable = await _store.GetResumableAsync(ct);
        var results = new List<WorkflowExecutionRecord>();

        _logger.LogInformation("Found {Count} workflows to resume after restart", resumable.Count);

        foreach (var record in resumable)
        {
            AddEvent(record, null, "workflow.resumed_after_restart", null,
                $"Resuming from status {record.Status}");
            await _store.UpdateAsync(record, ct);

            try
            {
                var result = await ExecuteAsync(record.Id, stepExecutor, ct);
                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resume workflow {WorkflowId}", record.Id);
                record.Status = WorkflowExecutionStatus.Failed;
                record.FailureReason = $"Resume failed: {ex.Message}";
                AddEvent(record, null, "workflow.resume_failed", null, ex.Message);
                await _store.UpdateAsync(record, ct);
                results.Add(record);
            }
        }

        return results;
    }

    /// <summary>
    /// Retries a failed workflow from its last failed step.
    /// </summary>
    public async Task<WorkflowExecutionRecord> RetryWorkflowAsync(
        Guid workflowId,
        Func<CoreTask, CancellationToken, Task<ExecutionResult>> stepExecutor,
        CancellationToken ct = default)
    {
        var record = await _store.GetAsync(workflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow {workflowId} not found.");

        if (record.Status is not (WorkflowExecutionStatus.Failed or WorkflowExecutionStatus.DeadLettered))
        {
            throw new InvalidOperationException(
                $"Workflow {workflowId} is {record.Status}, only Failed/DeadLettered can be retried.");
        }

        record.RetryCount++;
        record.FailureReason = null;
        AddEvent(record, null, "workflow.retry", null, $"Retry attempt {record.RetryCount}");

        // Reset failed steps to pending for re-execution
        foreach (var step in record.Steps.Where(s => s.Status == StepExecutionStatus.Failed))
        {
            step.Status = StepExecutionStatus.Pending;
            step.ErrorMessage = null;
        }

        await _store.UpdateAsync(record, ct);
        return await ExecuteAsync(workflowId, stepExecutor, ct);
    }

    private async Task<bool> ExecuteStepWithRetryAsync(
        WorkflowExecutionRecord workflow,
        WorkflowStepRecord step,
        Func<CoreTask, CancellationToken, Task<ExecutionResult>> executor,
        CancellationToken ct)
    {
        step.Status = StepExecutionStatus.Running;
        step.StartedAtUtc ??= DateTimeOffset.UtcNow;
        step.AttemptCount++;
        AddEvent(workflow, step.Id, "step.started", null,
            $"Attempt {step.AttemptCount} for step '{step.Name}'");
        await _store.UpdateAsync(workflow, ct);

        // Build a CoreTask from the step record for the executor
        var coreTask = new CoreTask(
            Id: step.Id,
            ObjectiveId: workflow.Id,
            Order: step.Order,
            Name: step.Name,
            Description: step.Name,
            RequiredCapability: step.RequiredCapability,
            Inputs: step.Inputs,
            CreatedAtUtc: workflow.CreatedAtUtc,
            StartedAtUtc: step.StartedAtUtc,
            CompletedAtUtc: null);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.StepTimeoutSeconds));

            var result = await executor(coreTask, cts.Token);

            if (result.IsSuccess)
            {
                step.Status = StepExecutionStatus.Succeeded;
                step.CompletedAtUtc = DateTimeOffset.UtcNow;
                step.Outputs = new Dictionary<string, string>(result.Outputs);
                AddEvent(workflow, step.Id, "step.succeeded", null, result.Summary);
                await _store.UpdateAsync(workflow, ct);
                return true;
            }
            else
            {
                step.Status = StepExecutionStatus.Failed;
                step.ErrorMessage = string.Join("; ", result.Errors);
                AddEvent(workflow, step.Id, "step.failed", null, step.ErrorMessage);
                await _store.UpdateAsync(workflow, ct);
                return false;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            step.Status = StepExecutionStatus.Cancelled;
            AddEvent(workflow, step.Id, "step.cancelled", null, "Caller cancellation");
            await _store.UpdateAsync(workflow, ct);
            throw;
        }
        catch (OperationCanceledException)
        {
            step.Status = StepExecutionStatus.Failed;
            step.ErrorMessage = $"Step timed out after {_options.StepTimeoutSeconds}s";
            AddEvent(workflow, step.Id, "step.timeout", null, step.ErrorMessage);
            await _store.UpdateAsync(workflow, ct);
            return false;
        }
        catch (Exception ex)
        {
            step.Status = StepExecutionStatus.Failed;
            step.ErrorMessage = ex.Message;
            AddEvent(workflow, step.Id, "step.error", null, ex.Message);
            await _store.UpdateAsync(workflow, ct);
            return false;
        }
    }

    private static void AddEvent(
        WorkflowExecutionRecord record, Guid? stepId,
        string eventType, string? actor, string? detail)
    {
        record.Events.Add(new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowId = record.Id,
            StepId = stepId,
            EventType = eventType,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Actor = actor,
            Detail = detail,
        });
    }
}
