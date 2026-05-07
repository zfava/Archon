namespace ArchonAI.Core.Models.Learning;

// ══════════════════════════════════════════════════════════════
//  Strategy performance profile built from outcome history
// ══════════════════════════════════════════════════════════════

public sealed record StrategyPerformanceProfile(
    string Strategy,
    int TotalExecutions,
    int Successes,
    int Failures,
    double SuccessRate,
    double AverageOverallScore,
    double AverageDurationAccuracy,
    double AverageCostAccuracy,
    double AverageRiskPredictionAccuracy,
    IReadOnlyDictionary<string, double> SuccessRateByDepartment,
    IReadOnlyDictionary<string, double> SuccessRateByPriority,
    DateTimeOffset LastUpdatedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Agent performance profile built from outcome history
// ══════════════════════════════════════════════════════════════

public sealed record AgentTypePerformanceProfile(
    string AgentType,
    int TotalAssignments,
    int Successes,
    int Failures,
    double SuccessRate,
    double AverageDurationDeviationPercent,
    double AverageCostDeviationPercent,
    int UnexpectedFailures,
    int CriticalPathFailures,
    DateTimeOffset LastUpdatedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Improvement recommendations
// ══════════════════════════════════════════════════════════════

public sealed record StrategyLearningRecommendation(
    string RecommendationType,
    string Target,
    string Recommendation,
    double Confidence,
    string Evidence);

public sealed record GoalGenerationAdjustment(
    string AdjustmentType,
    string Department,
    string CurrentBehaviour,
    string RecommendedBehaviour,
    string Reason,
    double Confidence);

public sealed record AgentAssignmentAdjustment(
    string AgentType,
    string RecommendedAction,
    string Reason,
    double Confidence,
    IReadOnlyDictionary<string, string> Evidence);

// ══════════════════════════════════════════════════════════════
//  Full learning report
// ══════════════════════════════════════════════════════════════

public sealed record StrategyLearningReport(
    Guid ReportId,
    int EvaluationsAnalyzed,
    IReadOnlyList<StrategyPerformanceProfile> StrategyProfiles,
    IReadOnlyList<AgentTypePerformanceProfile> AgentTypeProfiles,
    IReadOnlyList<StrategyLearningRecommendation> StrategyRecommendations,
    IReadOnlyList<GoalGenerationAdjustment> GoalAdjustments,
    IReadOnlyList<AgentAssignmentAdjustment> AgentAdjustments,
    string BestOverallStrategy,
    string WorstOverallStrategy,
    DateTimeOffset GeneratedAtUtc);
