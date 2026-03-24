# Trust Lineage — Frontend Contract Reference

## Overview

The Trust Lineage feature traces every decision from approval through execution to measured outcome. It consumes two backend endpoints exposed by `TrustVisibilityEndpoints.cs`.

## Endpoints

| Endpoint | Method | Auth Policy | Frontend Method |
|---|---|---|---|
| `/api/v1/trust-visibility/lineage/{decisionId}` | GET | `GovernanceRead` | `api.getTrustLineage(decisionId)` |
| `/api/v1/trust-visibility/posture` | GET | `GovernanceRead` | `api.getTrustPosture()` |

## Response Types

All TypeScript types live in `src/features/trust-lineage/trust-lineage.types.ts`.

### `TrustLineageResponse`

Top-level response for decision lineage lookup.

| Field | Type | Description |
|---|---|---|
| `decisionId` | `string` | UUID of the decision |
| `decision` | `TrustLineageDecision` | Core decision metadata |
| `approvalGates` | `ApprovalGate[]` | Linked approval gates |
| `governedActions` | `GovernedAction[]` | Actions linked to this decision |
| `outcome` | `LineageOutcome \| null` | Predicted vs actual outcome |
| `proofTimeline` | `ProofTimeline \| null` | Proof event trail |
| `statusHistory` | `unknown[]` | Decision status transitions |
| `lineageSummary` | `LineageSummary` | Completeness & safety flags |

### `TrustPostureResponse`

Tenant-wide governance health summary.

| Field | Type | Description |
|---|---|---|
| `tenantId` | `string` | Tenant UUID |
| `generatedAtUtc` | `string` | ISO timestamp |
| `governance` | `GovernancePosture` | Policy and approval metrics |
| `safety` | `SafetyPosture` | Reversibility and rollback stats |
| `trustTiers` | `TrustTierPosture` | Trust tier policy coverage |
| `outcomes` | `unknown` | Calibration summary (opaque) |
| `proofAnalytics` | `unknown` | Proof dashboard (opaque) |

### String Union Types

These match the backend enum `.ToString()` output:

- `ReversibilityLevel`: `'Reversible' | 'Compensatable' | 'Irreversible'`
- `RiskLevel`: `'Low' | 'Medium' | 'High'`
- `DecisionStatus`: `'Proposed' | 'Approved' | 'InProgress' | 'Completed' | 'Cancelled'`
- `ApprovalGateStatus`: `'Pending' | 'Approved' | 'Denied'`
- `ExecutionStatus`: `'NotExecuted' | 'Succeeded' | 'Failed'`
- `OutcomeDirection`: `'Positive' | 'Negative' | 'Neutral'`
- `OutcomeAssessment`: `'Accurate' | 'Overestimated' | 'Underestimated' | 'WildlyOff'`

## Component Architecture

```
TrustLineageView (exported)
├── PostureSummary        — tenant-wide governance KPIs
└── LineageDetail          — single decision trace
    ├── Decision Header    — title, domain, risk, reversibility badges
    ├── Completeness Chain — approval → execution → outcome → proof
    ├── Safety Indicators  — reversibility status, rollback, variance
    ├── Approval Gates     — gate cards with status, review, execution
    ├── Governed Actions   — action cards with safety classification
    ├── Outcome Grid       — expected vs actual with variance
    └── Proof Timeline     — chronological event trail
```

## Testing

Contract tests live in `src/features/trust-lineage/__tests__/trust-lineage-contracts.test.ts`.

Run: `npx vitest run src/features/trust-lineage/__tests__/`

Tests cover:
- Valid minimal and full lineage response shapes
- Posture response data integrity (approval rate, safety count sums)
- Lineage summary phase states
- Approval gate lifecycle states (pending, approved, denied)
- Null handling for optional fields
