using ArchonAI.Core.Models.Optimization;

namespace ArchonAI.Core.Interfaces;

public interface IContinuousImprovementEngine
{
    /// <summary>
    /// Run a full improvement cycle: analyze metrics, detect inefficiencies,
    /// generate recommendations, and optionally apply them.
    /// </summary>
    global::System.Threading.Tasks.Task<ContinuousImprovementReport> RunCycleAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detect inefficiencies from current performance metrics without
    /// generating or applying recommendations.
    /// </summary>
    global::System.Threading.Tasks.Task<IReadOnlyList<DetectedInefficiency>> DetectInefficienciesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate system improvement recommendations from detected inefficiencies.
    /// </summary>
    global::System.Threading.Tasks.Task<IReadOnlyList<SystemImprovementRecommendation>> RecommendImprovementsAsync(
        IReadOnlyList<DetectedInefficiency> inefficiencies,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get performance trends for all tracked metrics.
    /// </summary>
    global::System.Threading.Tasks.Task<IReadOnlyList<PerformanceTrend>> GetTrendsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the history of all improvement cycle reports.
    /// </summary>
    IReadOnlyList<ContinuousImprovementReport> GetCycleHistory();
}
