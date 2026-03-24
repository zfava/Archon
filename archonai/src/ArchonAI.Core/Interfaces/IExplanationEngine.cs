using ArchonAI.Core.Models.Explanation;

namespace ArchonAI.Core.Interfaces;

public interface IExplanationEngine
{
    Task<StrategyExplanation> ExplainStrategyChoiceAsync(
        Guid goalId,
        string goalTitle,
        IReadOnlyList<string> candidateStrategies,
        CancellationToken ct = default);

    Task<AgentExplanation> ExplainAgentSelectionAsync(
        string requiredCapability,
        string? taskType,
        CancellationToken ct = default);

    Task<DecisionExplanation> ExplainDecisionAsync(
        Guid goalId,
        string goalTitle,
        IReadOnlyList<string> candidateStrategies,
        string requiredCapability,
        string? taskType,
        CancellationToken ct = default);
}
