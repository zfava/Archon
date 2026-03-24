using ArchonAI.Core.Models.Planning;

namespace ArchonAI.Core.Interfaces;

public interface ITaskGraphBuilder
{
    global::System.Threading.Tasks.Task<TaskGraph> BuildGraphAsync(
        OperationalGoal goal,
        string strategy,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<TaskGraphDispatchResult> DispatchGraphAsync(
        TaskGraph graph,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<TaskGraph?> GetGraphAsync(
        Guid graphId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<TaskGraph>> GetGraphsByGoalAsync(
        Guid goalId,
        CancellationToken cancellationToken = default);
}
