# Decision-to-Outcome Lineage

## Overview

Decision-to-outcome lineage traces the complete path from when ArchonAI creates a decision through its execution to the observed business outcome. This lineage provides auditable evidence of what happened, when, and what the measured result was.

## Lineage Chain

A typical lineage for a fully-executed decision:

```
DecisionCreated
  → RecommendationMade
    → ApprovalRequested
      → ApprovalGranted (or Denied)
        → ActionExecuted (success/failure)
          → ExpectedOutcomeRecorded
            → ActualOutcomeRecorded
              → VarianceComputed
                → EconomicImpactAttributed (if data available)
```

Not all decisions follow the full chain. Some may be denied at approval, fail at execution, or not yet have measured outcomes.

## Data Model

### ProofEvent

Each node in the lineage is a `ProofEvent`:

```
ProofEvent:
  Id              : Guid
  TenantId        : Guid
  DecisionId      : Guid
  WorkflowId      : Guid? (links to hero workflows)
  EventType       : ProofEventType enum
  Actor           : string (user or system ID)
  Detail          : string? (human-readable description)
  ExpectedValue   : decimal?
  ActualValue     : decimal?
  Variance        : decimal?
  VariancePercent  : double?
  ActionType      : string? (e.g., "deploy", "pricing.adjust")
  IsSuccess       : bool?
  OverrideReason  : string?
  EconomicImpact  : decimal?
  ImpactAttribution: string? (what the impact is attributed to)
  OccurredAtUtc   : DateTimeOffset
```

### ProofTimeline

A timeline aggregates all proof events for a decision:

```
ProofTimeline:
  DecisionId    : Guid
  TenantId      : Guid
  DecisionTitle : string
  Domain        : string
  Events        : ProofEvent[] (ordered by OccurredAtUtc)
  Summary       : ProofTimelineSummary
```

### Timeline Summary

```
ProofTimelineSummary:
  TotalEvents                : int
  HasOutcome                 : bool
  WasOverridden              : bool
  WasReversed                : bool
  PredictedValue             : decimal?
  ActualValue                : decimal?
  Variance                   : decimal?
  VariancePercent            : double?
  FinalAssessment            : string?
  DecisionToOutcomeDuration  : TimeSpan?
```

## Querying Lineage

### Single Decision

```
GET /api/v1/proof-analytics/timeline/{decisionId}
```

Returns the full `ProofTimeline` with all events and summary.

### Workflow-Scoped

```
GET /api/v1/proof-analytics/workflow/{workflowId}/timelines
```

Returns timelines for all decisions linked to a hero workflow.

## Aggregated Views

### Predicted vs Actual

```
GET /api/v1/proof-analytics/predicted-vs-actual?domain=ops&limit=50
```

Returns a `PredictedVsActualSummary` with per-decision entries showing predicted value, actual value, variance, and direction.

### Variance Attribution

When `EconomicImpactAttributed` events are recorded, the system tracks what impact is attributed to AI-influenced decisions vs other factors. This is surfaced in timeline detail views.

## Integrity Guarantees

1. **Immutability**: Proof events cannot be modified after recording. Each event is appended, never updated.
2. **Ordering**: Events are always returned in chronological order by `OccurredAtUtc`.
3. **Tenant scoping**: All queries are scoped to the authenticated tenant's data.
4. **Auditability**: Each event records the `Actor` who triggered it.

## UI

The Proof Analytics view (`/proof-analytics`) provides:

1. **Dashboard**: KPI summary with accuracy rate, approval rate, execution success, override rate
2. **Predicted vs Actual table**: Clickable rows drill into individual decision timelines
3. **Approval conversion panel**: Funnel metrics from request to execution
4. **Execution trend bars**: Visual time-bucketed success/failure
5. **Override/reversal rates**: With reason distribution
6. **Trust analytics table**: Per-action-type grades

Clicking a decision row opens the **Timeline Detail** view showing the full lineage chain with event details, values, and variance.

## Honest Reporting

The system is designed to report honestly:

- Underperformance is shown alongside outperformance
- Override and reversal rates are prominently displayed
- Variance percentages are calculated before any rounding
- "Pending" decisions (no outcome yet) are clearly distinguished
- Attribution language avoids causal claims
