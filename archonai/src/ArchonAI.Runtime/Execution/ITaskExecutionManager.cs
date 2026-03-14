using ArchonAI.Core.Models;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Runtime.Execution;

public interface ITaskExecutionManager
{
    global::System.Threading.Tasks.Task EnqueueAsync(
        CoreTask task,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<ExecutionResult>> ExecuteAllAsync(
        Func<CoreTask, CancellationToken, global::System.Threading.Tasks.Task<ExecutionResult>> executor,
        CancellationToken cancellationToken = default);
}
