# Multi-Instance Correctness — Technical Diligence Artifact

Last verified: 2026-03-21

This document provides evidence that ArchonAI's Agent Registry and Control Plane state is durable and consistent across multiple application instances.

## Executive Summary

ArchonAI uses PostgreSQL as its shared persistence layer for operational state. The Agent Registry, Control Plane, Agent Capability Registry, Control Plane operational state (pause/alerts/events), and all identity stores — previously in-memory, file-backed, or volatile — now use the same PostgreSQL persistence pattern as the other domain stores in the system. This eliminates all identified multi-instance correctness gaps. The system now has 31 PostgreSQL-backed stores (22 domain + 9 identity) with 31 `ReplaceWithFactory` registrations.

## Problem Statement

| Subsystem | Previous Storage | Issue |
|-----------|-----------------|-------|
| Agent Registry | `ConcurrentDictionary` (in-memory) | State lost on restart; invisible to other instances |
| Control Plane | JSON file (`data/control-plane.json`) | Single-instance only; file locks prevent concurrent access |
| Agent Capability Registry | `ConcurrentDictionary` (in-memory) | Each instance computes independent performance scores; agent selection diverges across pods |
| Control Plane Observability | `volatile bool`, `ConcurrentDictionary`, `ConcurrentQueue` (in-memory) | System pause state, alerts, and events invisible to other instances |

## Solution

Both subsystems now implement PostgreSQL-backed stores following the existing `ReplaceWithFactory` pattern:

```
IAgentRegistryRepository    → PostgresAgentRegistryStore (when ConnectionString is set)
                            → AgentRepository (in-memory fallback)

IControlPlaneRepository     → PostgresControlPlaneStore (when ConnectionString is set)
                            → DurableControlPlaneRepository (file-backed fallback)

IAgentCapabilityRegistry    → PostgresAgentCapabilityRegistryStore (when ConnectionString is set)
                            → InMemoryAgentCapabilityRegistry (in-memory fallback)

IControlPlaneAlertStore     → PostgresControlPlaneAlertStore (when ConnectionString is set)
                            → InMemoryControlPlaneAlertStore (in-memory fallback)
```

### What Changed

| File | Change |
|------|--------|
| `Scripts/021_create_agent_registry.sql` | Migration: `registered_agents`, `agent_metrics` tables |
| `Scripts/022_create_control_plane.sql` | Migration: `tenants`, `managed_workflows`, `managed_agents`, `platform_policies`, `platform_configurations` tables |
| `Scripts/023_create_agent_capability_profiles.sql` | Migration: `agent_capability_profiles`, `agent_execution_samples` tables |
| `Scripts/024_create_control_plane_alerts.sql` | Migration: `control_plane_system_state`, `control_plane_alerts`, `control_plane_agent_events` tables |
| `Stores/PostgresAgentRegistryStore.cs` | Implements `IAgentRegistryRepository` against PostgreSQL |
| `Stores/PostgresControlPlaneStore.cs` | Implements `IControlPlaneRepository` against PostgreSQL |
| `Stores/PostgresAgentCapabilityRegistryStore.cs` | Implements `IAgentCapabilityRegistry` against PostgreSQL |
| `Stores/PostgresControlPlaneAlertStore.cs` | Implements `IControlPlaneAlertStore` against PostgreSQL |
| `IControlPlaneAlertStore.cs` | New interface extracting durable state from `ControlPlaneObservabilityService` |
| `InMemoryControlPlaneAlertStore.cs` | In-memory fallback for `IControlPlaneAlertStore` |
| `ControlPlaneObservabilityService.cs` | Refactored to delegate all state to `IControlPlaneAlertStore` |
| `DependencyInjection.cs` (Persistence) | Added `ReplaceWithFactory` calls for both new interfaces (19 → 31 stores, including 9 identity stores added in identity hardening pass) |
| `DependencyInjection.cs` (ControlPlane) | Added `IControlPlaneAlertStore` registration |

### Concurrency Safety

- All upserts use `ON CONFLICT ... DO UPDATE` for safe concurrent writes
- Agent metrics are append-only (no update conflicts)
- Configuration uses composite unique key `(tenant_id, scope, key)` for conflict resolution
- Tenant names have UNIQUE constraints preventing duplicate creation
- Agent capability profiles use P95 latency computed via SQL `PERCENTILE_CONT(0.95)` over rolling samples
- System pause state uses a single-row table pattern (`CHECK (id = 1)`) — no INSERT races
- Alert eviction is bounded (default 500) to prevent unbounded growth

## Proof Artifacts

### Integration Tests

| Test | What It Proves |
|------|---------------|
| `Agent_SurvivesStoreReinstantiation` | Agent state persists across service restart |
| `TwoInstances_SeeSameAgentState` | Instance A writes, instance B reads same data |
| `Tenant_SurvivesStoreReinstantiation` | Tenant state persists across service restart |
| `TwoInstances_SeeSameTenantState` | Instance A writes, instance B sees update |
| `MultiInstance_FullLifecycleAcrossInstances` | Full CRUD across two instances |
| `UpsertAgent_IsIdempotent` | Repeated upserts don't duplicate data |
| `RemoveAgent_DeletesAgentAndMetrics` | Cascade deletion works correctly |
| `Profile_SurvivesStoreReinstantiation` | Agent capability profile persists across restart |
| `TwoInstances_SeeSameProfileState` | Agent performance data shared across instances |
| `ExecutionData_VisibleAcrossInstances` | Execution aggregates consistent across instances |
| `SuspendAndReinstate_SharedAcrossInstances` | Agent suspension/reinstatement shared |
| `SelectBestAgent_PicksHighestScoringAgent` | Agent selection consistent with shared data |
| `PauseState_SurvivesStoreReinstantiation` | System pause persists across restart |
| `PauseState_VisibleAcrossInstances` | Pause/resume visible across instances |
| `Alerts_VisibleAcrossInstances` | Alert state shared across instances |
| `AcknowledgeAlert_PersistsAcrossInstances` | Alert acknowledgement visible to all |
| `Events_SurviveStoreReinstantiation` | Agent events persist across restart |
| `MultiInstance_FullLifecycleAcrossInstances` (Alerts) | Full pause/alert/resume lifecycle across instances |

Run all multi-instance correctness tests:

```bash
dotnet test --filter "Category=Integration&Database=PostgreSQL"
```

### Fallback Behavior

When `ArchonAIPersistence:ConnectionString` is not set:
- Agent Registry falls back to in-memory `AgentRepository` (labeled non-production)
- Control Plane falls back to file-backed `DurableControlPlaneRepository` (labeled non-production)
- Agent Capability Registry falls back to in-memory `InMemoryAgentCapabilityRegistry` (labeled non-production)
- Control Plane Alerts falls back to in-memory `InMemoryControlPlaneAlertStore` (labeled non-production)
- All other 27 stores (including 9 identity stores) follow the same fallback pattern

## Verification Procedures

### For Technical Diligence

1. **Inspect DI registration**: Verify all 31 `ReplaceWithFactory` calls (22 domain + 9 identity) in `DependencyInjection.cs`
2. **Run integration tests**: `dotnet test --filter "Category=Integration&Database=PostgreSQL"` — all must pass
3. **Verify migration scripts**: Check migrations 001–025 exist and are idempotent
4. **Confirm store count**: `DependencyInjection.cs` should register 31 PostgreSQL stores with 31 `ReplaceWithFactory` calls

### For Operators

Ensure `ArchonAIPersistence:ConnectionString` is set in production. Without it, agent registry and control plane will use non-production fallback storage.

## Residual Considerations

- **In-memory fallback is non-production**: The fallback implementations are preserved for local development but should never be used in production multi-instance deployments
- **Migration ordering**: Migrations 021–025 must run before the application starts with PostgreSQL persistence enabled
- **No data migration**: Existing in-memory or file-backed state is not automatically migrated to PostgreSQL; a fresh start is required when switching backends
- **IModelPerformanceTracker remains in-memory**: Model performance scoring (`ModelPerformanceTracker`) is still in-memory. This is acceptable because model scores are derived from live request telemetry and rebuild naturally on startup
- **IClusterCoordinator state is transient**: Cluster node membership is inherently instance-specific and does not require shared persistence
