using ArchonAI.Core.Models.Support;

namespace ArchonAI.Core.Interfaces;

public interface ISupportEngine
{
    global::System.Threading.Tasks.Task<TicketAnalysisResult> AnalyzeTicketsAsync(
        string scope,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<RecurringIssue>> DetectRecurringIssuesAsync(
        string scope,
        int minOccurrences = 3,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<AutoResponseRecommendation>> RecommendAutoResponsesAsync(
        string issueCategory,
        CancellationToken cancellationToken = default);

    SupportEngineStatus GetStatus();
}
