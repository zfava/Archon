using ArchonAI.Core.Models.Planning;

namespace ArchonAI.Core.Models.Simulation;

public sealed record SimulationValidatedPlan(
    WorkflowDefinition Workflow,
    string ValidatedStrategy,
    ScenarioResult SimulationOutcome,
    bool SimulationApproved,
    string AdjustmentReason,
    DateTimeOffset ValidatedAtUtc);
