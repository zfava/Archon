# Decision Engine

The decision engine provides structured, auditable business decision management with full lifecycle tracking, tenant isolation, and artifact linking.

## Domain Model

All types live in `ArchonAI.Core.Models.Decisions`.

| Type | Purpose |
|------|---------|
| `DecisionRecord` | Core decision with objective, constraints, assumptions, alternatives, risk, and confidence |
| `DecisionAlternative` | A considered option with pros, cons, estimated confidence, and estimated value |
| `DecisionLink` | Reference to an external artifact (workflow, approval gate, etc.) |
| `DecisionLifecycleEvent` | Timestamped audit entry for every state change |
| `DecisionStatus` | `Draft → Proposed → UnderReview → Approved → Rejected → Executing → Completed → Superseded` |
| `DecisionReversibility` | `FullyReversible`, `PartiallyReversible`, `Irreversible` |
| `DecisionRiskLevel` | `Low`, `Medium`, `High`, `Critical` |

### DecisionRecord Fields

| Field | Type | Description |
|-------|------|-------------|
| Id | Guid | Unique identifier |
| TenantId | Guid | Owning tenant (isolation boundary) |
| Title | string | Short decision title |
| Domain | string | Business domain (e.g. "finance", "operations") |
| Objective | string | What the decision aims to achieve |
| Constraints | string[] | Hard constraints bounding the decision |
| Assumptions | string[] | Working assumptions |
| Alternatives | DecisionAlternative[] | Options considered |
| RecommendedOptionId | string | ID of the recommended alternative |
| Confidence | double | 0.0–1.0 confidence score |
| Reversibility | enum | How reversible the decision is |
| RiskLevel | enum | Risk classification |
| ExpectedValue | decimal? | Expected monetary value |
| RequiresApproval | bool | Whether the decision needs explicit approval |
| LinkedArtifacts | DecisionLink[] | Attached artifacts |
| Status | enum | Current lifecycle status |
| CreatedBy | string | Actor who created the decision |
| CreatedAtUtc | DateTimeOffset | Creation timestamp |
| UpdatedAtUtc | DateTimeOffset | Last update timestamp |

## Service Interface

`IDecisionService` (in `ArchonAI.Core.Interfaces`) exposes six operations:

```csharp
Task<DecisionRecord> CreateAsync(DecisionRecord decision, CancellationToken ct = default);
Task<DecisionRecord?> GetAsync(Guid decisionId, CancellationToken ct = default);
Task<IReadOnlyList<DecisionRecord>> ListAsync(Guid tenantId, string? domain = null, DecisionStatus? status = null, int limit = 50, CancellationToken ct = default);
Task<DecisionRecord?> UpdateStatusAsync(Guid decisionId, DecisionStatus newStatus, string actor, string? detail = null, CancellationToken ct = default);
Task<DecisionRecord?> LinkArtifactAsync(Guid decisionId, DecisionLink link, CancellationToken ct = default);
Task<IReadOnlyList<DecisionLifecycleEvent>> GetHistoryAsync(Guid decisionId, CancellationToken ct = default);
```

The implementation (`DecisionService` in `ArchonAI.Api.Security`) uses `ConcurrentDictionary` for in-memory storage and publishes events via `IEventBus`.

## API Endpoints

All endpoints are under `/api/v1/decisions` and require `OperatorOrAdmin` authorization.

| Method | Path | Description |
|--------|------|-------------|
| POST | `/` | Create a decision |
| GET | `/` | List decisions (query: `tenantId`, `domain`, `status`, `limit`) |
| GET | `/{decisionId}` | Get a single decision |
| PUT | `/{decisionId}/status` | Update status (body: `newStatus`, `actor`, `detail`) |
| POST | `/{decisionId}/links` | Attach an artifact link |
| GET | `/{decisionId}/history` | Get lifecycle event history |

### Request DTOs

- `CreateDecisionRequest` — full decision payload with nested `CreateAlternativeRequest` items
- `UpdateDecisionStatusRequest` — `NewStatus`, `Actor`, optional `Detail`
- `CreateDecisionLinkRequest` — `ArtifactType`, `ArtifactId`, `Description`

## Frontend

The decisions UI lives at `/decisions` in the sidebar navigation (Operations section, "zap" icon).

**List view**: Filterable by status. Shows title, domain, status badge, risk level, confidence, and approval requirement. Click a row to open detail.

**Detail view**: Shows objective, constraints, assumptions, alternatives with pros/cons, linked artifacts, and full lifecycle history. The recommended alternative is highlighted.

## Tests

16 integration tests in `ArchonAI.Enterprise.Tests/Integration/DecisionEngineTests.cs`:

- Create and retrieve decisions
- Event emission on create
- Lifecycle status progression (Draft → Proposed → Approved → Executing → Completed)
- History event recording
- Tenant isolation (2 tests)
- Domain and status filtering
- Artifact linking (single, multiple, unknown decision)
- Data integrity (alternatives, constraints, assumptions)
- Edge cases (unknown IDs return null/empty)
