# Retry and Recovery Model

## Step-Level Retry

Each workflow step executes within the `DurableWorkflowExecutionEngine` with:
- **Timeout**: Configurable per-step (default 300s via `StepTimeoutSeconds`)
- **Attempt tracking**: `AttemptCount` incremented on each execution
- **Error capture**: `ErrorMessage` recorded on failure

Steps that fail due to timeout receive `step.timeout` event with the duration.

## Workflow-Level Retry

Retry operates at the workflow level, not individual step level:

1. Workflow fails → status becomes `Failed`
2. Operator calls `POST /executions/{id}/retry`
3. Engine resets failed steps to `Pending`
4. Increments `RetryCount`
5. Re-executes from the first pending step

Previously succeeded steps are NOT re-executed (idempotent skip).

## Dead-Letter

When a step's `AttemptCount` reaches `MaxRetries` (default 3), the workflow
transitions to `DeadLettered` — a terminal state indicating the workflow
requires manual investigation.

Dead-lettered workflows can still be retried via the API, but this is an
explicit operator action, not automatic.

## Configuration

```json
{
  "WorkflowRuntime": {
    "MaxRetries": 3,
    "BaseRetryDelayMs": 1000,
    "StepTimeoutSeconds": 300,
    "DefaultMaxParallelism": 16,
    "PersistencePath": "/var/data/workflow-executions.json"
  }
}
```

## Restart Recovery

### On Startup

`DurableWorkflowExecutionEngine.ResumeAfterRestartAsync()` should be called
during application startup. It:

1. Queries `IWorkflowExecutionStore.GetResumableAsync()` for workflows in
   `Running`, `Queued`, or `Waiting` status
2. Records `workflow.resumed_after_restart` audit event
3. Re-executes each from its current pending step

### What Survives Restart

| Data | Survives | Mechanism |
|------|----------|-----------|
| Workflow status | Yes | `IWorkflowExecutionStore` |
| Step status and outputs | Yes | Persisted with workflow record |
| In-flight step execution | No | Re-executed from `Pending` |
| Audit events | Yes | Persisted in workflow record |
| Task queue position | No | Rebuilt from pending steps |

### What Does Not Survive

- Active HTTP connections to AI providers
- In-memory task queues (rebuilt on resume)
- Timer/delay state within a step execution

## Idempotency

Steps include an `IdempotencyKey` (`{workflowId}:{stepId}`) that can be used
by step executors to deduplicate side effects. The engine guarantees:

- Succeeded steps are never re-executed on retry
- Only `Pending` and `Failed` steps are candidates for execution
- Step outputs are preserved through retry cycles

## Remaining Durability Gaps

| Gap | Impact | Mitigation Path |
|-----|--------|-----------------|
| File-based persistence | Single-node only, no HA | Replace `DurableWorkflowStore` with DB-backed `IWorkflowExecutionStore` |
| No distributed locking | Multiple instances could resume same workflow | Add lease/lock mechanism in DB store |
| No automatic resume on startup | Requires explicit call to `ResumeAfterRestartAsync` | Wire into hosted service lifecycle |
| Step timeout is per-attempt, not cumulative | Long-running steps could retry indefinitely | Add cumulative step budget |
