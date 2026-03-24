# Action Safety Model

## Overview

The action safety model provides a classification system for every action ArchonAI can execute. Each classification describes whether the action is reversible, how rollback works, and what operators should know.

## Classification Fields

| Field | Type | Description |
|---|---|---|
| `actionType` | string | The action being classified (e.g., `decision.execute`) |
| `reversibility` | enum | `Reversible`, `Compensatable`, or `Irreversible` |
| `rollbackSupported` | bool | Whether automated rollback is available |
| `rollbackStrategy` | enum | `None`, `Automatic`, `ManualTrigger`, `OutOfBand`, `Compensation` |
| `rollbackWindow` | TimeSpan? | Time after execution during which rollback is permitted |
| `compensationDescription` | string? | What the compensation action does (for Compensatable actions) |
| `operatorNotes` | string? | Honest notes about limitations and caveats |
| `classifiedBy` | string | Who/what set this classification |

## Reversibility Levels

### Reversible
The action can be fully undone, restoring the exact prior state. Examples:
- `data.read` — No state change to reverse
- `strategy.override` — Revert to prior strategy version
- `connector.disconnect` — Reconnect restores state

### Compensatable
The action cannot be truly undone, but a compensating action can offset its effects. The original action record is preserved. Examples:
- `decision.execute` — Revert status, but downstream actions remain
- `workflow.execute` — Cancel and revert artifacts where possible
- `policy.delete` — Re-create with same parameters (new ID)
- `rbac.role.delete` — Re-create role, reassign users

### Irreversible
The action cannot be undone or compensated. Examples:
- `notification.send` — Messages cannot be recalled
- `connector.send` — Data sent to external systems cannot be recalled
- `workflow.cancel` — Cancelled workflows cannot be resumed

## Rollback Strategies

| Strategy | Description |
|---|---|
| `None` | No rollback mechanism exists |
| `Automatic` | System reverses the action without human involvement |
| `ManualTrigger` | Operator initiates rollback through the system |
| `OutOfBand` | Requires intervention outside ArchonAI (e.g., vendor support) |
| `Compensation` | A compensating action is executed instead of true reversal |

## Default Classifications

| Action Type | Reversibility | Rollback | Strategy | Window |
|---|---|---|---|---|
| `data.read` | Reversible | Yes | Automatic | 24h |
| `notification.send` | Irreversible | No | None | — |
| `workflow.execute` | Compensatable | No | Compensation | 4h |
| `decision.execute` | Compensatable | No | Compensation | 2h |
| `connector.send` | Irreversible | No | None | — |
| `connector.disconnect` | Reversible | Yes | ManualTrigger | 24h |
| `strategy.override` | Reversible | Yes | Automatic | 8h |
| `policy.delete` | Compensatable | No | Compensation | 1h |
| `workflow.cancel` | Irreversible | No | None | — |
| `rbac.role.delete` | Compensatable | No | Compensation | 2h |

## Governed Action Record

Each executed action creates a `GovernedActionRecord` that tracks:
- Safety classification at time of execution
- Current status (RollbackEligible, Irreversible, RolledBack, etc.)
- Links to decision, workflow, and approval gate
- Full rollback attempt history
- Compensation outcome (if applicable)

## State Machine

```
Executed → RollbackEligible (if reversible/compensatable + rollback supported)
Executed → Irreversible (if irreversible)

RollbackEligible → RolledBack (successful automatic/manual rollback)
RollbackEligible → CompensationApplied (successful compensation)
RollbackEligible → RollbackFailed (rollback attempt failed)
RollbackEligible → RollbackWindowExpired (window elapsed)

RollbackWindowExpired → (terminal)
RolledBack → (terminal)
CompensationApplied → (terminal)
Irreversible → (terminal)
```

## Safety Summary

The `safetySummary` field provides a human-readable one-liner:
- "Reversible. Rollback via Automatic."
- "Not directly reversible. Compensation: Revert decision status to prior state."
- "Irreversible. No rollback or compensation available."

## Design Principles

1. **Honest defaults**: Unknown actions are classified as irreversible
2. **No fake undo**: If something cannot be undone, the system says so
3. **Operator visibility**: Safety classification is shown before and after execution
4. **Audit trail**: Every rollback attempt is recorded, including blocked attempts
5. **Time-bounded**: Rollback windows prevent indefinite reversibility claims
