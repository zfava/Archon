using ArchonAI.Core.Models.ControlPlane;

namespace ArchonAI.Core.Interfaces;

public interface IControlPlaneRepository
{
    // ── Tenants ───────────────────────────────────────────────

    global::System.Threading.Tasks.Task<Tenant> UpsertTenantAsync(Tenant tenant, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<Tenant?> GetTenantByNameAsync(string name, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<IReadOnlyList<Tenant>> ListTenantsAsync(
        TenantStatus? status, int offset, int limit, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<bool> RemoveTenantAsync(Guid tenantId, CancellationToken ct = default);

    // ── Managed workflows ─────────────────────────────────────

    global::System.Threading.Tasks.Task<ManagedWorkflow> UpsertWorkflowAsync(ManagedWorkflow workflow, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<ManagedWorkflow?> GetWorkflowAsync(Guid workflowId, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<IReadOnlyList<ManagedWorkflow>> ListWorkflowsAsync(
        string? tenantId, ManagedWorkflowStatus? status, int offset, int limit, CancellationToken ct = default);

    // ── Managed agents ────────────────────────────────────────

    global::System.Threading.Tasks.Task<ManagedAgent> UpsertAgentAsync(ManagedAgent agent, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<ManagedAgent?> GetAgentAsync(Guid agentId, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<IReadOnlyList<ManagedAgent>> ListAgentsAsync(
        string? tenantId, ManagedAgentStatus? status, int offset, int limit, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<bool> RemoveAgentAsync(Guid agentId, CancellationToken ct = default);

    // ── Policies ──────────────────────────────────────────────

    global::System.Threading.Tasks.Task<PlatformPolicy> UpsertPolicyAsync(PlatformPolicy policy, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<PlatformPolicy?> GetPolicyAsync(Guid policyId, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<IReadOnlyList<PlatformPolicy>> ListPoliciesAsync(
        string? tenantId, PlatformPolicyType? policyType, bool? isEnabled, int offset, int limit, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<bool> RemovePolicyAsync(Guid policyId, CancellationToken ct = default);

    // ── Configurations ────────────────────────────────────────

    global::System.Threading.Tasks.Task<PlatformConfiguration> UpsertConfigurationAsync(PlatformConfiguration config, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<PlatformConfiguration?> GetConfigurationAsync(
        string tenantId, string scope, string key, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<IReadOnlyList<PlatformConfiguration>> ListConfigurationsAsync(
        string? tenantId, string? scope, int offset, int limit, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<bool> RemoveConfigurationAsync(
        string tenantId, string scope, string key, CancellationToken ct = default);

    // ── Counts ────────────────────────────────────────────────

    global::System.Threading.Tasks.Task<int> CountTenantsAsync(TenantStatus? status = null, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<int> CountWorkflowsAsync(string? tenantId = null, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<int> CountAgentsAsync(string? tenantId = null, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<int> CountPoliciesAsync(string? tenantId = null, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<int> CountConfigurationsAsync(string? tenantId = null, CancellationToken ct = default);
}
