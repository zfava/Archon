using ArchonAI.Core.Models.Marketing;

namespace ArchonAI.Core.Interfaces;

public interface IMarketingEngine
{
    global::System.Threading.Tasks.Task<CampaignAnalysisResult> AnalyzeCampaignAsync(
        string campaignId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<MarketingStrategyRecommendation>> RecommendStrategiesAsync(
        string scope,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<EngagementMetricsSnapshot> GetEngagementMetricsAsync(
        string scope,
        string period,
        CancellationToken cancellationToken = default);

    MarketingEngineStatus GetStatus();
}
