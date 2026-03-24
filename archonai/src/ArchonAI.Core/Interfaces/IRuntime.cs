using ArchonAI.Core.Models;
using CoreTask = ArchonAI.Core.Models.Task;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;

namespace ArchonAI.Core.Interfaces;

public interface IRuntime
{
    global::System.Threading.Tasks.Task RegisterAgentAsync(
        Agent agent,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task ScheduleTaskAsync(
        CoreTask task,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<ExecutionResult>> ExecuteScheduledTasksAsync(
        CoreExecutionContext context,
        CancellationToken cancellationToken = default);
}
