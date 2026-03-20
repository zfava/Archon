# Multi-Instance Correctness — Technical Diligence Artifact

This document provides evidence that ArchonAI's Agent Registry and Control Plane state is durable and consistent across multiple application instances.

## Executive Summary

ArchonAI uses PostgreSQL as its shared persistence layer for operational state. The Agent Registry and Control Plane subsystems — previously in-memory and file-backed respectively — now use the same PostgreSQL persistence pattern as the 17 other domain stores in the system. This eliminates the multi-instance correctness gap identified during diligence.

## Problem Statement

| Subsystem | Previous Storage | Issue |
|-----------|-----------------|-------|
| Agent Registry | `ConcurrentDictionary` (in-memory) | State lost on restart; invisible to other instances |
| Control Plane | JSON file (`data/control-plane.json`) | Single-instance only; file locks prevent concurrent access |

## Solution

Both subsystems now implement PostgreSQL-backed stores following the existing `ReplaceWithFactory` pattern:

```
IAgentRegistryRepository → PostgresAgentRegistryStore (when ConnectionString is set)
                         → AgentRepository (in-memory fallback)

IControlPlaneRepository  → PostgresControlPlaneStore (when ConnectionString is set)
                         → DurableControlPlaneRepository (file-backed fallback)
```

### What Changed

| File | Change |
|------|--------|
| `Scripts/021_create_agent_registry.sql` | Migration: `registered_agents`, `agent_metrics` tables |
| `Scripts/022_create_control_plane.sql` | Migration: `tenants`, `managed_workflows`, `managed_agents`, `platform_policies`, `platform_configurations` tables |
| `Stores/PostgresAgentRegistryStore.cs` | Implements `IAgentRegistryRepository` against PostgreSQL |
| `Stores/PostgresControlPlaneStore.cs` | Implements `IControlPlaneRepository` against PostgreSQL |
| `DependencyInjection.cs` | Added `ReplaceWithFactory` calls for both interfaces (17 → 19 stores) |

### Concurrency Safety

- All upserts use `ON CONFLICT ... DO UPDATE` for safe concurrent writes
- Agent metrics are append-only (no update conflicts)
- Configuration uses composite unique key `(tenant_id, scope, key)` for conflict resolution
- Tenant names have UNIQUE constraints preventing duplicate creation

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

Run all multi-instance correctness tests:

```bash
dotnet test --filter "Category=Integration&Database=PostgreSQL"
```

### Fallback Behavior

When `ArchonAIPersistence:ConnectionString` is not set:
- Agent Registry falls back to in-memory `AgentRepository` (labeled non-production)
- Control Plane falls back to file-backed `DurableControlPlaneRepository` (labeled non-production)
- All other 17 stores follow the same fallback pattern

## Verification Procedures

### For Technical Diligence

1. **Inspect DI registration**: Verify `ReplaceWithFactory<IAgentRegistryRepository, PostgresAgentRegistryStore>` and `ReplaceWithFactory<IControlPlaneRepository, PostgresControlPlaneStore>` in `DependencyInjection.cs`
2. **Run integration tests**: `dotnet test --filter "Category=Integration&Database=PostgreSQL"` — all must pass
3. **Verify migration scripts**: Check `021_create_agent_registry.sql` and `022_create_control_plane.sql` exist and are idempotent
4. **Confirm store count**: `DependencyInjection.cs` should register 19 PostgreSQL stores with 19 `ReplaceWithFactory` calls

### For Operators

Ensure `ArchonAIPersistence:ConnectionString` is set in production. Without it, agent registry and control plane will use non-production fallback storage.

## Residual Considerations

- **In-memory fallback is non-production**: The fallback implementations are preserved for local development but should never be used in production multi-instance deployments
- **Migration ordering**: Migrations 021 and 022 must run before the application starts with PostgreSQL persistence enabled
- **No data migration**: Existing in-memory or file-backed state is not automatically migrated to PostgreSQL; a fresh start is required when switching backends
