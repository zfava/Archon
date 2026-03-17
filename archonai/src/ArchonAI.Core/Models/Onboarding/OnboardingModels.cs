namespace ArchonAI.Core.Models.Onboarding;

public sealed record OnboardingDeployRequest(
    IReadOnlyList<string> ConnectedSystems,
    string BusinessType,
    string AutomationLevel,
    IReadOnlyList<DepartmentConfig> Departments);

public sealed record DepartmentConfig(
    string Name,
    string Level);

public sealed record OnboardingDeployResult(
    bool Success,
    IReadOnlyList<string> AgentsConfigured,
    IReadOnlyList<string> StrategiesApplied,
    IReadOnlyList<string> IntegrationsActive,
    int EstimatedReadyMinutes);
