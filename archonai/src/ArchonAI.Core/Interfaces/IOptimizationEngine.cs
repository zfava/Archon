using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Optimization;
using ArchonAI.Core.Models.Planning;

namespace ArchonAI.Core.Interfaces;

public interface IOptimizationEngine
{
    global::System.Threading.Tasks.Task<WorkflowDefinition> OptimizeWorkflowAsync(
        Objective objective,
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<OperationalStrategy>> GenerateImprovedStrategiesAsync(
        Objective objective,
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<bool> DeployOptimizedWorkflowAsync(
        WorkflowDefinition optimizedWorkflow,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<PerformanceReport> AnalyzePerformanceAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<ImprovementAction>> RunContinuousImprovementCycleAsync(
        CancellationToken cancellationToken = default);
}
