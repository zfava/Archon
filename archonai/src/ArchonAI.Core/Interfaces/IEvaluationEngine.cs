using ArchonAI.Core.Models;
using CoreTask = ArchonAI.Core.Models.Task;
using ArchonAI.Core.Models.Evaluation;

namespace ArchonAI.Core.Interfaces;

public interface IEvaluationEngine
{
    global::System.Threading.Tasks.Task<EvaluationReport> EvaluateAsync(
        Agent agent,
        CoreTask task,
        ExecutionResult result,
        double latencyMs,
        decimal estimatedCost,
        CancellationToken cancellationToken = default);
}
