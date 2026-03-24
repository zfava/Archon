using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Supervision;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Core.Interfaces;

public interface IAgentSupervisor
{
    global::System.Threading.Tasks.Task RegisterAgentAsync(Agent agent, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<Agent?> SelectAgentAsync(
        IReadOnlyList<Agent> candidates,
        CoreTask task,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<SupervisionDecision> ValidateExecutionAsync(
        Agent agent,
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task RecordExecutionCompletedAsync(
        Agent agent,
        ExecutionResult result,
        double executionTimeMs,
        CancellationToken cancellationToken = default);
}
