# Runtime Execution Model

## Overview

ArchonAI uses a durable workflow execution model where all execution state is
persisted to survive process restarts. The system maintains two layers:

1. **State Machine** (`WorkflowEngine`) — deterministic transition logic
2. **Durable Engine** (`DurableWorkflowExecutionEngine`) — persistent execution
   with step-level tracking, retry, cancellation, and audit

## Architecture

```
API Request
      │
      ▼
DurableWorkflowExecutionEngine
      │
      ├── IWorkflowExecutionStore (persistence)
      │     └── DurableWorkflowStore (file-backed, swappable to DB)
      │
      ├── IWorkflowExecutionEngine (state machine transitions)
      │     └── WorkflowExecutionEngine → WorkflowEngine
      │
      └── Step Executor (injected Func<Task, CancellationToken, Task<ExecutionResult>>)
            └── Agent runtime, direct service calls, etc.
```

## Execution Record Structure

```
WorkflowExecutionRecord
├── Id, Name, Version, TenantId, InitiatedBy
├── Status: Queued | Running | Waiting | Succeeded | Failed | Cancelled | DeadLettered
├── Steps[]
│   ├── Id, Order, Name, RequiredCapability
│   ├── Status: Pending | Running | Succeeded | Failed | Skipped | Cancelled
│   ├── AttemptCount, ErrorMessage
│   ├── IdempotencyKey
│   └── Inputs{}, Outputs{}
├── Events[] (audit trail)
│   ├── EventType, OccurredAtUtc, Actor, Detail
│   └── StepId (if step-level)
└── Metadata{}
```

## Persistence

The default `DurableWorkflowStore` uses file-backed JSON persistence:
- In-memory `ConcurrentDictionary` as hot cache
- Asynchronous flush to disk after every state change
- Loads from disk on startup for restart recovery

For production deployments, implement `IWorkflowExecutionStore` with a database backend.

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/executions` | List workflow executions (filter by tenant, status) |
| GET | `/api/v1/executions/{id}` | Get execution record with steps and events |
| POST | `/api/v1/executions/{id}/cancel` | Cancel a running/queued workflow |
| POST | `/api/v1/executions/{id}/retry` | Retry a failed/dead-lettered workflow |
| GET | `/api/v1/executions/resumable` | List workflows eligible for resume |
