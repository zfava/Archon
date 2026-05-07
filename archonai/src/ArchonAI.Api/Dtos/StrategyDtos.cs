using ArchonAI.Core.Models.Evaluation;
using ArchonAI.Core.Models.Simulation;

namespace ArchonAI.Api.Dtos;

public sealed record FinanceAnalysisRequest(string Scope, Dictionary<string, string> Parameters);
public sealed record FinanceSummaryRequest(string Scope, string Period);
public sealed record BudgetAssistRequest(string DepartmentId, Dictionary<string, string> Parameters);
public sealed record SupportAnalysisRequest(string Scope, Dictionary<string, string> Parameters);
public sealed record ResourceUsageInput(double EstimatedCpuSeconds, double EstimatedMemoryMb, int EstimatedAgentCount, double EstimatedCostPerExecution, string CostCurrency);
public sealed record CreateStrategyTemplateRequest(string Name, string Description, string ObjectiveType, string WorkflowTemplate, Dictionary<string, string> SuccessMetrics, ResourceUsageInput ResourceUsage, List<string>? Tags = null, Dictionary<string, string>? Metadata = null);
public sealed record UpdateStrategyTemplateRequest(string? Description = null, string? WorkflowTemplate = null, Dictionary<string, string>? SuccessMetrics = null, ResourceUsageInput? ResourceUsage = null, List<string>? Tags = null);
public sealed record RecordStrategyExecutionRequest(bool IsSuccess, double LatencyMs, double Cost, Dictionary<string, string>? Outcomes = null);
public sealed record CompareStrategiesRequest(IReadOnlyList<Guid> StrategyIds);
public sealed record EconomicEvaluationRequest(
    string ObjectiveTitle,
    string ObjectiveDescription,
    IReadOnlyList<string>? CandidateStrategies,
    Dictionary<string, string>? Constraints,
    DateTimeOffset? Deadline,
    EconomicWeightsDto? Weights);
public sealed record SingleStrategyEvaluationRequest(
    string ObjectiveTitle,
    string ObjectiveDescription,
    string Strategy,
    Dictionary<string, string>? Constraints,
    DateTimeOffset? Deadline);
public sealed record EconomicWeightsDto(
    double CostWeight,
    double ImpactWeight,
    double SuccessProbabilityWeight,
    double ExecutionTimeWeight);
public sealed record BuildTaskGraphRequest(
    Guid GoalId,
    string? Strategy);
public sealed record SimulateStrategyRequest(
    Guid GraphId,
    string Strategy);
public sealed record CompareTaskGraphStrategiesRequest(
    Guid GraphId,
    IReadOnlyList<string> Strategies);
public sealed record SimulateGoalStrategiesRequest(
    Guid GoalId,
    IReadOnlyList<string>? Strategies);
public sealed record SimulationGuidedPlanRequest(
    Guid GoalId,
    IReadOnlyList<string>? Strategies);
public sealed record EvaluateOutcomeRequest(
    StrategySimulationResult SimulationResult,
    TaskGraphExecutionResult ActualResult);
public sealed record ExplainStrategyRequest(
    Guid GoalId,
    string GoalTitle,
    IReadOnlyList<string> CandidateStrategies);
public sealed record ExplainAgentRequest(
    string RequiredCapability,
    string? TaskType);
public sealed record ExplainDecisionRequest(
    Guid GoalId,
    string GoalTitle,
    IReadOnlyList<string> CandidateStrategies,
    string RequiredCapability,
    string? TaskType);
public sealed record CancelGoalRequest(string Reason);
