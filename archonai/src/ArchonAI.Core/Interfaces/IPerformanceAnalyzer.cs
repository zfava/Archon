using ArchonAI.Core.Models.Optimization;

namespace ArchonAI.Core.Interfaces;

public interface IPerformanceAnalyzer
{
    void RecordAgentExecution(Guid agentId, string agentName, bool success, double executionTimeMs, decimal cost);

    void RecordTaskCompletion(string taskType, bool success, double executionTimeMs, decimal cost);

    global::System.Threading.Tasks.Task<PerformanceReport> AnalyzeAsync(CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<ImprovementAction>> GenerateImprovementsAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task ApplyImprovementsAsync(
        IReadOnlyList<ImprovementAction> actions,
        CancellationToken cancellationToken = default);
}
