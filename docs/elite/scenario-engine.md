# Scenario Engine

## Overview

The Scenario Engine enables operators and executives to explore "what happens if" planning paths within ArchonAI. It provides structured scenario modeling with explicit assumptions, deterministic effect projections, and side-by-side comparison — without making unfounded AI forecasting claims.

Scenarios are bounded planning tools: they formalize the assumptions behind a proposed change, map those assumptions to projected effects, and let users compare alternatives across common metrics.

## Domain Model

### Scenario Types

| Type | Use Case |
|------|----------|
| `WhatIf` | General exploration of a hypothetical change |
| `CostReduction` | Modeling cost-cutting or efficiency initiatives |
| `GrowthPlanning` | Evaluating expansion, hiring, or scaling plans |
| `RiskMitigation` | Assessing risk reduction strategies |
| `ResourceReallocation` | Exploring redistribution of headcount, budget, etc. |
| `ProcessChange` | Modeling workflow or process modifications |
| `StrategicPivot` | Evaluating fundamental strategic shifts |

### Scenario Status

`Draft` · `Active` · `Compared` · `Archived`

### Assumptions

Each scenario contains a list of structured assumptions:

- **Name** — the variable being changed (e.g., "Headcount", "Budget")
- **CurrentValue** — the baseline state
- **ProposedValue** — the hypothetical new value
- **Unit** — optional measurement unit
- **Rationale** — why this change is being considered

### Projected Effects

Effects are deterministically derived from assumptions:

- **Area** / **Metric** — what is affected
- **BaselineValue** / **ProjectedValue** — numeric before/after (if applicable)
- **Direction** — `Increase`, `Decrease`, `Unchanged`, or `Changed`
- **Confidence** — `High` (small delta, <10%), `Medium` (larger delta), or `Low` (non-numeric)

The engine does NOT perform speculative AI forecasting. Confidence levels reflect the size and nature of the input change, not predictive accuracy claims.

### Scenario Links

Scenarios can reference platform artifacts:

- **KPIs** — from the operational twin
- **Decisions** — from the decision engine
- **TwinEntities** — teams, systems, workflows from the operational twin
- Financial consequence models and strategic objectives (via entity links)

## API Endpoints

All endpoints under `/api/v1/scenarios`, requiring authentication.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `POST` | `/` | GovernanceWrite | Create scenario with assumptions and links |
| `GET` | `/` | GovernanceRead | List scenarios (optional `?type=` and `?status=` filters) |
| `GET` | `/{scenarioId}` | GovernanceRead | Get scenario details |
| `PUT` | `/{scenarioId}/assumptions` | GovernanceWrite | Update assumptions (re-derives effects) |
| `POST` | `/compare` | GovernanceRead | Compare 2+ scenarios side by side |
| `DELETE` | `/{scenarioId}` | GovernanceWrite | Delete scenario |

## Comparison

The `/compare` endpoint accepts a list of scenario IDs and returns:

- **Axes** — the union of all projected-effect metrics across the compared scenarios
- **Values per scenario** — projected values for each metric, with `null` where a scenario doesn't model that metric

Compared scenarios are automatically marked with `Compared` status.

## Effect Derivation Logic

When assumptions are created or updated, the engine derives projected effects using deterministic rules:

1. **Numeric assumptions** — compute delta, determine direction (Increase/Decrease/Unchanged), assign confidence based on magnitude
2. **Non-numeric assumptions** — produce qualitative effect with `Changed`/`Unchanged` direction and `Low` confidence
3. Confidence threshold: delta < 10% of baseline → `High`, otherwise → `Medium`

This approach provides useful structured output without overpromising predictive capability.

## Tenant Isolation

All operations are tenant-scoped. Cross-tenant scenario access returns `null` or empty results. Comparison excludes scenarios from other tenants.

## Frontend

The Scenario Engine view (`/scenarios`) provides:

- **Type filter bar** — filter by WhatIf, CostReduction, etc.
- **Scenario list** — with type/status badges, assumption and effect counts
- **Detail panel** — assumptions table, projected effects, linked artifacts
- **Comparison panel** — side-by-side metric table for 2+ selected scenarios
- **Create form** — modal for defining new scenarios with assumptions
- **Checkbox selection** — for multi-scenario comparison

## Events

Scenario creation publishes `scenario.created` via `IEventBus` with scenario metadata.

## Testing

27 tests covering:

- Create and persistence
- Event publication
- Tenant isolation (get, list, delete, compare)
- Delete behavior
- List filtering by type and status
- Assumption update with effect re-derivation
- Comparison axis building (metric union)
- Comparison value population per scenario
- Status marking on comparison
- Linkage preservation (KPIs, Decisions)
- Effect derivation: numeric direction, non-numeric qualitative, confidence levels (small/large delta), decrease direction
