namespace ArchonAI.Core.Models.Decisions;

/// <summary>
/// The expected economic consequences of a decision or action.
/// All monetary fields are nullable — partial information is valid and expected.
/// Ranges (low/high) are preferred over false precision.
/// </summary>
public sealed record FinancialConsequence(
    Guid Id,
    Guid DecisionId,
    Guid TenantId,

    // ── Revenue & Cost ────────────────────────────────────────
    decimal? ExpectedRevenueImpactLow,
    decimal? ExpectedRevenueImpactHigh,
    decimal? ExpectedCostImpactLow,
    decimal? ExpectedCostImpactHigh,
    decimal? ExpectedMarginImpact,

    // ── Cash & Labor ──────────────────────────────────────────
    string? ExpectedCashTimingImpact,
    string? LaborImpact,

    // ── Risk Profile ──────────────────────────────────────────
    decimal? DownsideRisk,
    decimal? UpsidePotential,

    // ── Derived Indicators ────────────────────────────────────
    double? ConfidenceAdjustment,
    decimal? RoiEstimateLow,
    decimal? RoiEstimateHigh,
    string? BreakEvenEstimate,

    // ── Assumptions & Notes ───────────────────────────────────
    IReadOnlyList<string> Assumptions,
    string? Notes,

    // ── Metadata ──────────────────────────────────────────────
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
