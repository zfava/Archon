namespace ArchonAI.Core.Models.Decisions;

/// <summary>
/// Records the predicted outcome of a decision/action, the actual outcome after execution,
/// and the variance between them — forming the basis for calibration intelligence.
/// </summary>
public sealed record OutcomeRecord(
    Guid Id,
    Guid DecisionId,
    Guid TenantId,

    // ── Expected (at prediction time) ─────────────────────────
    string? ExpectedOutcomeSummary,
    decimal? ExpectedValue,
    double ConfidenceAtPrediction,
    string? ExpectedTimeframe,

    // ── Actual (post-execution) ───────────────────────────────
    string? ActualOutcomeSummary,
    decimal? ActualValue,
    DateTimeOffset? OutcomeObservedAtUtc,

    // ── Variance ──────────────────────────────────────────────
    decimal? ValueVariance,
    double? VariancePercent,
    OutcomeDirection Direction,

    // ── Assessment ────────────────────────────────────────────
    string? RootCause,
    string? Notes,
    OutcomeAssessment Assessment,

    // ── Recalibration ─────────────────────────────────────────
    RecalibrationSignal RecalibrationSignal,

    // ── Metadata ──────────────────────────────────────────────
    string RecordedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Whether actual outcome overshot, undershot, or matched the prediction.</summary>
public enum OutcomeDirection
{
    Pending,
    OnTarget,
    Overperformed,
    Underperformed,
}

/// <summary>High-level assessment of the outcome quality.</summary>
public enum OutcomeAssessment
{
    Pending,
    AsExpected,
    BetterThanExpected,
    WorseThanExpected,
    CompletelyMissed,
}

/// <summary>Signal for downstream calibration systems.</summary>
public enum RecalibrationSignal
{
    /// <summary>No calibration change needed.</summary>
    None,
    /// <summary>Confidence was well-calibrated — reinforce.</summary>
    ConfidenceCalibrated,
    /// <summary>Confidence was too high relative to outcome — reduce future confidence for similar scope.</summary>
    ConfidenceInflated,
    /// <summary>Confidence was too low relative to outcome — increase future confidence for similar scope.</summary>
    ConfidenceDeflated,
    /// <summary>Value estimation was systematically off — flag for value model review.</summary>
    ValueModelDrift,
    /// <summary>Scope or domain assumptions were invalid.</summary>
    AssumptionInvalid,
}

/// <summary>
/// Aggregated calibration summary for a tenant/domain.
/// </summary>
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
