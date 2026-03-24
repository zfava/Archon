using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Policy;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Core.Interfaces;

public interface IPolicyEngine
{
    global::System.Threading.Tasks.Task<PolicyDecision> EvaluateAsync(
        Agent agent,
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default);
}
