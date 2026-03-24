# Proof Analytics

## Overview

Proof Analytics provides a trust-building evidence layer for ArchonAI. It tracks the complete lifecycle of decisions from creation through execution to measured outcomes, enabling stakeholders to verify that AI-driven decisions produce real results.

## Core Concepts

### Proof Event

The atomic unit of proof. Each event captures one step in a decision's lifecycle:

| Event Type | Description |
|---|---|
| `DecisionCreated` | A new decision was registered |
| `RecommendationMade` | ArchonAI recommended a course of action |
| `ApprovalRequested` | Human approval was requested |
| `ApprovalGranted` | Approval was given |
| `ApprovalDenied` | Approval was denied |
| `ActionExecuted` | The decided action was executed |
| `ExpectedOutcomeRecorded` | Predicted outcome was recorded |
| `ActualOutcomeRecorded` | Actual outcome was observed and recorded |
| `VarianceComputed` | Variance between predicted and actual was calculated |
| `OverrideApplied` | A human override changed the AI recommendation |
| `ReversalApplied` | A previous action was reversed |
| `EconomicImpactAttributed` | Economic impact was attributed (not causal) |

### Proof Timeline

An ordered sequence of proof events for a single decision. Shows the complete chain from decision creation to outcome measurement.

### Economic Impact Attribution

Where economic impact data is available, we attribute it to decisions. This is **attribution, not causation**. The system does not claim that ArchonAI caused specific financial outcomes; it tracks the correlation between AI-influenced decisions and observed business results.

## Aggregations

### Predicted vs Actual

Compares expected outcomes against actual results across all decisions. Includes:
- Per-decision variance (absolute and percentage)
- Aggregate accuracy rate (on-target + overperformed / total with outcomes)
- Mean and median variance percentages

### Approval-to-Execution Conversion

Tracks the funnel from approval request to execution:
- Approval rate (granted / requested)
- Execution conversion rate (executed / granted)
- Mean approval latency
- Breakdown by action type

### Execution Trends

Time-bucketed success/failure rates for executed actions.

### Override & Reversal Rates

Tracks how often AI recommendations are overridden or reversed:
- Override rate (decisions overridden / total decisions)
- Reversal rate (decisions reversed / total decisions)
- Override reason distribution

### Trust Analytics by Action Type

Per-action-type trust scoring:
- Accuracy rate
- Override rate
- Mean confidence
- Mean variance
- Letter grade (A-F) based on accuracy and override rates

## API Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/v1/proof-analytics/events` | Record a proof event |
| `GET` | `/api/v1/proof-analytics/timeline/{decisionId}` | Get proof timeline for a decision |
| `GET` | `/api/v1/proof-analytics/workflow/{workflowId}/timelines` | Get timelines for a workflow |
| `GET` | `/api/v1/proof-analytics/predicted-vs-actual` | Predicted vs actual summary |
| `GET` | `/api/v1/proof-analytics/approval-conversion` | Approval-to-execution metrics |
| `GET` | `/api/v1/proof-analytics/execution-trends` | Execution trend buckets |
| `GET` | `/api/v1/proof-analytics/override-rates` | Override/reversal rates |
| `GET` | `/api/v1/proof-analytics/trust-analytics` | Trust grades by action type |
| `GET` | `/api/v1/proof-analytics/dashboard` | Full proof dashboard |

All endpoints require authentication. Read endpoints require `GovernanceRead` authorization. Write endpoints require `OperatorOrAdmin`.

## Trust Grading

| Grade | Accuracy | Override Rate |
|---|---|---|
| A | >= 90% | <= 5% |
| B | >= 75% | <= 15% |
| C | >= 60% | <= 25% |
| D | >= 40% | any |
| F | < 40% | any |

## Tenant Isolation

All proof data is scoped to the authenticated tenant. Cross-tenant data access is not possible through the API. The service filters events by `TenantId` on every aggregation query.

## Integration Points

- **Decision Engine**: Proof events are recorded when decisions are created, approved, or executed
- **Outcome Learning**: Actual outcomes feed into predicted vs actual comparisons
- **Hero Workflows**: Workflow-scoped timelines show proof across multi-step processes
- **Executive Command**: Dashboard data surfaces in executive summaries

## Limitations

- Economic impact is **attribution**, not proven causation
- Accuracy rates reflect correlation between predictions and outcomes, not AI quality in isolation
- Override rates can indicate either poor AI performance or conservative human governance
- Trust grades are heuristic; they should be interpreted in context
