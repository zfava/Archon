# State Persistence Model

## Overview

ArchonAI persists all enterprise-critical platform state to disk-backed JSON stores. Each store maintains an in-memory `ConcurrentDictionary` hot cache for low-latency reads, with fire-and-forget flush to a JSON file on every mutation. On startup, the store loads from disk to restore state, ensuring platform truth survives process restarts.

## Architecture

```
  ┌──────────────┐     ┌──────────────────┐     ┌──────────────┐
  │   API/Service │────>│  Durable Store    │────>│  JSON File   │
  │   Consumers   │<────│  (hot cache +     │<────│  on Disk     │
  └──────────────┘     │   async flush)    │     └──────────────┘
                       └──────────────────┘
```

### Design Principles

1. **In-memory hot cache** — all reads served from `ConcurrentDictionary` at O(1)
2. **Async flush on write** — every mutation triggers a non-blocking flush to disk
3. **Load on construction** — state restored from JSON before the store becomes available
4. **Fail-safe** — flush failures are logged but do not block the caller
5. **Interface parity** — durable stores implement the exact same interface as their in-memory predecessors

## Persistent Stores

| Store | Interface | Data File | Data |
|-------|-----------|-----------|------|
| `DurableControlPlaneRepository` | `IControlPlaneRepository` | `data/control-plane.json` | Tenants, managed workflows, managed agents, policies, configurations |
| `DurableAdminService` | `IAdminService` | `data/admin-state.json` | Agent enable/disable overrides, policy configuration |
| `DurableTraceStore` | `ITraceStore` | `data/trace-entries.json` | Execution trace entries (bounded) |
| `DurableStrategyStore` | `IStrategyStore` | `data/strategies.json` | Operational strategies and workflow templates |
| `DurableAgentIdentityStore` | `IAgentIdentityStore` | `data/agent-identities.json` | Agent profiles, capabilities, permissions, execution history |
| `DurableIdentityStore` | `IUserStore`, `IOrganizationStore`, `IMembershipStore`, `IRefreshTokenStore`, `IInviteTokenStore` | `data/identity-state.json` | Users, organizations, memberships, refresh tokens, invite tokens |
| `DurableWorkflowStore` | `IWorkflowExecutionStore` | `data/workflow-executions.json` | Workflow execution records (pre-existing) |

## Configuration

Each store supports a `PersistencePath` option to override the default file location:

```json
{
  "ControlPlane": {
    "PersistencePath": "/var/archonai/data/control-plane.json"
  },
  "Trace": {
    "PersistencePath": "/var/archonai/data/traces.json"
  },
  "Strategy": {
    "PersistencePath": "/var/archonai/data/strategies.json"
  },
  "Admin": {
    "PersistencePath": "/var/archonai/data/admin-state.json"
  },
  "Identity": {
    "PersistencePath": "/var/archonai/data/identity-state.json",
    "AgentIdentityPersistencePath": "/var/archonai/data/agent-identities.json"
  }
}
```

If no path is configured, files default to `{AppContext.BaseDirectory}/data/`.

## Stores That Remain In-Memory (By Design)

These stores hold **ephemeral/transient state** that is not persisted because it is rebuilt on startup or is inherently tied to active process state:

| Store | Rationale |
|-------|-----------|
| `TaskExecutionManager` | Task queues are rebuilt from pending workflow steps on startup |
| `AgentCoordinationService` | Pending support requests are tied to active connections |
| `AgentCollaborationManager` | Active collaboration sessions are tied to running processes |
| `AgentRuntime._agentRegistry` | Rebuilt from agent registrations during startup |
| `InMemoryEventBus` | Subscriptions are code-level; events are fire-and-forget |

## Restart Behavior

1. Process starts; DI constructs durable stores
2. Each store's constructor loads its JSON file from disk
3. Hot cache is populated from the loaded data
4. Store is ready to serve reads immediately
5. On first mutation, the store flushes the updated state to disk
6. If the JSON file doesn't exist, the store starts clean (no error)

## Concurrency

- Reads: lock-free via `ConcurrentDictionary`
- Writes: mutations are lock-free; flush uses a `SemaphoreSlim(1,1)` to serialize disk writes
- If a flush is already in progress, a new flush request is skipped (non-blocking)

## Production Upgrade Path

The file-backed stores are designed as a stepping stone. For production deployments at scale, replace individual stores with database-backed implementations:

1. Implement the same interface (e.g., `IControlPlaneRepository`) backed by PostgreSQL
2. Update the DI registration in the project's `DependencyInjection.cs`
3. No consumer code changes required — persistence boundary is the interface
