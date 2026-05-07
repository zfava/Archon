# Operator Inspection Tooling

## Overview

The Operator Inspection Tooling provides deep introspection into ArchonAI's decision-making, policy enforcement, memory usage, and workflow execution. It makes the platform deeply inspectable and operationally trustworthy by allowing operators to understand exactly why ArchonAI made a decision, gated an action, changed a recommendation, or failed a workflow.

## Architecture

### Backend

- **Models**: `ArchonAI.Core.Models.Inspection` namespace
  - `DecisionRationaleBundle` — full inspection package for a decision
  - `PolicyEvaluationResult` — detailed policy rule-by-rule evaluation
  - `MemoryContextReference` — memory/context sources that influenced a decision
  - `WorkflowFailureDiagnostics` — failure/stall root-cause analysis
  - `InspectionSummary` — lightweight listing record

- **Service**: `InspectionService` (`ArchonAI.Api.Security`)
  - Composes from `IDecisionService`, `IHeroWorkflowService`, `IExceptionIntelligenceService`
  - All queries are tenant-scoped (enforced via `TenantId` checks)
  - Permission-gated via `GovernanceRead` authorization policy
  - Records policy evaluations, memory references, and workflow diagnostics for inspection

- **Interface**: `IInspectionService` (`ArchonAI.Core.Interfaces`)

### API Endpoints

All endpoints require authentication and `GovernanceRead` authorization.

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/v1/inspection/summaries` | List inspection summaries (filterable by subjectType, domain) |
| `GET` | `/api/v1/inspection/decisions/{id}/rationale` | Full decision rationale bundle |
| `GET` | `/api/v1/inspection/policy/{subjectType}/{subjectId}` | Policy evaluation result |
| `GET` | `/api/v1/inspection/memory/{subjectType}/{subjectId}` | Memory/context references |
| `GET` | `/api/v1/inspection/workflows/{id}/diagnostics` | Workflow failure diagnostics |

### Frontend

- **Route**: `/inspection` (AdminGated with `governance:read` permission)
- **Feature module**: `archonai-ui/src/features/inspection/`
- **Tabs**:
  - **Overview** — summary queue with filtering by type/domain
  - **Decision Rationale** — assumptions, constraints, alternatives, pros/cons, recommendation rationale
  - **Policy Inspection** — rule-by-rule evaluation, violations, approval state, override state
  - **Memory/Context** — memory sources used, relevance scores, usage context
  - **Workflow Diagnostics** — step-by-step diagnostics, failure category, suggested remediation

## Security Model

1. **Tenant isolation**: Every query is scoped to the requesting user's tenant. Cross-tenant access returns `null`/`404`.
2. **Permission gating**: All inspection endpoints require `GovernanceRead` authorization policy.
3. **No internal data leakage**: Inspection responses use operator-safe models that do not expose raw system internals.

## Integration Points

- **Executive Command Layer**: Inspection summaries can be surfaced in executive dashboards
- **Exception Intelligence Center**: Workflow diagnostics link to related exceptions
- **Proof Analytics**: Decision rationale bundles can be used as proof evidence
- **Hero Workflows**: Workflow diagnostics directly inspect hero workflow instances

## Data Flow

```
Decision/Action/Workflow Created
  → Policy evaluated → PolicyEvaluationResult recorded
  → Memory retrieved → MemoryContextReference recorded
  → Status changes → DecisionLifecycleEvent recorded
  → Failure/stall → WorkflowFailureDiagnostics built
  → Operator requests inspection → InspectionService composes bundle
  → Frontend renders inspection view
```

## Test Coverage

- `InspectionTenantIsolationTests` — 7 tests covering:
  - Cross-tenant decision rationale access prevention
  - Cross-tenant policy evaluation access prevention
  - Cross-tenant workflow diagnostics access prevention
  - Retrieval integrity (assumptions, alternatives, recommendations)
  - Memory reference integrity
  - Tenant-scoped summary listing
  - Nonexistent subject handling
