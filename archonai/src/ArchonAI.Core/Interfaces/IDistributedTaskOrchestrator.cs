using ArchonAI.Core.Models;
using CoreTask = ArchonAI.Core.Models.Task;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;

namespace ArchonAI.Core.Interfaces;

public interface IDistributedTaskOrchestrator
{
    global::System.Threading.Tasks.Task EnqueueAsync(CoreTask task, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<ExecutionResult>> ExecuteAllAsync(
        CoreExecutionContext context,
        Func<CoreTask, CancellationToken, global::System.Threading.Tasks.Task<ExecutionResult>> executor,
        CancellationToken cancellationToken = default);
}
