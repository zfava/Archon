# Rollback and Reversibility

## Overview

ArchonAI implements explicit rollback and reversibility primitives to ensure actions can be safely undone when possible, and that operators know upfront when they cannot.

The system does **not** pretend all actions are reversible. Every action carries an honest safety classification, and the UI surfaces this clearly before and after execution.

## Rollback Lifecycle

```
Action Executed
  → Status: RollbackEligible (if reversible/compensatable)
  → Status: Irreversible (if irreversible)

Rollback Attempted
  → Check: Is action irreversible? → Blocked
  → Check: Is rollback window expired? → Blocked
  → Check: Already rolled back? → Blocked
  → Execute rollback/compensation
    → Success → RolledBack / CompensationApplied
    → Failure → RollbackFailed
```

## Rollback Window

Actions with a rollback window have a time limit after execution during which rollback is permitted. Once the window expires, the system blocks rollback and sets the status to `RollbackWindowExpired`.

Windows are configured per action type in the safety classification:
- `strategy.override`: 8 hours
- `connector.disconnect`: 24 hours
- `decision.execute`: 2 hours
- `workflow.execute`: 4 hours

## Compensation vs Rollback

- **Rollback** (Reversible actions): The system restores the exact prior state.
- **Compensation** (Compensatable actions): A compensating action offsets the effect, but the original action remains on record.

For example, a deleted policy can be re-created with the same parameters (compensation), but the original policy ID and audit history are not restored.

## Integration with Trust Tiers

The trust tier system already has a `RequireReversible` guardrail on policies. When this is enabled:
- Only actions classified as `Reversible` are permitted for auto-execution
- `Compensatable` and `Irreversible` actions require human approval regardless of trust tier

## Integration with Proof Analytics

Successful rollbacks are recorded as `ReversalApplied` proof events in the decision-to-outcome lineage. Failed rollback attempts are also recorded to maintain a complete audit trail.

## API Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/v1/action-safety/classifications` | List all safety classifications |
| `GET` | `/api/v1/action-safety/classifications/{actionType}` | Get classification for action type |
| `PUT` | `/api/v1/action-safety/classifications` | Set/update a classification |
| `POST` | `/api/v1/action-safety/actions` | Record a governed action |
| `GET` | `/api/v1/action-safety/actions` | List governed actions (tenant-scoped) |
| `GET` | `/api/v1/action-safety/actions/{actionId}` | Get a specific action |
| `POST` | `/api/v1/action-safety/actions/{actionId}/rollback` | Trigger rollback |
| `GET` | `/api/v1/action-safety/summary` | Get rollback summary |

## Honest Defaults

Unknown action types default to `Irreversible` with no rollback support. This is the safe default — it is always safer to block rollback for an unclassified action than to allow it.

## Tenant Isolation

All governed action records and rollback summaries are scoped to the authenticated tenant. Cross-tenant rollback is not possible.
