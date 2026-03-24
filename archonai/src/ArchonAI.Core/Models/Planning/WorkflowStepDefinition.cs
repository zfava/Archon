namespace ArchonAI.Core.Models.Planning;

public sealed record WorkflowStepDefinition(
    int Order,
    string Name,
    string Description,
    string AgentType,
    IReadOnlyDictionary<string, string> Inputs);
