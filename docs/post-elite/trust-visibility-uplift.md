# Trust & Proof Visibility Uplift

**Date**: 2026-03-20
**Scope**: Backend composite APIs + frontend Trust Lineage view

---

## Problem

Trust-critical data was scattered across 4+ independent API endpoints (decisions,
governance, action-safety, outcomes, proof-analytics). Operators and diligence
reviewers had to mentally join this data. No single surface showed the full
decision→approval→execution→outcome trace.

## Changes

### Backend — Composite Trust Visibility Endpoints

**File**: `archonai/src/ArchonAI.Api/Endpoints/TrustVisibilityEndpoints.cs`

Two new read-only composite endpoints registered under `/api/v1/trust-visibility`:

| Endpoint | Purpose |
|---|---|
| `GET /lineage/{decisionId}` | Full decision-to-outcome trace: decision record, approval gates, governed actions with safety classification, predicted-vs-actual outcome, proof event timeline, and a lineage completeness summary |
| `GET /posture` | Tenant-wide governance health: active policies, approval rate, SoD enforcement, reversibility rate, rollback success rate, trust tier coverage, calibration summary, proof analytics dashboard |

Both require `GovernanceRead` authorization. They read from existing services —
no data duplication.

**Lineage response shape**:
- `decision` — title, domain, risk, reversibility, confidence, status
- `approvalGates[]` — status, requestedBy, reviewedBy, executionStatus
- `governedActions[]` — actionType, safety classification, rollback info
- `outcome` — expectedValue, actualValue, variance, variancePercent, isCalibrated
- `proofTimeline` — chronological events with actors, success flags, economic impact
- `lineageSummary` — boolean flags: hasApproval, hasExecution, hasOutcome, hasProofTrail, allActionsReversible, anyRollbackAttempted, varianceWithinThreshold

**Posture response shape**:
- `governance` — activePolicies, pendingApprovals, approvalRate, SoD
- `safety` — reversible/compensatable/irreversible counts, reversibilityRate, rollbackSuccessRate
- `trustTiers` — enabledPolicies, actionsCovered, requiresReversible
- `outcomes` — calibration summary
- `proofAnalytics` — dashboard data

### Frontend — Trust Lineage View

| File | Purpose |
|---|---|
| `archonai-ui/src/features/trust-lineage/TrustLineageView.tsx` | Two-tab UI: Trust Posture summary + Decision Lineage trace |
| `archonai-ui/src/features/trust-lineage/trust-lineage.css` | Styles for lineage view |
| `archonai-ui/src/features/trust-lineage/index.ts` | Barrel export |
| `archonai-ui/src/App.tsx` | Route `/trust-lineage` with `governance:read` gate |
| `archonai-ui/src/api/client.ts` | `getTrustLineage()` and `getTrustPosture()` API methods |

**Trust Posture tab**: governance KPIs (active policies, pending approvals,
approval rate, SoD), safety & reversibility rates, trust tier coverage.

**Decision Lineage tab**: enter a decision ID (supports deep-link via
`?decisionId=`). Shows:
- Decision header with risk/reversibility badges
- Lineage completeness chain: Approval → Execution → Outcome → Proof Trail
- Safety indicators (all-reversible, rollback attempted, variance threshold)
- Approval gate cards with execution status
- Governed action cards with safety classification and rollback window
- Predicted-vs-actual outcome grid (expected, actual, variance, calibration)
- Proof event timeline with actors, success/failure, economic impact
- Cross-links to Proof Analytics, Action Safety, Trust Tiers, Inspection

### Registration

- `Program.cs`: `v1.MapTrustVisibilityEndpoints()` added to API v1 group

## Visibility Improvements Summary

| Gap | Resolution |
|---|---|
| Decision-to-outcome trace | Lineage endpoint joins 5 services into one response |
| Approval-to-execution link | Approval gates resolved per governed action |
| Predicted vs actual variance | Outcome grid with variance % and calibration flag |
| Reversibility/safety indicators | Safety classification, rollback support, and window on every action |
| Trust-tier coverage | Posture endpoint includes tier policy counts |
| Proof event chronology | Timeline with actors, success flags, economic impact |
| Operator-friendly single view | Trust Lineage UI with two tabs and deep-link support |

## Residual Gaps

1. **Real-time posture updates** — posture endpoint is request/response; could benefit from SignalR push via ControlPlaneDashboardHub
2. **Historical posture snapshots** — no time-series storage of posture; operators cannot compare posture over time
3. **Lineage search by domain/agent** — currently requires exact decision ID; index-based search would improve discoverability
4. **PDF/export** — no export capability for diligence reviewers who need offline artifacts
