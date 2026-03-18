# Outcome Learning

ArchonAI records expected and actual outcomes for decisions, computes variance, and generates recalibration signals. This forms the evidence base for improving prediction accuracy over time.

## How It Works

1. **Record expected outcome** — When a decision is made, the system captures what was predicted: expected value, confidence at prediction time, summary, and timeframe.
2. **Record actual outcome** — After execution, an operator or automated system records the actual result.
3. **Compute variance** — The system calculates the gap between expected and actual, both in absolute terms and percentage.
4. **Assess direction** — Was the outcome on target, overperformed, or underperformed?
5. **Generate recalibration signal** — Based on the confidence-outcome relationship, the system determines whether confidence needs adjustment.

## Outcome Model

Each `OutcomeRecord` links to a decision and tracks:

| Field | Purpose |
|-------|---------|
| ExpectedOutcomeSummary | Human-readable prediction |
| ExpectedValue | Numeric expected value (revenue, savings, etc.) |
| ConfidenceAtPrediction | How confident the system was when making the prediction |
| ActualOutcomeSummary | What actually happened |
| ActualValue | Numeric actual value |
| ValueVariance | Actual - Expected (absolute) |
| VariancePercent | Variance as percentage of expected |
| Direction | OnTarget / Overperformed / Underperformed |
| Assessment | AsExpected / BetterThanExpected / WorseThanExpected / CompletelyMissed |
| RecalibrationSignal | What calibration action is needed |
| RootCause | Why the variance occurred |

## Direction Thresholds

- **On Target**: Actual within 10% of expected
- **Overperformed**: Actual > 110% of expected
- **Underperformed**: Actual < 90% of expected

## Recalibration Signals

| Signal | Trigger | Meaning |
|--------|---------|---------|
| ConfidenceCalibrated | On target | Confidence model is working correctly |
| ConfidenceInflated | High confidence (>=75%) + underperformance | System was overconfident — reduce future confidence for similar decisions |
| ConfidenceDeflated | Low confidence (<50%) + overperformance | System was underconfident — increase future confidence |
| ValueModelDrift | Large variance (>40%) regardless of direction | Value estimation model is systematically off |
| AssumptionInvalid | High confidence + massive miss (>50%) | Underlying assumptions were wrong |

These signals are metadata — they surface patterns for human review and can feed into future calibration pipelines. The system does not automatically retrain models.

## Calibration Summary

The `/api/v1/outcomes/calibration` endpoint provides aggregated statistics:

- Total outcomes evaluated
- Distribution of on-target / overperformed / underperformed
- Mean confidence at prediction time
- Hit rate (on-target + overperformed as fraction of total)
- Mean variance percentage
- Distribution of recalibration signals

## Integration Points

- **Decision Engine**: Each outcome is linked to a `DecisionRecord` by ID. The decision's confidence, expected value, and domain provide the prediction-time context.
- **Financial Consequence Engine**: When a decision has both a financial consequence and an outcome record, operators can compare predicted financial impact (from FinancialConsequence) with actual results (from OutcomeRecord).
- **Trust Tier System**: Recalibration signals can inform trust tier policy adjustments — for example, if ConfidenceInflated signals are frequent for a scope, the tier might be lowered to require more approval.

## Frontend

The Decisions detail view shows an "Outcome Comparison" section when actual results are available:
- Side-by-side expected vs actual value
- Variance with percentage and color coding (green for overperformance, red for underperformance)
- Direction and assessment badges
- Recalibration signal indicator
- Root cause explanation when provided
