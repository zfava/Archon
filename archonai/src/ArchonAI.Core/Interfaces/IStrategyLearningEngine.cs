using ArchonAI.Core.Models.Learning;

namespace ArchonAI.Core.Interfaces;

public interface IStrategyLearningEngine
{
    /// <summary>
    /// Analyze all outcome history and produce a learning report with
    /// improvement recommendations for strategy selection, goal generation,
    /// and agent assignment.
    /// </summary>
    global::System.Threading.Tasks.Task<StrategyLearningReport> AnalyzeAndLearnAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get performance profiles for all strategies observed so far.
    /// </summary>
    global::System.Threading.Tasks.Task<IReadOnlyList<StrategyPerformanceProfile>> GetStrategyProfilesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get performance profiles for all agent types observed so far.
    /// </summary>
    global::System.Threading.Tasks.Task<IReadOnlyList<AgentTypePerformanceProfile>> GetAgentTypeProfilesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recommend the best strategy for a given department and priority,
    /// using learned performance data.
    /// </summary>
    global::System.Threading.Tasks.Task<string> RecommendStrategyAsync(
        string department,
        string priority,
        CancellationToken cancellationToken = default);
}
