namespace ArchonAI.Core.Models.ControlPlane;

// ── Tenant lifecycle ───────────────────────────────────────────────

public sealed record Tenant(
    Guid Id,
    string Name,
    string DisplayName,
    TenantStatus Status,
    TenantTier Tier,
    TenantResourceQuota ResourceQuota,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? SuspendedAtUtc);

public enum TenantStatus
{
    Provisioning,
    Active,
    Suspended,
    Deprovisioning,
    Deprovisioned
}

public enum TenantTier
{
    Free,
    Standard,
    Professional,
    Enterprise
}

public sealed record TenantResourceQuota(
    int MaxAgents,
    int MaxWorkflows,
    int MaxConcurrentExecutions,
    long MaxStorageBytes,
    int MaxEventsPerMinute);

// ── Managed workflow lifecycle ─────────────────────────────────────

public sealed record ManagedWorkflow(
    Guid Id,
    string TenantId,
    string Name,
    string Description,
    ManagedWorkflowStatus Status,
    string Strategy,
    int StepCount,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastExecutedAtUtc,
    long ExecutionCount,
    long FailureCount);

public enum ManagedWorkflowStatus
{
    Draft,
    Active,
    Paused,
    Archived,
    Failed
}

// ── Managed agent lifecycle ────────────────────────────────────────

public sealed record ManagedAgent(
    Guid Id,
    string TenantId,
    string Name,
    string Version,
    ManagedAgentStatus Status,
    IReadOnlyList<string> Capabilities,
    IReadOnlyDictionary<string, string> Configuration,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? LastActiveAtUtc,
    long ExecutionCount,
    long FailureCount);

public enum ManagedAgentStatus
{
    Registering,
    Active,
    Disabled,
    Draining,
    Deregistered
}

// ── Policy management ──────────────────────────────────────────────

public sealed record PlatformPolicy(
    Guid Id,
    string TenantId,
    string Name,
    string Description,
    PlatformPolicyType PolicyType,
    string TargetResource,
    IReadOnlyDictionary<string, string> Rules,
    bool IsEnabled,
    int Priority,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public enum PlatformPolicyType
{
    Security,
    RateLimit,
    ResourceQuota,
    DataAccess,
    Compliance,
    Workflow,
    Agent
}

// ── Configuration management ───────────────────────────────────────

public sealed record PlatformConfiguration(
    Guid Id,
    string TenantId,
    string Scope,
    string Key,
    string Value,
    string? Description,
    bool IsSecret,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

// ── Control plane status ───────────────────────────────────────────

public sealed record ControlPlaneStatus(
    bool IsActive,
    int TotalTenants,
    int ActiveTenants,
    int TotalManagedWorkflows,
    int TotalManagedAgents,
    int TotalPolicies,
    int TotalConfigurations,
    long TenantOperations,
    long WorkflowOperations,
    long AgentOperations,
    long PolicyOperations,
    long ConfigOperations,
    DateTimeOffset StatusAsOfUtc);

public sealed record ControlPlaneDashboard(
    ControlPlaneStatus Status,
    IReadOnlyList<TenantSummary> Tenants,
    IReadOnlyList<PolicySummary> ActivePolicies,
    DateTimeOffset GeneratedAtUtc);

public sealed record TenantSummary(
    Guid TenantId,
    string Name,
    TenantStatus Status,
    TenantTier Tier,
    int ActiveAgents,
    int ActiveWorkflows,
    DateTimeOffset CreatedAtUtc);

public sealed record PolicySummary(
    Guid PolicyId,
    string Name,
    PlatformPolicyType PolicyType,
    string TargetResource,
    bool IsEnabled,
    int Priority);
