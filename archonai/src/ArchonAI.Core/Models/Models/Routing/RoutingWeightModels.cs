namespace ArchonAI.Core.Models.Models.Routing;

// ══════════════════════════════════════════════════════════════
//  Per-model routing weight
// ══════════════════════════════════════════════════════════════

public sealed record ModelRoutingWeight(
    string Provider,
    string Model,
    double Weight,
    double PreviousWeight,
    string AdjustmentReason,
    DateTimeOffset AdjustedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Per-task-type weight overrides
// ══════════════════════════════════════════════════════════════

public sealed record TaskTypeModelWeight(
    string TaskType,
    string Provider,
    string Model,
    double Weight,
    double SuccessRate,
    double AverageLatencyMs,
    int SampleCount,
    DateTimeOffset AdjustedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Weight adjustment event
// ══════════════════════════════════════════════════════════════

public sealed record WeightAdjustmentRecord(
    string Provider,
    string Model,
    string? TaskType,
    double OldWeight,
    double NewWeight,
    string Reason,
    DateTimeOffset AdjustedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Full weight adjustment report
// ══════════════════════════════════════════════════════════════

public sealed record RoutingWeightReport(
    Guid ReportId,
    int ModelsEvaluated,
    IReadOnlyList<ModelRoutingWeight> GlobalWeights,
    IReadOnlyList<TaskTypeModelWeight> TaskTypeWeights,
    IReadOnlyList<WeightAdjustmentRecord> Adjustments,
    string TopModel,
    string BottomModel,
    DateTimeOffset GeneratedAtUtc);
