# Flaky Test Remediation

## DurableWorkflowTests — Fire-and-Forget Flush Race Condition

### Root Cause (Original — Phase 1)

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

### Fix (Phase 1)

Added `DurableWorkflowStore.FlushPendingAsync()`, a synchronization method
that acquires and immediately releases the internal write semaphore. Tests
call `await store.FlushPendingAsync()` instead of `Task.Delay(…)`.

### Residual Race Condition (Phase 2)

The Phase 1 `FlushPendingAsync` implementation had a subtle race:

```csharp
// Phase 1 — RACY under contention
public async Task FlushPendingAsync()
{
    await _writeLock.WaitAsync();   // acquire semaphore
    _writeLock.Release();           // immediately release — no write
}
```

**Two problems:**

1. **Non-FIFO semaphore ordering**: When `FlushAsync` (fire-and-forget) and
   `FlushPendingAsync` both queue on the semaphore, `SemaphoreSlim` does not
   guarantee FIFO wakeup. `FlushPendingAsync` could acquire first, find no
   flush in progress, release, and return — while the actual `FlushAsync`
   hasn't written to disk yet. The caller then disposes the store before the
   data reaches disk.

2. **Silent skip on timeout**: `FlushAsync` uses
   `WaitAsync(TimeSpan.FromSeconds(5))`. If the semaphore isn't acquired
   within 5 seconds (e.g., under CI contention), `FlushAsync` returns
   without writing — silently losing the flush. `FlushPendingAsync` (Phase 1)
   wouldn't detect this because it only waited for semaphore availability,
   not for a write to have occurred.

### Fix (Phase 2 — Deterministic)

`FlushPendingAsync` now performs a real flush instead of just probing the
semaphore:

```csharp
public async Task FlushPendingAsync()
{
    await _writeLock.WaitAsync();   // wait indefinitely (no timeout)
    try
    {
        await WriteStateAsync();    // write current in-memory state to disk
    }
    finally
    {
        _writeLock.Release();
    }
}
```

**Why this eliminates the race:**

- **No timeout**: Unlike the fire-and-forget `FlushAsync` (5-second timeout),
  `FlushPendingAsync` waits indefinitely. It cannot silently skip.
- **Always writes**: Even if `FlushPendingAsync` acquires the semaphore before
  a queued `FlushAsync`, it writes the current state itself. The data is on
  disk when it returns, regardless of what the fire-and-forget task does.
- **Shared write logic**: Both `FlushAsync` and `FlushPendingAsync` use the
  extracted `WriteStateAsync()` method — no code duplication, identical write
  semantics.

The fire-and-forget `FlushAsync` in `CreateAsync`/`UpdateAsync` remains
unchanged (best-effort background persistence). `FlushPendingAsync` is the
deterministic "write barrier" for shutdown and tests.

### Why This Is Safe

- `FlushPendingAsync` is on the concrete class, not the
  `IWorkflowExecutionStore` interface — no contract change for other
  implementations.
- The method is also useful in production for graceful-shutdown scenarios
  where you need to guarantee pending writes are persisted before process exit.
- No assertion strength was reduced; the behavioral intent of both tests is
  preserved exactly.

### Remaining Flaky-Risk Areas

| Area | Risk | Notes |
|------|------|-------|
| `FlushAsync` 5-second timeout | Low | Fire-and-forget flushes can still be skipped under extreme contention, but this only affects background persistence — `FlushPendingAsync` compensates by doing its own write. |
| Other `DurableWorkflowStore` tests | None | All non-persistence tests operate on in-memory state via the engine and do not depend on flush timing. |
| Testcontainers-backed tests | None | Use real PostgreSQL, no file-backed store involved. |
