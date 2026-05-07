using ArchonAI.Core.Models.Planning;

namespace ArchonAI.Core.Models.Simulation;

public sealed record SimulationGuidedPlan(
    OperationalGoal Goal,
    TaskGraph SelectedTaskGraph,
    StrategySimulationResult SelectedSimulation,
    TaskGraphStrategyComparison ComparisonResult,
    string SelectedStrategy,
    double ExpectedSuccessProbability,
    double RiskScore,
    string PlanDecisionReason,
    DateTimeOffset PlannedAtUtc);
