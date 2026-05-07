# Executive Command Layer

## Overview

The Executive Command Layer is a single high-signal view for CEO/COO/CFO-level users. It aggregates all prior operational systems into one surface that answers: what changed, what matters, what needs approval, what is drifting, where money is leaking, and what the AI is doing.

This is not a cosmetic dashboard. Every element is derived from live service composition with concurrent fan-out reads across six subsystems.

## Architecture

`ExecutiveCommandService` composes data from six services using `Task.WhenAll` for concurrent fan-out:

| Source Service | Data Provided |
|---|---|
| `IExceptionIntelligenceService` | Queue summary, prioritized exceptions, full exception list |
| `IGovernanceService` | Pending approval gates |
| `IOutcomeLearningService` | Calibration summary, outcome records (for drift detection) |
| `IOperationalTwinService` | Entity counts, bottlenecks, warning KPIs, dependency count |
| `ITrustTierService` | Policies, tier map per action scope |
| `IScenarioService` | All scenarios (active, compared, draft counts) |

## Summary Briefs

### Exception Brief
- `TotalOpen`, `Critical`, `High` counts from queue summary
- `TotalEconomicExposure` — sum of economic impact estimates across open exceptions
- `TopExceptions` — top 5 open exceptions ordered by priority score, excluding resolved/dismissed
- Each headline includes severity, category, title, priority score, economic impact, and recommended action type

### Approval Brief
- `PendingCount` — total pending approval gates
- `PendingApprovals` — up to 10 headlines ordered by request time (oldest first)
- Each headline includes action type, requester, justification, and request timestamp

### Calibration Brief
- `TotalOutcomes`, `Underperformed` count
- `HitRate` — fraction of outcomes that met or exceeded expectations
- `MeanVariancePercent` — average deviation from expected values
- `SignalDistribution` — breakdown by recalibration signal type

### Operational Brief
- `EntityCounts` — operational twin entity counts by type
- `ActiveBottlenecks` — count + top 5 headlines with severity, description, detection time
- `WarningKpis` — count of KPIs in warning state
- `TotalDependencies` — count of dependency edges in the operational twin graph

### Trust Tier Brief
- `TotalPolicies` — number of active trust tier policies
- `TierMap` — action scope to effective tier mapping (e.g., `deploy` -> `DraftApprovalRequired`)

### Scenario Brief
- `TotalActive` and `TotalCompared` counts
- `RecentScenarios` — top 5 by update time, with title, type, status, assumption/effect counts

### Economic Brief
- `ExceptionExposure` — total economic exposure from open exceptions
- `DecisionsPendingApproval` — count of pending approval gates
- `OutcomesDrifting` — count of outcomes with `Underperformed` direction
- `ActiveBottlenecks` — count of unresolved operational bottlenecks

## API

### `GET /api/v1/executive-command/summary`

**Authorization:** `GovernanceRead` policy

**Response:** `ExecutiveCommandSummary` containing all seven briefs plus `GeneratedAtUtc` timestamp.

## Frontend

The `ExecutiveCommandView` component renders:

1. **Signal cards** — 7 top-level metric cards with conditional coloring (critical=red, warning=amber, good=green, money=purple)
2. **What Needs Attention** — clickable exception headlines linking to `/exceptions`
3. **Awaiting Your Approval** — pending approval queue with action type, justification, requester, time
4. **Decision Calibration** — outcomes, hit rate, underperformed count, mean variance
5. **Operational Twin** — entity counts, dependency count, KPI warnings, bottleneck headlines
6. **AI Autonomy Controls** — trust tier chips showing scope-to-tier mappings
7. **Scenario Planning** — recent scenarios linking to `/scenarios`

Navigation: First item in the Operations section, accessible via `briefcase` icon. Requires `governance:read` permission.

## Tenant Isolation

All reads are scoped to the requesting tenant's ID. Services that accept `Guid tenantId` receive the raw GUID; services that accept `string tenantId` (IGovernanceService, ITrustTierService) receive `tenantId.ToString()`.

## Test Coverage

20 tests covering:
- **Composition behavior** — empty tenant returns zero briefs, each brief correctly aggregates its source data, limits enforced (top 5 exceptions, top 5 bottlenecks, top 5 scenarios, top 10 approvals), resolved/dismissed exceptions filtered out, priority ordering preserved
- **Tenant isolation** — correct tenant ID passed to every downstream service, different tenants produce independent results, string conversion verified for governance/trust tier services
- **Data integrity** — recommended action types surface in headlines, approval ordering by request time, economic brief cross-references multiple sources, generated timestamp is recent
