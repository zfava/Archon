using ArchonAI.Core.Models.ControlPlane;

namespace ArchonAI.Core.Interfaces;

public interface IControlPlaneService
{
    // ── Tenant lifecycle ──────────────────────────────────────

    global::System.Threading.Tasks.Task<Tenant> ProvisionTenantAsync(
        string name, string displayName, TenantTier tier,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<Tenant?> GetTenantAsync(
        Guid tenantId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<Tenant>> ListTenantsAsync(
        TenantStatus? status = null, int offset = 0, int limit = 50,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<Tenant> ActivateTenantAsync(
        Guid tenantId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<Tenant> SuspendTenantAsync(
        Guid tenantId, string reason, CancellationToken ct = default);

    global::System.Threading.Tasks.Task DeprovisionTenantAsync(
        Guid tenantId, CancellationToken ct = default);

    // ── Workflow lifecycle ─────────────────────────────────────

    global::System.Threading.Tasks.Task<ManagedWorkflow> RegisterWorkflowAsync(
        string tenantId, string name, string description, string strategy, int stepCount,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<ManagedWorkflow?> GetManagedWorkflowAsync(
        Guid workflowId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<ManagedWorkflow>> ListManagedWorkflowsAsync(
        string? tenantId = null, ManagedWorkflowStatus? status = null,
        int offset = 0, int limit = 50, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<ManagedWorkflow> UpdateWorkflowStatusAsync(
        Guid workflowId, ManagedWorkflowStatus status, CancellationToken ct = default);

    // ── Agent lifecycle ───────────────────────────────────────

    global::System.Threading.Tasks.Task<ManagedAgent> RegisterAgentAsync(
        string tenantId, string name, string version, IReadOnlyList<string> capabilities,
        IReadOnlyDictionary<string, string>? configuration = null,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<ManagedAgent?> GetManagedAgentAsync(
        Guid agentId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<ManagedAgent>> ListManagedAgentsAsync(
        string? tenantId = null, ManagedAgentStatus? status = null,
        int offset = 0, int limit = 50, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<ManagedAgent> UpdateAgentStatusAsync(
        Guid agentId, ManagedAgentStatus status, CancellationToken ct = default);

    global::System.Threading.Tasks.Task DeregisterAgentAsync(
        Guid agentId, CancellationToken ct = default);

    // ── Policy management ─────────────────────────────────────

    global::System.Threading.Tasks.Task<PlatformPolicy> CreatePolicyAsync(
        string tenantId, string name, string description, PlatformPolicyType policyType,
        string targetResource, IReadOnlyDictionary<string, string> rules, int priority,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<PlatformPolicy?> GetPolicyAsync(
        Guid policyId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<PlatformPolicy>> ListPoliciesAsync(
        string? tenantId = null, PlatformPolicyType? policyType = null, bool? isEnabled = null,
        int offset = 0, int limit = 50, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<PlatformPolicy> UpdatePolicyAsync(
        Guid policyId, bool isEnabled, IReadOnlyDictionary<string, string>? rules = null,
        int? priority = null, CancellationToken ct = default);

    global::System.Threading.Tasks.Task DeletePolicyAsync(
        Guid policyId, CancellationToken ct = default);

    // ── Configuration management ──────────────────────────────

    global::System.Threading.Tasks.Task<PlatformConfiguration> SetConfigurationAsync(
        string tenantId, string scope, string key, string value,
        string? description = null, bool isSecret = false,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<PlatformConfiguration?> GetConfigurationAsync(
        string tenantId, string scope, string key,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<PlatformConfiguration>> ListConfigurationsAsync(
        string? tenantId = null, string? scope = null,
        int offset = 0, int limit = 100,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task DeleteConfigurationAsync(
        string tenantId, string scope, string key,
        CancellationToken ct = default);

    // ── Dashboard & status ────────────────────────────────────

    global::System.Threading.Tasks.Task<ControlPlaneDashboard> GetDashboardAsync(
        CancellationToken ct = default);

    ControlPlaneStatus GetStatus();

    /// <summary>
    /// Async version of GetStatus. Preferred in all async call sites.
    /// </summary>
    global::System.Threading.Tasks.Task<ControlPlaneStatus> GetStatusAsync(CancellationToken ct = default);
}
