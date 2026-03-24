namespace ArchonAI.Core.Models.Explanation;

public sealed record StrategyExplanation(
    Guid ExplanationId,
    Guid GoalId,
    string GoalTitle,
    string ChosenStrategy,
    double EconomicScore,
    string SelectionRationale,
    IReadOnlyList<StrategyFactor> Factors,
    IReadOnlyList<StrategyComparison> Alternatives,
    DateTimeOffset GeneratedAtUtc);

public sealed record StrategyFactor(
    string Name,
    double Score,
    double Weight,
    string Impact,
    string Description);

public sealed record StrategyComparison(
    string Strategy,
    double EconomicScore,
    double SuccessProbability,
    decimal EstimatedCost,
    double EstimatedDurationHours,
    string WhyNotChosen);

public sealed record AgentExplanation(
    Guid ExplanationId,
    string RequiredCapability,
    string? TaskType,
    Guid SelectedAgentId,
    string SelectedAgentName,
    double SelectionScore,
    string SelectionReason,
    IReadOnlyList<AgentFactor> Factors,
    IReadOnlyList<AgentAlternative> Alternatives,
    DateTimeOffset GeneratedAtUtc);

public sealed record AgentFactor(
    string Name,
    string Value,
    string Impact,
    string Description);

public sealed record AgentAlternative(
    Guid AgentId,
    string AgentName,
    double Score,
    double SuccessRate,
    double AverageLatencyMs,
    decimal AverageCost,
    string WhyNotChosen);

public sealed record DecisionExplanation(
    Guid ExplanationId,
    string DecisionType,
    string Summary,
    StrategyExplanation? StrategyExplanation,
    AgentExplanation? AgentExplanation,
    DateTimeOffset GeneratedAtUtc);
