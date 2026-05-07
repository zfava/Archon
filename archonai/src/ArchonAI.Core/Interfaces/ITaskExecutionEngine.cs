using ArchonAI.Core.Models;
using ArchonAI.Core.Models.TaskRuntime;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Core.Interfaces;

public interface ITaskExecutionEngine
{
    global::System.Threading.Tasks.Task<TaskExecutionOutcome> ExecuteTaskAsync(
        CoreTask task,
        CoreExecutionContext context,
        IReadOnlyList<IAgent> availableAgents,
        CancellationToken cancellationToken = default);
}
