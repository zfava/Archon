# Financial Consequence Engine

Attaches structured economic consequence modeling to decisions, enabling ArchonAI to represent the expected financial impact of important business decisions.

## Design Principles

- **Ranges over false precision**: Revenue, cost, and ROI fields use low/high bounds rather than single-point estimates.
- **Partial data is valid**: Every monetary field is nullable. A consequence record with only downside risk and labor impact is perfectly acceptable.
- **Assumptions are explicit**: Every consequence carries a list of assumptions so reviewers know what the numbers depend on.
- **No fake predictions**: The system stores human-supplied or model-supplied estimates with explicit confidence adjustments — it does not pretend to forecast exact outcomes.

## Domain Model

`FinancialConsequence` — defined in `ArchonAI.Core.Models.Decisions.FinancialConsequenceModels`

| Field | Type | Description |
|-------|------|-------------|
| Id | Guid | Unique identifier |
| DecisionId | Guid | Parent decision |
| TenantId | Guid | Owning tenant |
| ExpectedRevenueImpactLow | decimal? | Lower bound of expected revenue change |
| ExpectedRevenueImpactHigh | decimal? | Upper bound of expected revenue change |
| ExpectedCostImpactLow | decimal? | Lower bound of expected cost change (negative = savings) |
| ExpectedCostImpactHigh | decimal? | Upper bound of expected cost change |
| ExpectedMarginImpact | decimal? | Net margin effect |
| ExpectedCashTimingImpact | string? | Description of cash flow timing changes |
| LaborImpact | string? | Description of FTE / headcount effect |
| DownsideRisk | decimal? | Worst-case monetary exposure |
| UpsidePotential | decimal? | Best-case monetary upside |
| ConfidenceAdjustment | double? | 0.0–1.0 multiplier reflecting estimate quality |
| RoiEstimateLow | decimal? | Lower bound ROI |
| RoiEstimateHigh | decimal? | Upper bound ROI |
| BreakEvenEstimate | string? | Human-readable break-even timeline |
| Assumptions | string[] | What the numbers depend on |
| Notes | string? | Free-form analyst commentary |
| CreatedBy | string | Who created the record |
| CreatedAtUtc | DateTimeOffset | Creation timestamp |
| UpdatedAtUtc | DateTimeOffset | Last update timestamp |

## Service

`IFinancialConsequenceService` (in `ArchonAI.Core.Interfaces`) with three operations:

```csharp
Task<FinancialConsequence> AttachAsync(FinancialConsequence consequence, CancellationToken ct);
Task<FinancialConsequence?> GetByDecisionAsync(Guid decisionId, CancellationToken ct);
Task<FinancialConsequence?> UpdateAsync(Guid decisionId, FinancialConsequence consequence, CancellationToken ct);
```

Implementation uses `ConcurrentDictionary` keyed by `DecisionId` (one consequence per decision). Events are published to `IEventBus`:
- `decision.financial_consequence.attached` — on initial attach
- `decision.financial_consequence.updated` — on update

## API Endpoints

All under `/api/v1/decisions/{decisionId}/financial-consequence`, requiring `OperatorOrAdmin` authorization.

| Method | Path | Description |
|--------|------|-------------|
| POST | `/{decisionId}/financial-consequence` | Attach a consequence to a decision |
| GET | `/{decisionId}/financial-consequence` | Retrieve the consequence for a decision |
| PUT | `/{decisionId}/financial-consequence` | Update the consequence model |

### Request DTO

`AttachFinancialConsequenceRequest` — all fields are nullable. Send only the fields you have data for.

## Frontend

The financial consequence section renders in the decision detail view when a consequence record exists. It displays:

- **Impact cards**: Revenue, cost, margin, downside risk, upside potential, ROI, confidence, break-even — each in a compact card with range formatting
- **Detail rows**: Cash timing impact, labor impact as descriptive text
- **Assumptions**: Displayed as tags
- **Notes**: Free-form commentary

Values are color-coded: green for positive impacts, red for negative. Ranges display as "$50K – $120K" format with automatic K/M scaling.

## Tests

11 tests in `FinancialConsequenceTests.cs`:

- Attach and retrieve consequence
- Event emission on attach
- Get returns attached data with all fields preserved
- Get returns null for unknown decision
- Update modifies existing consequence
- Update returns null for unknown decision
- Update emits event
- Handles fully null/partial data safely
- Preserves assumptions and notes
- Tenant isolation (consequences scoped by decision)
- Overwrite behavior (re-attach replaces previous)

## Integration with Decision Engine

The financial consequence is a **companion record** to a `DecisionRecord`, linked by `DecisionId`. This design:
- Keeps the decision model clean and focused on the decision itself
- Allows financial modeling to be added, updated, or absent without affecting decision lifecycle
- Supports decisions that don't have financial implications (operational, organizational)
- Enables independent evolution of the financial model

## Residual Items

- **Aggregate views**: Sum financial consequences across a domain or tenant for portfolio-level analysis
- **Scenario modeling**: Multiple consequence records per decision for best/base/worst case
- **Automated scoring**: Feed decision alternatives through a scoring model to pre-populate consequence estimates
- **Audit trail**: Full history of consequence changes (currently only latest state is stored)
