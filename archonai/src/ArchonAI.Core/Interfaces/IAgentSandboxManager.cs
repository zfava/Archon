using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Sandbox;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Core.Interfaces;

public interface IAgentSandboxManager
{
    global::System.Threading.Tasks.Task<SandboxDecision> EnsureSandboxAsync(
        Agent agent,
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task RecordExecutionCompletedAsync(
        Guid sandboxId,
        CancellationToken cancellationToken = default);
}
