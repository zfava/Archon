using ArchonAI.Core.Models.Planning;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// The autonomous intelligence loop that forms the core operating system
/// for ArchonAI organizations. Continuously cycles through:
/// Perception → Planner → Reasoner → Simulation → TaskGraph → Agents → Evaluation → Learning.
/// </summary>
public interface IIntelligenceLoop
{
    /// <summary>
    /// Execute a single iteration of the intelligence loop:
    /// 1. Perception observes business signals
    /// 2. Planner generates goals
    /// 3. Reasoner evaluates strategies
    /// 4. Simulation tests strategies
    /// 5. Planner generates task graphs
    /// 6. Agents execute tasks
    /// 7. Reasoner evaluates outcomes
    /// 8. Learning improves future strategies
    /// </summary>
    global::System.Threading.Tasks.Task<IntelligenceLoopCycleResult> ExecuteCycleAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current status of the intelligence loop.
    /// </summary>
    IntelligenceLoopStatus GetStatus();
}

public sealed record IntelligenceLoopCycleResult(
    Guid CycleId,
    int SignalsObserved,
    int GoalsGenerated,
    int StrategiesEvaluated,
    int SimulationsRun,
    int TaskGraphsBuilt,
    int TasksExecuted,
    int OutcomesEvaluated,
    bool LearningApplied,
    IReadOnlyList<OperationalGoal> ProcessedGoals,
    IReadOnlyList<string> Insights,
    TimeSpan CycleDuration,
    DateTimeOffset CompletedAtUtc);

public sealed record IntelligenceLoopStatus(
    bool IsRunning,
    long TotalCyclesCompleted,
    long TotalGoalsProcessed,
    long TotalTasksExecuted,
    long TotalLearningCycles,
    DateTimeOffset? LastCycleCompletedAtUtc,
    DateTimeOffset? NextCycleScheduledAtUtc,
    TimeSpan? AverageCycleDuration,
    DateTimeOffset StatusAsOfUtc);
