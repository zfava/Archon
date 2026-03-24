# Decision Lifecycle

Decisions follow a defined state machine. Each transition is recorded as a `DecisionLifecycleEvent` with the actor, timestamp, and optional detail.

## Status Flow

```
Draft → Proposed → UnderReview → Approved → Executing → Completed
                              ↘ Rejected
                                         Any → Superseded
```

| Status | Meaning |
|--------|---------|
| Draft | Initial state. Decision is being formulated. |
| Proposed | Decision has been submitted for consideration. |
| UnderReview | Active review by stakeholders or approvers. |
| Approved | Decision has been approved and is ready for execution. |
| Rejected | Decision was rejected with optional reason in `detail`. |
| Executing | Approved decision is being carried out. |
| Completed | Execution finished. Terminal state. |
| Superseded | Replaced by a newer decision. Can occur from any state. |

## Lifecycle Events

Every state change creates a `DecisionLifecycleEvent`:

```
EventType: "decision.status.{newStatus}"
Actor: the user or system that triggered the change
Detail: optional reason or comment
OccurredAtUtc: server timestamp
```

Additional event types:
- `decision.created` — emitted when a decision is first stored
- `decision.artifact.linked` — emitted when an artifact is linked

## Event Bus Integration

Each status change also publishes a `SystemEvent` to `IEventBus` with:
- `EventType`: `"decision.status.changed"`
- `Source`: `"DecisionService"`
- `Payload`: includes `decisionId`, `newStatus`, and `actor`

This allows downstream systems (workflows, notifications, audit) to react to decision state changes.

## Tenant Isolation

All list operations are scoped by `TenantId`. A tenant can only see and modify their own decisions. The service filters at the data layer — there is no cross-tenant access path.

## Artifact Linking

Decisions can be linked to external artifacts (workflows, approval gates, reports) via `DecisionLink`. Each link records:
- `ArtifactType` — category of the linked item (e.g. "workflow", "approval")
- `ArtifactId` — external identifier
- `Description` — human-readable context
- `LinkedAtUtc` — when the link was created

Links are append-only and recorded in lifecycle history.
