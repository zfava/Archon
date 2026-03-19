# Flaky Test Remediation

## DurableWorkflowTests — Fire-and-Forget Flush Race Condition

### Root Cause

`DurableWorkflowStore.CreateAsync` and `UpdateAsync` use fire-and-forget
flushing (`_ = FlushAsync()`). Two persistence-survival tests depended on
`Task.Delay(100)` / `Task.Delay(200)` to hope the background flush finished
before disposing the store and reloading from disk. Under CI load the delay
was sometimes insufficient, causing the second store instance to read stale
or missing data.

**Affected tests:**
- `Store_SurvivesRestart` — 100 ms delay after `CreateAsync`
- `RestartRecovery_PartiallyCompleteWorkflow_ResumesFromLastPending` — 200 ms
  delay after `UpdateAsync`

### Fix

Added `DurableWorkflowStore.FlushPendingAsync()`, a zero-cost synchronization
method that acquires and immediately releases the internal write semaphore.
Because `FlushAsync` holds the semaphore for the duration of the file write,
awaiting `FlushPendingAsync()` guarantees the in-flight flush has completed.

Tests now call `await store.FlushPendingAsync()` instead of `Task.Delay(…)`,
making them fully deterministic.

### Why This Is Safe

- `FlushPendingAsync` is on the concrete class, not the `IWorkflowExecutionStore`
  interface — no contract change for other implementations.
- The method is also useful in production for graceful-shutdown scenarios where
  you need to guarantee pending writes are persisted before process exit.
- No assertion strength was reduced; the behavioral intent of both tests is
  preserved exactly.

### Remaining Flaky-Risk Areas

None identified in the current test suite. All other `DurableWorkflowTests`
operate synchronously on in-memory state and do not depend on flush timing.
