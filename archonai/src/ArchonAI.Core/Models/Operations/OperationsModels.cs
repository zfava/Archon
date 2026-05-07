namespace ArchonAI.Core.Models.Operations;

public sealed record WorkflowAnalysisResult(
    Guid ObjectiveId,
    bool IsSuccess,
    int TotalSteps,
    int SuccessfulSteps,
    int FailedSteps,
    double EfficiencyScore,
    IReadOnlyList<string> Bottlenecks,
    IReadOnlyList<string> Recommendations,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset AnalyzedAtUtc);

public sealed record OperationsInsight(
    Guid Id,
    string InsightType,
    string Title,
    string Description,
    double Severity,
    string AffectedComponent,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset DiscoveredAtUtc);

public sealed record OperationsRecommendation(
    Guid Id,
    Guid ObjectiveId,
    string RecommendationType,
    string Title,
    string Description,
    double ExpectedImpact,
    string SuggestedStrategy,
    IReadOnlyList<string> SuggestedAgents,
    DateTimeOffset GeneratedAtUtc);

public sealed record WorkflowCoordinationResult(
    Guid CoordinationId,
    bool IsSuccess,
    string WorkflowTemplate,
    int AgentsInvolved,
    int StepsCompleted,
    int StepsFailed,
    IReadOnlyDictionary<string, string> Outputs,
    IReadOnlyList<string> Errors,
    TimeSpan Duration,
    DateTimeOffset CompletedAtUtc);

public sealed record OperationsStatus(
    bool IsActive,
    long WorkflowsAnalyzed,
    long InefficienciesDetected,
    long RecommendationsGenerated,
    long WorkflowsCoordinated,
    long ReasoningCycles,
    long DataFabricQueries,
    long StrategyLookups,
    DateTimeOffset StatusAsOfUtc);
