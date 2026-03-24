# Policy Simulation

## Overview

Policy simulation provides a **dry-run mode** for all governed actions in ArchonAI. Operators can preview exactly what would happen — which decisions would be created, what trust tier applies, whether approval is required, what economic impact is projected, and which workflow steps would execute — all **without mutating any state**.

Every simulation is a read-only projection against the full governance stack. No decisions are created, no approvals are requested, no events are published, no artifacts are stored.

## Architecture

### Design Principles

1. **Zero side effects** — simulation calls only read-only service methods (`EvaluateAsync`, `RequiresApprovalAsync`, `ListApprovalPoliciesAsync`, `GetDefinitionAsync`). No writes, no events.
2. **Real policy evaluation** — simulation uses the same governance stack as production execution. Results reflect actual policy behavior, not approximations.
3. **Explicit and inspectable** — every policy outcome, trust-tier evaluation, and approval requirement is surfaced with explanations.
4. **Composable** — simulation integrates with the decision engine, trust-tier autonomy, governance/approvals, financial consequence engine, and hero workflows.

### Simulation Pipeline

```
Request → 1. Simulate Decision
        → 2. Evaluate Trust Tier (read-only)
        → 3. Check Approval Requirements (read-only)
        → 4. Project Economic Effect
        → 5. Preview Workflow Steps
        → 6. Determine Verdict
        → SimulationResult
```

Each step produces structured output that feeds into the final `SimulationResult`.

### Verdict Determination

| Trust Disposition | Approval Required | Verdict |
|---|---|---|
| `AutoExecute` | No | **Allowed** |
| `AutoExecute` | Yes | **RequiresApproval** |
| `DraftForApproval` | — | **RequiresApproval** |
| `Recommend` | — | **RecommendOnly** |
| `Observe` | — | **ObserveOnly** |
| Not allowed | — | **Blocked** |

## Data Model

### SimulationRequest

| Field | Type | Description |
|---|---|---|
| `TenantId` | `Guid` | Tenant context |
| `ActionType` | `string` | The action being simulated |
| `ActionScope` | `string` | Scope for trust-tier evaluation |
| `Title` | `string` | Human-readable title |
| `Domain` | `string?` | Business domain |
| `RiskLevel` | `string?` | Low / Medium / High / Critical |
| `Reversibility` | `string?` | FullyReversible / PartiallyReversible / Irreversible |
| `Confidence` | `double?` | Confidence level (0–1) |
| `ExpectedValue` | `decimal?` | Expected monetary value |
| `RevenueImpactLow/High` | `decimal?` | Revenue impact range |
| `CostImpactLow/High` | `decimal?` | Cost impact range |
| `DownsideRisk` | `decimal?` | Downside risk exposure |
| `UpsidePotential` | `decimal?` | Upside potential |
| `RequestedTier` | `string?` | Desired trust tier |
| `WorkflowType` | `string?` | Hero workflow to preview |
| `RequestedBy` | `string` | User who requested the simulation |

### SimulationResult

| Section | Type | Description |
|---|---|---|
| `Verdict` | `SimulationVerdict` | Overall outcome: Allowed, RequiresApproval, Blocked, RecommendOnly, ObserveOnly |
| `Decision` | `SimulatedDecision` | What decision would be created |
| `TrustTierOutcome` | `TrustTierEvaluation` | Trust-tier evaluation result |
| `ApprovalRequirement` | `SimulatedApproval` | Whether and from whom approval is required |
| `PolicyOutcomes` | `List<PolicyOutcome>` | Per-policy pass/fail with explanations |
| `EconomicEffect` | `SimulatedEconomicEffect?` | Projected financial impact |
| `WorkflowPreview` | `SimulatedWorkflowPreview?` | Step-by-step workflow projection |
| `Reasons` | `List<string>` | Human-readable rationale chain |

## API

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api/v1/policy-simulation/simulate` | `GovernanceRead` | Run a dry-run simulation |
| `GET` | `/api/v1/policy-simulation/{id}` | `GovernanceRead` | Retrieve a simulation result |
| `GET` | `/api/v1/policy-simulation` | `GovernanceRead` | List simulation results for the tenant |

### Running a Simulation

```json
POST /api/v1/policy-simulation/simulate
{
  "actionType": "vendor-selection",
  "title": "Q1 Cloud Provider Evaluation",
  "domain": "procurement",
  "riskLevel": "High",
  "confidence": 0.75,
  "expectedValue": 500000,
  "requestedTier": "DraftApprovalRequired",
  "workflowType": "vendor-selection",
  "revenueImpactLow": 200000,
  "revenueImpactHigh": 800000,
  "costImpactLow": 100000,
  "costImpactHigh": 300000
}
```

### Response Structure

```json
{
  "id": "...",
  "verdict": "RequiresApproval",
  "decision": {
    "title": "Q1 Cloud Provider Evaluation",
    "riskLevel": "High",
    "wouldRequireApproval": true
  },
  "trustTierOutcome": {
    "allowed": true,
    "disposition": "DraftForApproval",
    "effectiveTier": "DraftApprovalRequired"
  },
  "approvalRequirement": {
    "required": true,
    "explanation": "Trust tier disposition is 'draft_for_approval'; Decision risk level (High/Critical) requires approval."
  },
  "policyOutcomes": [
    { "policyName": "RiskLevelPolicy", "passed": false, "explanation": "Risk level 'High' exceeds auto-approval threshold." },
    { "policyName": "TrustTierPolicy", "passed": true, "explanation": "Action allowed at tier 'DraftApprovalRequired'." },
    { "policyName": "ApprovalPolicy", "passed": false, "explanation": "..." }
  ],
  "economicEffect": {
    "netImpactLow": -100000,
    "netImpactHigh": 700000
  },
  "workflowPreview": {
    "workflowType": "vendor-selection",
    "totalSteps": 7,
    "steps": [...]
  },
  "reasons": ["..."]
}
```

## Frontend

The simulation view (`/simulation`) provides:

1. **Simulate tab** — Form to configure simulation parameters; run dry-run and view detailed results
2. **History tab** — Browse previous simulation results with verdict indicators

### Result Display

- **Verdict banner** — Color-coded (green=Allowed, yellow=RequiresApproval, red=Blocked, blue=RecommendOnly, purple=ObserveOnly)
- **Policy outcomes** — Per-policy pass/fail with explanations
- **Decision preview** — What decision record would be created
- **Trust tier evaluation** — Disposition, effective tier, reason
- **Approval requirement** — Required role, separation of duties, matched policy
- **Economic projection** — Revenue/cost ranges, net impact, downside risk
- **Workflow preview** — Step-by-step projection with projected outcomes
- **Rationale** — Full chain of reasons explaining the verdict

## Test Coverage

28 integration tests covering:

- Basic simulation result structure and population
- Verdict determination (Allowed for low risk, RequiresApproval for high/critical)
- **No side effects**: no decisions created, no consequences created, no events published, no approval gates created
- Tenant isolation on get and list operations
- Economic effect calculation accuracy (net impact = revenue - cost)
- Workflow preview integration (7 steps for vendor-selection, 8 for compliance)
- Policy outcome accuracy (risk, trust-tier, approval policies)
- Default handling (invalid risk level defaults to Medium, confidence clamped to 0–1)
- List ordering (most recent first) and limit enforcement
- Unknown ID returns null
