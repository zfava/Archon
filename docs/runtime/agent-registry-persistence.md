# Agent Registry Persistence

This document describes the durable PostgreSQL persistence backend for the Agent Registry subsystem.

## Overview

The Agent Registry tracks all registered agents, their capabilities, status, and runtime metrics. Prior to this change, agent state was held entirely in-memory via `ConcurrentDictionary` in `AgentRepository.cs`, meaning all state was lost on restart and invisible to other application instances.

## Persistence Model

### Tables

| Table | Purpose |
|-------|---------|
| `archonai.registered_agents` | Agent identity, status, capabilities, configuration |
| `archonai.agent_metrics` | Point-in-time performance snapshots per agent |

### Schema (Migration 021)

```sql
CREATE TABLE archonai.registered_agents (
    id                 uuid        PRIMARY KEY,
    name               text        NOT NULL,
    description        text        NOT NULL,
    version            text        NOT NULL,
    status             int         NOT NULL,  -- Active/Disabled/Draining/Offline
    capabilities       jsonb       NOT NULL,  -- Array of AgentCapabilityRecord
    configuration      jsonb       NOT NULL,  -- Key-value configuration
    registered_at_utc  timestamptz NOT NULL,
    last_heartbeat_utc timestamptz,
    disabled_at_utc    timestamptz
);

CREATE TABLE archonai.agent_metrics (
    id                    uuid        PRIMARY KEY,
    agent_id              uuid        NOT NULL REFERENCES registered_agents(id) ON DELETE CASCADE,
    total_executions      bigint,
    successful_executions bigint,
    failed_executions     bigint,
    average_latency_ms    double precision,
    p95_latency_ms        double precision,
    uptime_percent        double precision,
    collected_at_utc      timestamptz NOT NULL
);
```

## Backend Selection

The persistence backend is selected at startup via configuration:

- **PostgreSQL** (production): Set `ArchonAIPersistence:ConnectionString` to a valid PostgreSQL connection string
- **In-memory** (development/fallback): Leave `ConnectionString` empty — the original `AgentRepository` is used

This is managed by the `ReplaceWithFactory<IAgentRegistryRepository, PostgresAgentRegistryStore>` pattern in `ArchonAI.Persistence/DependencyInjection.cs`.

## Multi-Instance Correctness

With PostgreSQL persistence enabled:
- All instances share the same agent registry state
- Upserts use `ON CONFLICT (id) DO UPDATE` for safe concurrent writes
- Metrics are append-only with no conflict risk
- Agent removal cascades to associated metrics via `ON DELETE CASCADE`

## Agent Capability Registry

In addition to the core agent registry, the **Agent Capability Registry** tracks agent performance profiles, execution samples, and composite scoring for intelligent agent selection. Prior to Phase 2, this was held entirely in `ConcurrentDictionary` in `InMemoryAgentCapabilityRegistry`, meaning each instance computed independent performance scores — causing agent selection divergence across pods.

### Tables (Migration 023)

| Table | Purpose |
|-------|---------|
| `archonai.agent_capability_profiles` | Per-agent profile: capabilities, tools, permissions, performance aggregates, suspension state |
| `archonai.agent_execution_samples` | Rolling execution samples for P95 latency computation |

Key design decisions:
- P95 latency computed via SQL `PERCENTILE_CONT(0.95)` over a rolling window of 1000 samples
- Composite agent score: 40% success rate + 30% latency + 20% cost + 10% throughput
- GIN indexes on `capabilities` and `supported_task_types` for jsonb containment queries
- Execution samples cascade-delete when an agent profile is removed

### Backend Selection

```
IAgentCapabilityRegistry → PostgresAgentCapabilityRegistryStore (when ConnectionString is set)
                         → InMemoryAgentCapabilityRegistry (in-memory fallback)
```

## Integration Tests

Run the PostgreSQL integration tests:

```bash
# Core agent registry
dotnet test --filter "Category=Integration&Subsystem=AgentRegistry"

# Agent capability registry
dotnet test --filter "Category=Integration&Subsystem=AgentCapabilityRegistry"
```

Core agent registry tests prove:
- Agent state survives store re-instantiation (restart proof)
- Two store instances see the same state (multi-instance proof)
- List/filter/count operations work correctly
- Upsert is idempotent
- Remove cascades to metrics

Agent capability registry tests prove:
- Capability profiles survive store re-instantiation
- Two instances see the same agent performance data (multi-instance proof)
- Execution reporting updates aggregates correctly (including P95)
- Agent suspension/reinstatement is shared across instances
- Query by capability and task type returns correct results
- Best-agent selection is consistent across instances
