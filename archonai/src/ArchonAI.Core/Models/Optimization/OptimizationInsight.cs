namespace ArchonAI.Core.Models.Optimization;

public sealed record OptimizationInsight(
    Guid WorkflowId,
    bool WasInefficient,
    string Reason,
    double EfficiencyScore,
    int OriginalStepCount,
    int OptimizedStepCount,
    DateTimeOffset AnalyzedAtUtc);
