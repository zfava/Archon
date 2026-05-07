# Trust-Tiered Autonomy

ArchonAI uses a six-level trust tier model to control what the system may do autonomously. Each tier is a strict superset of the previous — granting Tier 3 implies Tiers 0-2 are also available.

## Tier Definitions

| Tier | Name | Behavior |
|------|------|----------|
| T0 | Observe Only | System records events and surfaces data. No recommendations or actions. |
| T1 | Recommend Only | System may propose actions with rationale. No drafts or execution. |
| T2 | Draft + Approval Required | System may draft action plans. Execution requires human approval. |
| T3 | Auto-Execute Reversible | System may execute actions that are reversible and within defined bounds. |
| T4 | Auto-Execute High Confidence | System may execute bounded actions (including irreversible) when confidence is high. |
| T5 | Policy Envelope | System operates with full autonomy within an approved policy envelope. |

## Design Principles

- **Default-deny**: Unknown action scopes default to Tier 0 (observe only). No action is auto-executed unless a policy explicitly allows it.
- **Tenant-scoped**: Policies are bound to tenants. One tenant's tier configuration does not affect another.
- **Guardrail gates**: Even when an action scope has a high max tier, individual evaluations can be downgraded by confidence thresholds, value ceilings, or reversibility requirements.
- **Behavioral, not decorative**: Trust tiers determine actual execution paths — observe, recommend, draft-for-approval, or auto-execute. They are not labels.

## Policy Model

Each `TrustTierPolicy` binds:

| Field | Purpose |
|-------|---------|
| ActionScope | What category of action this policy governs (e.g., `workflow.execute`, `connector.send`) |
| MaxTier | The highest tier allowed for this scope |
| ConfidenceThreshold | If set, actions with confidence below this value are downgraded to Tier 2 |
| ValueCeiling | If set, actions with estimated value above this amount are downgraded to Tier 2 |
| RequireReversible | If true, irreversible actions are downgraded to Tier 2 regardless of tier |
| TenantId | Owning tenant (or `__default__` for system-wide defaults) |

## Evaluation Logic

When the system evaluates an action:

1. Find the matching policy (tenant-specific overrides system defaults)
2. Cap the requested tier at the policy's max tier
3. Apply guardrail gates:
   - Confidence below threshold → downgrade to Tier 2
   - Value above ceiling → downgrade to Tier 2
   - Irreversible + policy requires reversible → downgrade to Tier 2
4. Determine disposition: `observe`, `recommend`, `draft_for_approval`, or `auto_execute`
5. Return whether the requested tier was allowed

## Default Policies

| Scope | Default Max Tier | Rationale |
|-------|-----------------|-----------|
| `workflow.execute` | T2 (Draft + Approval) | Workflow execution changes system state |
| `decision.execute` | T2 (Draft + Approval) | Decision execution has organizational impact |
| `connector.send` | T1 (Recommend Only) | External system writes are high-risk |
| `data.read` | T3 (Auto Reversible) | Read operations are inherently reversible |
| `notification.send` | T3 (Auto Reversible) | Notifications are low-risk, capped at $1K value |

## Integration Points

- **Decisions**: When viewing a decision detail, the UI evaluates the decision's trust tier and displays the disposition (observe/recommend/draft/auto-execute)
- **Governance**: Trust tiers complement the existing approval gate system — Tier 2 actions naturally flow into the governance approval workflow
- **Workflows**: Workflow execution can be gated by trust tier before entering the execution engine

## Administration

The Trust Tiers view (`/trust-tiers`) shows all active policies with their scopes, max tiers, confidence gates, value ceilings, and sources (system vs. tenant). Tenant administrators can create custom policies that override system defaults for their organization.
