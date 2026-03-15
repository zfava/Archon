namespace ArchonAI.Core.Models.Simulation;

public sealed record StrategyComparisonResult(
    string RecommendedStrategy,
    IReadOnlyList<SimulationResult> Results,
    string Reason,
    DateTimeOffset ComparedAtUtc);
