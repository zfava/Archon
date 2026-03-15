namespace ArchonAI.Core.Models.Planning;

public sealed record WorkflowDefinition(
    Guid ObjectiveId,
    string Strategy,
    string Summary,
    IReadOnlyList<WorkflowStepDefinition> Steps,
    DateTimeOffset CreatedAtUtc);
