namespace ArchonAI.Core.Models.Simulation;

public sealed record ScenarioDefinition(
    Guid ScenarioId,
    string Name,
    string Description,
    IReadOnlyList<ScenarioStep> Steps,
    IReadOnlyDictionary<string, string> EnvironmentParameters,
    DateTimeOffset CreatedAtUtc);

public sealed record ScenarioStep(
    int Order,
    string Name,
    string Action,
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyList<string> DependsOn);
