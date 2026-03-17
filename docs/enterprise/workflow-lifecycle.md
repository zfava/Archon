# Workflow Lifecycle

## Status Transitions

```
  Queued ──► Running ──► Succeeded
    │           │
    │           ├──► Failed ──► (Retry) ──► Running
    │           │                  └──► DeadLettered
    │           │
    │           └──► Waiting ──► Running
    │
    └──► Cancelled
```

### Workflow Statuses

| Status | Description | Terminal |
|--------|-------------|---------|
| Queued | Created, not yet started | No |
| Running | Steps are being executed | No |
| Waiting | Paused for external input (approval, etc.) | No |
| Succeeded | All steps completed successfully | Yes |
| Failed | A step failed (retryable) | No* |
| Cancelled | Explicitly cancelled by user/system | Yes |
| DeadLettered | Failed after exhausting all retries | Yes |

*Failed workflows can be retried, transitioning back to Running.

### Step Statuses

| Status | Description |
|--------|-------------|
| Pending | Not yet executed |
| Running | Currently executing |
| Succeeded | Completed successfully |
| Failed | Failed (may retry at workflow level) |
| Skipped | Skipped due to prior step failure or condition |
| Cancelled | Cancelled with parent workflow |

## Execution Flow

1. **Create** → `WorkflowExecutionStatus.Queued`, all steps `Pending`
2. **Execute** → status moves to `Running`, steps execute in order
3. Each step:
   - Idempotency guard: if step already `Succeeded`, skip with `step.skipped_idempotent` event
   - Otherwise, enter step-level retry loop (up to `MaxRetries` attempts)
   - Each attempt: transitions to `Running`, executes with timeout
   - On success: records `Succeeded` with outputs
   - On failure: exponential backoff, then retry
4. If all step-level retries exhausted → workflow `DeadLettered`
5. On all steps success → workflow `Succeeded`

## Cancellation

- API: `POST /api/v1/executions/{id}/cancel`
- Propagates to all pending/running steps
- Sets `StepExecutionStatus.Cancelled` on affected steps
- Terminal: cancelled workflows cannot be retried

## Retry

- API: `POST /api/v1/executions/{id}/retry`
- Only available for `Failed` or `DeadLettered` workflows
- Resets failed steps to `Pending`
- Increments `RetryCount`
- Re-executes from first failed step

## Resume After Restart

On process startup, `ResumeAfterRestartAsync` finds all workflows in
`Running`, `Queued`, or `Waiting` status and re-executes them from their
last pending step. A `workflow.resumed_after_restart` audit event is recorded.

## Audit Trail

Every state transition produces a `WorkflowEvent`:

| Event Type | When |
|------------|------|
| `workflow.created` | Workflow record created |
| `workflow.started` | Execution begins |
| `workflow.succeeded` | All steps complete |
| `workflow.failed` | Step failure |
| `workflow.cancelled` | Explicit cancellation |
| `workflow.dead_lettered` | Max retries exhausted |
| `workflow.retry` | Retry initiated |
| `workflow.resumed_after_restart` | Recovered after process restart |
| `step.started` | Step execution begins |
| `step.succeeded` | Step completes |
| `step.failed` | Step returns failure |
| `step.timeout` | Step exceeds timeout |
| `step.error` | Step throws exception |
| `step.cancelled` | Step cancelled |
| `step.retry_backoff` | Waiting before next retry attempt |
| `step.skipped_idempotent` | Step skipped (already succeeded) |
