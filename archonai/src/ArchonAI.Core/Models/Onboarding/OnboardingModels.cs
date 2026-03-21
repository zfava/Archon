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
    IReadOnlyList<string> WorkflowsCreated,
    int EstimatedReadyMinutes);

public sealed record OnboardingTemplateDeployRequest(
    string TemplateId,
    IReadOnlyList<string> ConnectedSystems,
    string BusinessType,
    string AutomationLevel,
    IReadOnlyList<DepartmentConfig> Departments,
    IReadOnlyList<string> Agents,
    IReadOnlyList<TemplateWorkflowConfig> Workflows,
    IReadOnlyList<string> Strategies,
    IReadOnlyList<TrustTierDefaultConfig>? TrustTierDefaults = null);

public sealed record TemplateWorkflowConfig(
    string Name,
    IReadOnlyList<string> Steps);

public sealed record TrustTierDefaultConfig(
    string ActionScope,
    string MaxTier,
    string Rationale);
