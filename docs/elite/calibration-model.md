# Calibration Model

Technical reference for the outcome learning and calibration system.

## Service Interface

`IOutcomeLearningService` (in `ArchonAI.Core.Interfaces`) with five operations:

```csharp
Task<OutcomeRecord> RecordExpectedOutcomeAsync(
    Guid decisionId, Guid tenantId,
    string? expectedSummary, decimal? expectedValue,
    double confidenceAtPrediction, string? expectedTimeframe,
    string recordedBy, CancellationToken ct);

Task<OutcomeRecord> RecordActualOutcomeAsync(
    Guid decisionId, string? actualSummary, decimal? actualValue,
    string? rootCause, string? notes, string recordedBy,
    CancellationToken ct);

Task<OutcomeRecord?> GetOutcomeAsync(Guid decisionId, CancellationToken ct);
Task<IReadOnlyList<OutcomeRecord>> ListOutcomesAsync(Guid tenantId, int limit, CancellationToken ct);
Task<CalibrationSummary> GetCalibrationSummaryAsync(Guid tenantId, string? domain, CancellationToken ct);
```

## API Endpoints

All under `/api/v1/outcomes`.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/expected` | OperatorOrAdmin | Record predicted outcome for a decision |
| POST | `/actual` | OperatorOrAdmin | Record actual outcome and compute variance |
| GET | `/{decisionId}` | GovernanceRead | Retrieve outcome record for a decision |
| GET | `/` | GovernanceRead | List outcome records for tenant |
| GET | `/calibration` | GovernanceRead | Aggregated calibration summary |

## Variance Computation

```
ValueVariance = ActualValue - ExpectedValue
VariancePercent = (ValueVariance / ExpectedValue) * 100

Direction:
  |VariancePercent| <= 10  → OnTarget
  ActualValue > ExpectedValue (beyond 10%) → Overperformed
  ActualValue < ExpectedValue (beyond 10%) → Underperformed
```

## Recalibration Signal Logic

```
if direction == OnTarget → ConfidenceCalibrated
if confidence >= 0.75 && direction == Underperformed:
    if |variance| > 50% → AssumptionInvalid
    else → ConfidenceInflated
if confidence < 0.50 && direction == Overperformed → ConfidenceDeflated
if |variance| > 40% → ValueModelDrift
else → None
```

## CalibrationSummary Model

```csharp
public sealed record CalibrationSummary(
    string TenantId,
    string? Domain,
    int TotalOutcomes,
    int OnTarget,
    int Overperformed,
    int Underperformed,
    double MeanConfidenceAtPrediction,
    double HitRate,
    double MeanVariancePercent,
    IReadOnlyDictionary<string, int> SignalDistribution);
```

## Event Bus Integration

Two event types published:
- `outcome.expected.recorded` — when a prediction is captured
- `outcome.actual.recorded` — when actual results are recorded, including direction, assessment, and signal metadata

## Tests

20 tests in `OutcomeLearningTests.cs`:

- Expected outcome recording + event emission
- Actual outcome variance computation (overperform, on-target, underperform)
- Error handling (actual without expected)
- Variance computation unit tests (positive, negative, null handling)
- Recalibration signal determination (5 scenarios: calibrated, inflated, deflated, value drift, assumption invalid)
- Tenant isolation
- Persistence and retrieval (store, null for unknown, updated after actual)
- Calibration summary aggregation
