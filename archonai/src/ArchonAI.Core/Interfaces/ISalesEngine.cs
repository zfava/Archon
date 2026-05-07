using ArchonAI.Core.Models.Sales;

namespace ArchonAI.Core.Interfaces;

public interface ISalesEngine
{
    global::System.Threading.Tasks.Task<PipelineAnalysisResult> AnalyzePipelineAsync(
        string pipelineId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<PrioritizedOpportunity>> PrioritizeOpportunitiesAsync(
        string pipelineId,
        int maxResults = 20,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<OutreachRecommendation>> RecommendOutreachAsync(
        string opportunityId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<SalesMetricsSnapshot> GetMetricsAsync(
        string scope,
        string period,
        CancellationToken cancellationToken = default);

    SalesEngineStatus GetStatus();
}
