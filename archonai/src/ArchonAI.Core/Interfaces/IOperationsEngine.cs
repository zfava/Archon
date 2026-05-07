using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Operations;

namespace ArchonAI.Core.Interfaces;

public interface IOperationsEngine
{
    global::System.Threading.Tasks.Task<WorkflowAnalysisResult> AnalyzeWorkflowAsync(
        Guid objectiveId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<OperationsInsight>> IdentifyInefficienciesAsync(
        string scope,
        int maxResults = 20,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<OperationsRecommendation>> RecommendImprovementsAsync(
        Guid objectiveId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<WorkflowCoordinationResult> CoordinateWorkflowAsync(
        string workflowTemplate,
        IReadOnlyList<string> agentCapabilities,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default);

    OperationsStatus GetStatus();
}
