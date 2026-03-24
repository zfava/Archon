# Flaky Test Remediation

## DurableWorkflowTests — Fire-and-Forget Flush Race Condition

**Status: RESOLVED (Phase 3 — Structural Elimination)**

### Root Cause (Original — Phase 1)

`DurableWorkflowStore.CreateAsync` and `UpdateAsync` used fire-and-forget
flushing (`_ = FlushAsync()`). Two persistence-survival tests depended on
`Task.Delay(100)` / `Task.Delay(200)` to hope the background flush finished
before disposing the store and reloading from disk. Under CI load the delay
was sometimes insufficient, causing the second store instance to read stale
or missing data.

**Affected tests:**
- `Store_SurvivesRestart`
- `RestartRecovery_PartiallyCompleteWorkflow_ResumesFromLastPending`

### Phase 1 Fix (Partial)

Added `FlushPendingAsync()` as a synchronization barrier.

### Phase 2 Fix (Partial)

Made `FlushPendingAsync()` perform its own write instead of just probing the
semaphore. This addressed the non-FIFO semaphore ordering issue but left the
fire-and-forget pattern in place — meaning untracked flush tasks could still
race with `Dispose()`, causing `ObjectDisposedException`.

### Phase 3 Fix (Structural — Current)

**Eliminated fire-and-forget entirely.** The race is now structurally impossible:

1. `CreateAsync` and `UpdateAsync` now **await** `FlushAsync()` directly.
   When the method returns, the data is on disk. No background task, no
   timing dependency, no semaphore ordering concern.

2. `FlushAsync` no longer has a 5-second timeout. It waits indefinitely for
   the write lock and propagates exceptions instead of swallowing them.

3. `FlushPendingAsync()` was **removed** — it was a workaround for
   fire-and-forget semantics that no longer exist.

4. `DurableWorkflowStore` now implements **`IAsyncDisposable`**. The
   `DisposeAsync()` method acquires the write lock before disposing the
   semaphore, ensuring no in-flight write is interrupted.

5. Tests use `IAsyncLifetime` and `await store.DisposeAsync()` instead of
   synchronous `Dispose()`.

### Why the Race Is Structurally Eliminated

- **No untracked tasks**: Every write is awaited by the caller. There is no
  background work that can outlive the store instance.
- **No disposal race**: `DisposeAsync` acquires the write lock, so it
  cannot destroy the semaphore while a write is in progress.
- **No silent data loss**: `FlushAsync` propagates exceptions instead of
  catching and logging. Write failures are visible to the caller.
- **Deterministic persistence**: When `CreateAsync`/`UpdateAsync` returns,
  the data is durable. No need for flush barriers, delays, or test-specific
  synchronization.

### Remaining Flaky-Risk Areas

| Area | Risk | Notes |
|------|------|-------|
| `DurableWorkflowStore` flush | **None** | Fire-and-forget eliminated. All writes are synchronous and awaited. |
| Other `DurableWorkflowStore` tests | None | All non-persistence tests operate on in-memory state via the engine. |
| Testcontainers-backed tests | None | Use real PostgreSQL, no file-backed store involved. |
