# Execution Trust Model

Technical reference for the trust-tiered autonomy implementation.

## Service Interface

`ITrustTierService` (in `ArchonAI.Core.Interfaces`) with six operations:

```csharp
Task<TrustTierEvaluation> EvaluateAsync(
    string tenantId, string actionScope, ExecutionTrustTier requestedTier,
    double? confidence, decimal? value, bool? reversible, CancellationToken ct);

Task<ExecutionTrustTier> GetEffectiveTierAsync(string tenantId, string actionScope, CancellationToken ct);
Task<IReadOnlyList<TrustTierPolicy>> ListPoliciesAsync(string tenantId, CancellationToken ct);
Task<TrustTierPolicy> SetPolicyAsync(TrustTierPolicy policy, CancellationToken ct);
Task<bool> DeletePolicyAsync(Guid policyId, string tenantId, CancellationToken ct);
Task<IReadOnlyDictionary<string, ExecutionTrustTier>> GetTierMapAsync(string tenantId, CancellationToken ct);
```

## API Endpoints

All under `/api/v1/trust-tiers`, requiring governance permissions.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/policies` | GovernanceRead | List all policies visible to tenant |
| POST | `/policies` | GovernanceWrite | Create or update a policy |
| DELETE | `/policies/{policyId}` | GovernanceWrite | Delete a tenant-owned policy |
| POST | `/evaluate` | GovernanceRead | Evaluate an action against tier policies |
| GET | `/map` | GovernanceRead | Get all action scopes with effective tiers |
| GET | `/effective/{actionScope}` | GovernanceRead | Get effective tier for a specific scope |

## Evaluation Result

`TrustTierEvaluation` contains:

| Field | Type | Description |
|-------|------|-------------|
| ActionScope | string | The evaluated action scope |
| RequestedTier | ExecutionTrustTier | What tier was requested |
| EffectiveTier | ExecutionTrustTier | The tier that applies after policy + guardrails |
| Allowed | bool | Whether the requested tier was granted |
| Disposition | string | One of: `observe`, `recommend`, `draft_for_approval`, `auto_execute`, `blocked` |
| Reason | string? | Explanation when the request is downgraded or blocked |

## Guardrail Behavior

```
Requested: T4 (AutoExecuteHighConfidence)
Policy Max: T4
Confidence: 0.60 (below threshold 0.85)
→ Downgraded to T2 (DraftApprovalRequired)
→ Allowed: false
→ Disposition: draft_for_approval
→ Reason: "Confidence 0.60 below threshold 0.85"
```

```
Requested: T3 (AutoExecuteReversible)
Policy Max: T3
Value: $5000 (above ceiling $1000)
→ Downgraded to T2
→ Allowed: false
```

```
Requested: T3 (AutoExecuteReversible)
Policy Max: T3
Reversible: false, RequireReversible: true
→ Downgraded to T2
→ Allowed: false
```

## Event Bus Integration

Policy changes emit `trust_tier.policy.set` events with scope and tier metadata, enabling downstream audit and notification systems.

## Tests

19 tests in `TrustTierTests.cs`:

- Tier enforcement: block when exceeds max, allow when within, exact match
- Approval flows: draft-for-approval disposition, observe-only for unknown scopes
- Confidence gate: downgrade on low confidence, allow on high confidence
- Value ceiling: downgrade when exceeds ceiling, allow when below
- Reversibility gate: downgrade irreversible when policy requires reversible
- Tenant isolation: override defaults per tenant, cross-tenant isolation, delete protection
- Policy CRUD: create/retrieve, event emission
- Tier map: returns all scopes, tenant overrides visible
- Effective tier: default for unknown scopes, returns policy tier

## Frontend

- **Trust Tiers admin view** (`/trust-tiers`): Policy table with action scope, max tier, confidence gate, value ceiling, reversibility requirement, and source indicator
- **Decision detail integration**: Trust tier evaluation badge showing disposition (observe/recommend/draft/auto-execute) based on the decision's confidence and reversibility
