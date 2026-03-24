using ArchonAI.Core.Models.Planning;

namespace ArchonAI.Core.Interfaces;

public interface IGoalGenerator
{
    global::System.Threading.Tasks.Task<GoalGenerationResult> GenerateGoalsAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<OperationalGoal?> GetGoalAsync(
        Guid goalId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<OperationalGoal>> GetGoalsByStatusAsync(
        GoalStatus status,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task ApproveGoalAsync(
        Guid goalId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task CancelGoalAsync(
        Guid goalId,
        string reason,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<GoalDashboard> GetDashboardAsync(
        CancellationToken cancellationToken = default);
}
