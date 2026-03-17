# Data Lifecycle and Retention

## Overview

This document describes how data flows through ArchonAI's persistent stores, what retention policies apply, and how data is managed across the platform lifecycle.

## Data Categories

### 1. Control Plane State (Critical, Unbounded)

**Stores:** `DurableControlPlaneRepository`

| Entity | Lifecycle | Retention |
|--------|-----------|-----------|
| Tenants | Created on onboarding, transitions through provisioning -> active -> suspended -> deprovisioned | Retained until explicitly removed |
| Managed Workflows | Created when a workflow is registered, status transitions through draft -> active -> paused -> archived | Retained until explicitly removed |
| Managed Agents | Created on agent registration, status transitions through registering -> active -> disabled -> deregistered | Retained until explicitly removed |
| Platform Policies | Created by administrators, can be enabled/disabled | Retained until explicitly removed |
| Platform Configurations | Key-value settings per tenant/scope | Retained until explicitly removed |

**Recommendation:** Implement periodic archival of deprovisioned tenants and archived workflows.

### 2. Admin Overrides (Critical, Bounded)

**Stores:** `DurableAdminService`

| Entity | Lifecycle | Retention |
|--------|-----------|-----------|
| Agent enable/disable overrides | Set by admin, survives restart | Retained until explicitly changed |
| Policy configuration | Set by admin, survives restart | Single active configuration, overwritten on update |

### 3. Identity Data (Critical, Unbounded)

**Stores:** `DurableIdentityStore`, `DurableAgentIdentityStore`

| Entity | Lifecycle | Retention |
|--------|-----------|-----------|
| Users | Created on registration | Retained until explicitly removed |
| Organizations | Created on onboarding | Retained until explicitly removed |
| Memberships | Created when user joins org | Retained until user leaves or is removed |
| Refresh tokens | Created on login, expire after TTL | Retained; expired/revoked tokens should be periodically pruned |
| Invite tokens | Created by admin, accepted or expire | Retained; accepted/expired invites should be periodically pruned |
| Agent identity profiles | Created on agent registration, updated per execution | Retained; execution history bounded by `MaxExecutionHistoryEntries` (default: 200) |

### 4. Execution Traces (Audit, Bounded)

**Stores:** `DurableTraceStore`

| Entity | Lifecycle | Retention |
|--------|-----------|-----------|
| Trace entries | Created per execution event | Bounded circular buffer (configurable via `Trace:MaxEntries`, default: 5000) |

Oldest entries are automatically evicted when the buffer is full.

### 5. Strategies (Operational, Unbounded)

**Stores:** `DurableStrategyStore`

| Entity | Lifecycle | Retention |
|--------|-----------|-----------|
| Operational strategies | Seeded from config or created at runtime | Retained indefinitely; updated in place |

### 6. Workflow Executions (Operational, Unbounded)

**Stores:** `DurableWorkflowStore`

| Entity | Lifecycle | Retention |
|--------|-----------|-----------|
| Workflow execution records | Created per workflow run, status transitions | Retained indefinitely |

**Recommendation:** Implement periodic cleanup of completed/failed executions older than a configurable threshold.

## Ephemeral Data (Not Persisted)

| Data | Store | Rationale |
|------|-------|-----------|
| Active task queues | `TaskExecutionManager` | Rebuilt from pending workflow steps |
| Agent coordination requests | `AgentCoordinationService` | Tied to active connections |
| Collaboration sessions | `AgentCollaborationManager` | Tied to active processes |
| Runtime agent registry | `AgentRuntime` | Rebuilt on startup |
| Event subscriptions | `InMemoryEventBus` | Code-level subscriptions |
| Task telemetry | `InMemoryTaskTelemetryStore` | Metrics aggregation; optionally persisted via PostgreSQL |
| Planning feedback | `InMemoryPlanningFeedbackStore` | Continuous improvement; no persistence requirement |
| Knowledge graph | `InMemoryKnowledgeGraphStore` | Optionally persisted via PostgreSQL |

## Retention Recommendations

### Short-term (implement now)
- Trace entries: bounded by `MaxEntries` (already implemented)
- Agent execution history: bounded by `MaxExecutionHistoryEntries` (already implemented)

### Medium-term (production hardening)
- Expired refresh tokens: prune tokens where `ExpiresAtUtc < now` on a schedule
- Accepted/expired invite tokens: prune on a schedule
- Completed workflow executions: archive records older than 30 days

### Long-term (enterprise scale)
- Replace file-backed stores with PostgreSQL-backed implementations
- Implement event sourcing for audit-critical data (control plane mutations)
- Add data export/archival pipelines for compliance

## Backup and Recovery

### Current (file-backed stores)
- JSON files in the `data/` directory
- Back up this directory to preserve all platform state
- Restore by placing the JSON files back before starting the process

### Recommended production setup
- Database-level backups with point-in-time recovery
- Regular automated backups on a schedule
- Tested restore procedures documented in runbooks

## Schema Versioning

Each JSON snapshot uses `System.Text.Json` with `JsonNamingPolicy.CamelCase` and `JsonStringEnumConverter`. Records are serialized as flat JSON objects.

For schema evolution:
1. New fields added to records should have sensible defaults
2. C# records with `init` properties handle missing fields gracefully (default values)
3. Enum values use string serialization for forward compatibility
4. For breaking changes, implement a migration step in the store's `LoadFromDisk()` method
