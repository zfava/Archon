# Governance Model

## Overview

ArchonAI's governance model enforces approval gates on high-risk actions, separation of duties, and maintains an audit-linked approval history. This ensures that destructive or sensitive operations require explicit human authorization before execution.

## Approval Gate Flow

```
Operator requests high-risk action
    │
    ▼
System checks: Does this action type have an ApprovalPolicy?
    │
    ├─ No  → Action proceeds immediately
    │
    └─ Yes → Create ApprovalGate (status: Pending)
              │
              ▼
         Admin reviews (POST /governance/{id}/review)
              │
              ├─ Tenant isolation check:
              │    Reviewer's tenant_id must match gate's TenantId
              │    → Mismatch returns 404 (prevents cross-tenant access)
              │
              ├─ Required approver role check:
              │    Reviewer's JWT role must match policy.RequiredApproverRole
              │    → Mismatch returns 403
              │
              ├─ Separation of duties check:
              │    If policy.RequireSeparationOfDuties = true
              │    AND reviewer == requester → REJECT (400)
              │
              ├─ Approved → Action may proceed
              │              Audit entry recorded
              │
              └─ Denied → Action blocked
                          Audit entry recorded
```

## Default Approval Policies

| Action Type | Description | Approver Role | Separation of Duties |
|-------------|-------------|---------------|:--------------------:|
| `workflow.cancel` | Cancelling a running workflow | Admin | Yes |
| `policy.delete` | Deleting a governance policy | Admin | Yes |
| `rbac.role.delete` | Deleting an RBAC role | Admin | No |
| `connector.disconnect` | Disconnecting an active integration | Admin | Yes |
| `strategy.override` | Overriding an AI-selected strategy | Admin | Yes |

## API Endpoints

| Method | Path | Policy | Description |
|--------|------|--------|-------------|
| GET | `/governance/policies` | GovernanceRead | List all approval policies |
| POST | `/governance/policies` | GovernanceWrite | Create a new approval policy |
| POST | `/governance/request` | OperatorOrAdmin | Request approval for an action |
| GET | `/governance/pending` | GovernanceRead | List pending approvals for caller's tenant |
| GET | `/governance/{id}` | GovernanceRead | Get a specific approval gate |
| POST | `/governance/{id}/review` | GovernanceApprove | Approve or deny a request |
| GET | `/governance/check/{actionType}` | GovernanceRead | Check if an action requires approval |
| GET | `/governance/history` | GovernanceRead | Query approval audit history |

## Separation of Duties

When an `ApprovalPolicy` has `RequireSeparationOfDuties = true`, the system enforces that:
- The person who **requests** approval cannot be the person who **reviews** it
- This is enforced at the `GovernanceService` level with an `InvalidOperationException`
- The check compares `RequestedBy` with `ReviewedBy` identifiers

## Audit Trail

Every approval decision (approve or deny) creates an `ApprovalAuditEntry`:

| Field | Description |
|-------|-------------|
| `ApprovalGateId` | Link to the original request |
| `ActionType` | What action was being gated |
| `TenantId` | Which organization |
| `RequestedBy` | Who requested the action |
| `ReviewedBy` | Who reviewed the request |
| `Outcome` | Approved or Denied |
| `OccurredAtUtc` | When the decision was made |

The history endpoint supports filtering by tenant and action type.

## Wired Actions

The following endpoints are gated behind governance approval checks in production:

| Endpoint | Action Type | Behavior |
|----------|-------------|----------|
| `POST /api/v1/admin/workflows/{id}/cancel` | `workflow.cancel` | Returns 202 Accepted with approval gate |
| `POST /api/v1/integrations/{id}/disconnect` | `connector.disconnect` | Returns 202 Accepted with approval gate |
| `POST /api/v1/human-overrides/modify-strategy` | `strategy.override` | Returns 202 Accepted with approval gate |

When an approval policy exists for the action type, the endpoint creates an `ApprovalGate` and returns HTTP 202 with the gate details. The caller must wait for admin approval before the action proceeds.

## Integration with Application Logic

To gate an action behind approval:

```csharp
// Check if approval is needed
if (await governance.RequiresApprovalAsync("workflow.cancel"))
{
    var gate = await governance.RequestApprovalAsync(
        "workflow.cancel", workflowId, tenantId, userId, justification);
    // Return gate ID to caller; they must poll or wait for approval
    return Results.Accepted(gate);
}

// No approval needed; proceed
await workflowService.CancelAsync(workflowId);
```

## Areas for Future Hardening

- **Approval expiry**: Pending gates should auto-expire after a configurable period
- **Escalation**: If no reviewer acts within N hours, escalate to next-level admin
- **Webhook notifications**: Notify reviewers via Slack/email when approval is requested
- **Multi-approver**: Require N-of-M approvers for critical actions
- **Policy versioning**: Track policy changes over time for compliance
