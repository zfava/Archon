# Operational Twin

## Overview

The Operational Twin provides a living, graph-oriented model of the customer's business within ArchonAI. It captures the organizational structure — teams, functions, systems, integrations, workflows — along with their dependencies, KPIs, bottlenecks, and strategic objectives.

Unlike a static org chart, the twin evolves in real time as entities are added, KPIs are recorded, bottlenecks are detected, and dependencies shift.

## Domain Model

### Entity Types

| Type | Description |
|------|-------------|
| `Team` | Organizational team (e.g., "Platform Engineering") |
| `Function` | Business function (e.g., "Revenue Operations") |
| `System` | Technical system (e.g., "Order Management") |
| `Integration` | External integration point (e.g., "Salesforce Connector") |
| `Workflow` | Business or automation workflow |
| `Kpi` | Named metric entity |
| `Objective` | Strategic objective or OKR |

### Entity Status

`Active` · `Degraded` · `Inactive` · `Archived`

### Dependencies

Typed, directed edges between entities:

- **DependsOn** — Entity requires another to function
- **Feeds** — Entity provides data/output to another
- **Owns** — Entity owns/manages another
- **Monitors** — Entity observes another
- **Blocks** — Entity is blocking another

Each dependency carries an optional `CriticalityScore` (0.0–1.0).

### KPIs

Per-entity metrics with threshold-based warning detection:

- `MetricName`, `CurrentValue`, `TargetValue`
- `ThresholdWarning`, `ThresholdCritical`
- `Direction`: `HigherIsBetter` or `LowerIsBetter`
- `Unit` (e.g., "req/s", "%", "ms")

Warning detection logic:
- **HigherIsBetter**: warning if `currentValue <= thresholdWarning`
- **LowerIsBetter**: warning if `currentValue >= thresholdWarning`

### Bottlenecks

Detected performance or process constraints:

- Linked to an affected entity
- Severity: `Low` · `Medium` · `High` · `Critical`
- Optional root cause description
- Tracked from detection through resolution

### Artifact Links

Connect twin entities to other ArchonAI artifacts (decisions, goals, memory records) via typed relationships.

## API Endpoints

All endpoints are under `/api/v1/twin` and require authentication.

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/entities` | List entities (optional `?type=` filter) |
| `POST` | `/entities` | Create/update entity |
| `GET` | `/entities/{id}` | Get entity by ID |
| `DELETE` | `/entities/{id}` | Delete entity |
| `POST` | `/dependencies` | Add dependency edge |
| `GET` | `/entities/{id}/dependencies` | Get entity dependencies |
| `POST` | `/kpis` | Record KPI measurement |
| `GET` | `/entities/{id}/kpis` | Get entity KPIs |
| `POST` | `/bottlenecks` | Report bottleneck |
| `POST` | `/bottlenecks/{id}/resolve` | Resolve bottleneck |
| `GET` | `/bottlenecks` | List bottlenecks |
| `POST` | `/entities/{id}/links` | Link artifact to entity |
| `GET` | `/entities/{id}/links` | Get entity artifact links |
| `GET` | `/overview` | Aggregated twin overview |

## Overview Response

The `/overview` endpoint returns:

- **Entity counts** grouped by type
- **Active bottlenecks** (unresolved, sorted by severity)
- **Warning KPIs** (metrics breaching thresholds)
- **Total dependency count**

## Tenant Isolation

All operations are scoped to the authenticated tenant. Cross-tenant entity access returns `null` / empty results. Deletion across tenants is rejected.

## Frontend

The Operational Twin view (`/operational-twin`) provides:

- **Overview cards** — entity counts, dependency count, bottleneck count, KPI warning count
- **Active bottlenecks panel** — severity badges, root cause, detection time
- **KPI warnings panel** — metric name, current value, target
- **Entity type filter bar** — filter by Team, Function, System, etc.
- **Entity list** — type badge, status badge, name, description, tags

## Events

Entity upserts publish `twin.entity.upserted` via `IEventBus` with entity metadata for downstream consumers.

## Testing

30 tests covering:

- Entity CRUD and upsert semantics
- Tenant isolation on get, list, delete
- Dependency storage and entity-scoped retrieval
- KPI recording, overwrite-on-same-key, entity filtering
- Bottleneck lifecycle (report → resolve), active filtering, tenant isolation
- Artifact link storage and entity filtering
- Overview aggregation: entity counts, active bottlenecks, warning KPI detection (both directions), dependency counts, tenant isolation
- Event publication verification
