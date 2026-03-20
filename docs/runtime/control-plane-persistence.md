# Control Plane Persistence

This document describes the durable PostgreSQL persistence backend for the Control Plane subsystem.

## Overview

The Control Plane manages tenant lifecycle, managed workflows, managed agents, platform policies, and platform configurations. Prior to this change, state was stored in a local JSON file (`data/control-plane.json`) via `DurableControlPlaneRepository`, making it single-instance only.

## Persistence Model

### Tables

| Table | Purpose |
|-------|---------|
| `archonai.tenants` | Tenant identity, status, tier, resource quotas |
| `archonai.managed_workflows` | Workflow definitions and execution counts |
| `archonai.managed_agents` | Tenant-scoped agent registrations |
| `archonai.platform_policies` | Security, rate-limit, compliance policies |
| `archonai.platform_configurations` | Tenant-scoped key-value configuration |

### Schema (Migration 022)

Five tables with appropriate indexes for tenant-scoped queries. See `Scripts/022_create_control_plane.sql` for full DDL.

Key design decisions:
- `tenants.name` has a UNIQUE constraint for name-based lookup
- `platform_configurations` has a composite UNIQUE constraint on `(tenant_id, scope, key)` for conflict-based upserts
- All entity tables use `ON CONFLICT (id) DO UPDATE` for safe concurrent writes
- Tenant IDs are stored as `text` in child tables (matching the existing `string TenantId` model property)

## Backend Selection

The persistence backend is selected at startup via configuration:

- **PostgreSQL** (production): Set `ArchonAIPersistence:ConnectionString`
- **File-backed** (development/fallback): Leave `ConnectionString` empty — the original `DurableControlPlaneRepository` is used

This is managed by the `ReplaceWithFactory<IControlPlaneRepository, PostgresControlPlaneStore>` pattern in `ArchonAI.Persistence/DependencyInjection.cs`.

## Multi-Instance Correctness

With PostgreSQL persistence enabled:
- All instances share the same control plane state
- Tenant operations are serialized by the database's UNIQUE and PRIMARY KEY constraints
- Configuration upserts use the composite unique key `(tenant_id, scope, key)` for idempotent writes
- Policy and workflow operations use `ON CONFLICT (id) DO UPDATE`

## Control Plane Alert Store

The **Control Plane Alert Store** manages system pause state, active alerts, and agent activity events. Prior to Phase 2, these were held as `volatile bool`, `ConcurrentDictionary`, and `ConcurrentQueue` fields inside `ControlPlaneObservabilityService` — meaning pause state and alerts were invisible to other instances.

The durable state was extracted into a focused `IControlPlaneAlertStore` interface, keeping the dashboard computation logic in `ControlPlaneObservabilityService` (which delegates to multiple other services).

### Tables (Migration 024)

| Table | Purpose |
|-------|---------|
| `archonai.control_plane_system_state` | Single-row table for system pause state (`CHECK (id = 1)`) |
| `archonai.control_plane_alerts` | Active system alerts with acknowledgement tracking |
| `archonai.control_plane_agent_events` | Recent agent activity event history |

Key design decisions:
- System state uses a single-row pattern with `CHECK (id = 1)` — no INSERT races
- Alert eviction deletes oldest beyond a configurable max (default 500)
- Events are pruned beyond 200 rows on each insert
- `ControlPlaneObservabilityService` delegates all state to `IControlPlaneAlertStore` via fire-and-forget for alerts

### Backend Selection

```
IControlPlaneAlertStore → PostgresControlPlaneAlertStore (when ConnectionString is set)
                        → InMemoryControlPlaneAlertStore (in-memory fallback)
```

## Integration Tests

Run the PostgreSQL integration tests:

```bash
# Core control plane (tenants, workflows, policies, config)
dotnet test --filter "Category=Integration&Subsystem=ControlPlane"

# Control plane alerts (pause state, alerts, events)
dotnet test --filter "Category=Integration&Subsystem=ControlPlaneAlerts"
```

Core control plane tests prove:
- Tenant state survives store re-instantiation (restart proof)
- Two store instances see the same tenant/workflow/policy state (multi-instance proof)
- Full lifecycle across instances (create on A, modify on B, read on A)
- List/filter/count operations work correctly
- Configuration upsert-on-conflict updates correctly

Control plane alert tests prove:
- System pause state is visible across instances
- Alerts are consistent across instances
- Acknowledgement persists and is visible to other instances
- Agent activity events survive store re-instantiation
- Full multi-instance lifecycle (pause, alert, resume across instances)
