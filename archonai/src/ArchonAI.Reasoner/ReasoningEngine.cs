using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;

namespace ArchonAI.Reasoner;

/// <summary>
/// Evaluates runtime outcomes, detects failures, and writes planner feedback signals.
/// </summary>
public sealed class ReasoningEngine : IReasoner
{
    private readonly IPlanningFeedbackStore _feedbackStore;

    public ReasoningEngine(IPlanningFeedbackStore feedbackStore)
    {
        _feedbackStore = feedbackStore;
    }

    public async global::System.Threading.Tasks.Task<string> EvaluateAsync(
        Objective objective,
        IReadOnlyList<ExecutionResult> executionResults,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int total = executionResults.Count;
        int failures = executionResults.Count(result => !result.IsSuccess);
        int successes = total - failures;
        bool degraded = failures > 0;

        string strategy = degraded ? "safe-mode" : "standard";
        string capability = degraded ? "workflow-orchestration" : "operation-execution";
        string rationale = degraded
            ? "Failure(s) detected; favor safer decomposition and stronger validation"
            : "Execution succeeded; keep default strategy";

        await _feedbackStore.AddAsync(new PlanningFeedback(
            Strategy: strategy,
            Capability: capability,
            WasSuccessful: !degraded,
            Rationale: rationale,
            RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        return $"Objective '{objective.Title}' evaluated. Successes={successes}, Failures={failures}, StrategyHint={strategy}.";
    }
}
