using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Governance;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Core.Interfaces;

public interface IGovernanceKernel
{
    global::System.Threading.Tasks.Task<GovernanceDecision> ValidateAgentRegistrationAsync(
        Agent agent,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<GovernanceDecision> ValidateExecutionAsync(
        Agent agent,
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task MarkExecutionCompletedAsync(
        Guid agentId,
        CancellationToken cancellationToken = default);
}
