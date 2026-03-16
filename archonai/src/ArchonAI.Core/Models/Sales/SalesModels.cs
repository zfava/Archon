namespace ArchonAI.Core.Models.Sales;

public sealed record PipelineAnalysisResult(
    bool IsSuccess,
    string PipelineId,
    int TotalOpportunities,
    double TotalValue,
    double WeightedValue,
    double AverageCloseRate,
    IReadOnlyList<string> StageDistribution,
    IReadOnlyList<string> Risks,
    IReadOnlyDictionary<string, string> Metrics,
    DateTimeOffset AnalyzedAtUtc);

public sealed record PrioritizedOpportunity(
    string OpportunityId,
    string Name,
    double Value,
    double PriorityScore,
    string Stage,
    string Reason,
    DateTimeOffset? ExpectedCloseDate,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record OutreachRecommendation(
    Guid Id,
    string OpportunityId,
    string ActionType,
    string Title,
    string Description,
    double ExpectedImpact,
    string SuggestedTiming,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset GeneratedAtUtc);

public sealed record SalesMetricsSnapshot(
    string Scope,
    string Period,
    double TotalRevenue,
    double PipelineValue,
    int DealsWon,
    int DealsLost,
    double WinRate,
    double AverageDealSize,
    double AverageSalesCycle,
    IReadOnlyDictionary<string, string> AdditionalMetrics,
    DateTimeOffset GeneratedAtUtc);

public sealed record SalesEngineStatus(
    bool IsActive,
    long PipelineAnalyses,
    long OpportunitiesPrioritized,
    long OutreachRecommendations,
    long MetricsGenerated,
    long DataFabricQueries,
    DateTimeOffset StatusAsOfUtc);
