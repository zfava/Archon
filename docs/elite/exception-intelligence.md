# Exception Intelligence

## Overview

The Exception Intelligence center makes ArchonAI exception-first rather than dashboard-first. Instead of requiring operators to scan dashboards for problems, the system surfaces a prioritized queue of anomalies, failures, drift, policy violations, threshold breaches, and high-value intervention points — ranked by composite priority score.

Each exception carries structured severity, economic impact estimates, urgency, confidence, escalation level, recommended actions, and links to platform artifacts (decisions, outcomes, operational twin entities, workflows).

## Domain Model

### Exception Categories

| Category | Description |
|----------|-------------|
| `Anomaly` | Unexpected deviation from normal patterns |
| `Failure` | System or process failure |
| `Drift` | Gradual degradation or deviation from targets |
| `Bottleneck` | Capacity or throughput constraint |
| `PolicyViolation` | Action breaching governance or trust policies |
| `ThresholdBreach` | KPI or metric exceeding defined thresholds |
| `EscalationRequired` | Situation requiring higher-authority attention |
| `InterventionPoint` | High-value opportunity for human intervention |

### Severity Levels

`Info` · `Warning` · `High` · `Critical`

### Exception Status

`Open` → `Acknowledged` → `InProgress` → `Resolved` | `Dismissed`

### Escalation Levels

`None` · `Operator` · `Manager` · `Executive`

## Priority Scoring

The priority score determines queue ordering. It is a composite of:

```
score = severityWeight × urgency × economicFactor × confidence × escalationBoost
```

Where:
- **severityWeight**: Critical=4, High=3, Warning=2, Info=1
- **urgency**: 0.0–1.0 (operator-provided or system-derived)
- **economicFactor**: `1 + min(log10(max(impact, 1)), 6) / 6` — log-scaled to prevent runaway scores
- **confidence**: 0.0–1.0 — how certain we are this exception is real
- **escalationBoost**: Executive=1.5x, Manager=1.2x, Operator/None=1.0x

This ensures critical high-urgency exceptions with large economic exposure and executive escalation always surface first.

## Recommended Actions

Each exception can carry a structured recommended action:

- **ActionType** — e.g., "Restart", "Rollback", "Escalate", "Investigate"
- **Description** — human-readable resolution guidance
- **TargetArtifactType/Id** — optional link to the artifact to act on
- **Confidence** — how confident the recommendation is

## Artifact Linkage

Exceptions link to platform artifacts via typed references:

- **Decision** — the decision that caused or relates to this exception
- **TwinEntity** — the operational twin entity affected
- **Workflow** — the workflow experiencing the exception
- **Outcome** — the outcome record showing variance
- **TrustTier** — the trust tier policy being violated

## API Endpoints

All endpoints under `/api/v1/exceptions`, requiring authentication.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `POST` | `/` | GovernanceWrite | Raise a new exception |
| `GET` | `/` | GovernanceRead | List exceptions (filters: `severity`, `category`, `status`, `domain`) |
| `GET` | `/{exceptionId}` | GovernanceRead | Get exception detail |
| `PUT` | `/{exceptionId}/status` | GovernanceWrite | Update status and assignment |
| `POST` | `/{exceptionId}/recommended-action` | GovernanceWrite | Attach recommended action |
| `GET` | `/summary` | GovernanceRead | Queue summary (counts, economic exposure, by-category) |
| `GET` | `/prioritized` | GovernanceRead | Priority-ranked queue (optional `?limit=`) |

## Queue Summary

The `/summary` endpoint returns:

- **TotalOpen** — exceptions not resolved/dismissed
- **Critical / High / Warning** — counts by severity
- **TotalEconomicExposure** — sum of economic impact estimates for open exceptions
- **ByCategory** — open exception counts grouped by category

## Tenant Isolation

All operations are scoped to the authenticated tenant. Cross-tenant exception access returns `null` or empty results. Status updates across tenants are rejected.

## Frontend

The Exception Intelligence view (`/exceptions`) provides:

- **Summary cards** — open count, critical/high/warning counts, total economic exposure
- **Severity filter bar** — filter by Critical, High, Warning, Info
- **Status filter bar** — filter by Open, Acknowledged, InProgress, Resolved, Dismissed
- **Prioritized exception queue** — sorted by composite priority score, showing severity/category/status badges, title, domain, urgency, economic impact
- **Detail panel** — scoring breakdown (urgency, economic impact, confidence, priority), recommended action, linked artifacts, timestamps, status transition buttons

## Events

Exception creation publishes `exception.raised` via `IEventBus` with exception metadata.

## Testing

30 tests covering:

- Raise and persistence, event publication
- Tenant isolation (get, list, summary, prioritized queue)
- Filtering by severity, category, status, domain
- Status transitions (acknowledge, in-progress, resolve) with timestamp tracking
- Assignment behavior
- Recommended action attachment
- Artifact linkage preservation
- Priority ordering in list and queue
- Queue exclusion of resolved/dismissed
- Queue limit enforcement
- Summary aggregation (counts, economic exposure, by-category)
- Priority scoring logic (severity weight, urgency, escalation boost, economic impact)
