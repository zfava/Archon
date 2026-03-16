namespace ArchonAI.Core.Models.Marketing;

public sealed record CampaignAnalysisResult(
    bool IsSuccess,
    string CampaignId,
    double TotalSpend,
    double TotalRevenue,
    double ROI,
    double ConversionRate,
    int TotalImpressions,
    int TotalClicks,
    IReadOnlyList<string> KeyFindings,
    IReadOnlyList<string> Improvements,
    IReadOnlyDictionary<string, string> Metrics,
    DateTimeOffset AnalyzedAtUtc);

public sealed record MarketingStrategyRecommendation(
    Guid Id,
    string StrategyType,
    string Title,
    string Description,
    double ExpectedROI,
    double ConfidenceScore,
    IReadOnlyList<string> TargetChannels,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset GeneratedAtUtc);

public sealed record EngagementMetricsSnapshot(
    string Scope,
    string Period,
    int TotalImpressions,
    int TotalClicks,
    int TotalConversions,
    double ClickThroughRate,
    double ConversionRate,
    double CostPerAcquisition,
    IReadOnlyDictionary<string, string> ChannelBreakdown,
    IReadOnlyDictionary<string, string> AdditionalMetrics,
    DateTimeOffset GeneratedAtUtc);

public sealed record MarketingEngineStatus(
    bool IsActive,
    long CampaignsAnalyzed,
    long StrategiesRecommended,
    long MetricsGenerated,
    long DataFabricQueries,
    DateTimeOffset StatusAsOfUtc);
