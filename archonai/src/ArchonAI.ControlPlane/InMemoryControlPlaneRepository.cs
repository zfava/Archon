using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ControlPlane;
using Microsoft.Extensions.Logging;

namespace ArchonAI.ControlPlane;

public sealed class InMemoryControlPlaneRepository : IControlPlaneRepository
{
    private readonly ConcurrentDictionary<Guid, Tenant> _tenants = new();
    private readonly ConcurrentDictionary<Guid, ManagedWorkflow> _workflows = new();
    private readonly ConcurrentDictionary<Guid, ManagedAgent> _agents = new();
    private readonly ConcurrentDictionary<Guid, PlatformPolicy> _policies = new();
    private readonly ConcurrentDictionary<string, PlatformConfiguration> _configurations = new();
    private readonly ILogger<InMemoryControlPlaneRepository> _logger;

    public InMemoryControlPlaneRepository(ILogger<InMemoryControlPlaneRepository> logger)
    {
        _logger = logger;
    }

    // ── Tenants ───────────────────────────────────────────────

    public global::System.Threading.Tasks.Task<Tenant> UpsertTenantAsync(
        Tenant tenant, CancellationToken ct = default)
    {
        _tenants[tenant.Id] = tenant;
        _logger.LogDebug("Upserted tenant {TenantId} '{Name}'", tenant.Id, tenant.Name);
        return global::System.Threading.Tasks.Task.FromResult(tenant);
    }

    public global::System.Threading.Tasks.Task<Tenant?> GetTenantAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        _tenants.TryGetValue(tenantId, out var tenant);
        return global::System.Threading.Tasks.Task.FromResult(tenant);
    }

    public global::System.Threading.Tasks.Task<Tenant?> GetTenantByNameAsync(
        string name, CancellationToken ct = default)
    {
        var tenant = _tenants.Values.FirstOrDefault(
            t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return global::System.Threading.Tasks.Task.FromResult(tenant);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<Tenant>> ListTenantsAsync(
        TenantStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        IEnumerable<Tenant> query = _tenants.Values.OrderByDescending(t => t.CreatedAtUtc);
        if (status.HasValue) query = query.Where(t => t.Status == status.Value);
        IReadOnlyList<Tenant> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<bool> RemoveTenantAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        bool removed = _tenants.TryRemove(tenantId, out _);
        return global::System.Threading.Tasks.Task.FromResult(removed);
    }

    // ── Managed workflows ─────────────────────────────────────

    public global::System.Threading.Tasks.Task<ManagedWorkflow> UpsertWorkflowAsync(
        ManagedWorkflow workflow, CancellationToken ct = default)
    {
        _workflows[workflow.Id] = workflow;
        return global::System.Threading.Tasks.Task.FromResult(workflow);
    }

    public global::System.Threading.Tasks.Task<ManagedWorkflow?> GetWorkflowAsync(
        Guid workflowId, CancellationToken ct = default)
    {
        _workflows.TryGetValue(workflowId, out var workflow);
        return global::System.Threading.Tasks.Task.FromResult(workflow);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<ManagedWorkflow>> ListWorkflowsAsync(
        string? tenantId, ManagedWorkflowStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        IEnumerable<ManagedWorkflow> query = _workflows.Values.OrderByDescending(w => w.CreatedAtUtc);
        if (!string.IsNullOrWhiteSpace(tenantId)) query = query.Where(w => w.TenantId == tenantId);
        if (status.HasValue) query = query.Where(w => w.Status == status.Value);
        IReadOnlyList<ManagedWorkflow> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    // ── Managed agents ────────────────────────────────────────

    public global::System.Threading.Tasks.Task<ManagedAgent> UpsertAgentAsync(
        ManagedAgent agent, CancellationToken ct = default)
    {
        _agents[agent.Id] = agent;
        return global::System.Threading.Tasks.Task.FromResult(agent);
    }

    public global::System.Threading.Tasks.Task<ManagedAgent?> GetAgentAsync(
        Guid agentId, CancellationToken ct = default)
    {
        _agents.TryGetValue(agentId, out var agent);
        return global::System.Threading.Tasks.Task.FromResult(agent);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<ManagedAgent>> ListAgentsAsync(
        string? tenantId, ManagedAgentStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        IEnumerable<ManagedAgent> query = _agents.Values.OrderByDescending(a => a.RegisteredAtUtc);
        if (!string.IsNullOrWhiteSpace(tenantId)) query = query.Where(a => a.TenantId == tenantId);
        if (status.HasValue) query = query.Where(a => a.Status == status.Value);
        IReadOnlyList<ManagedAgent> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<bool> RemoveAgentAsync(
        Guid agentId, CancellationToken ct = default)
    {
        bool removed = _agents.TryRemove(agentId, out _);
        return global::System.Threading.Tasks.Task.FromResult(removed);
    }

    // ── Policies ──────────────────────────────────────────────

    public global::System.Threading.Tasks.Task<PlatformPolicy> UpsertPolicyAsync(
        PlatformPolicy policy, CancellationToken ct = default)
    {
        _policies[policy.Id] = policy;
        return global::System.Threading.Tasks.Task.FromResult(policy);
    }

    public global::System.Threading.Tasks.Task<PlatformPolicy?> GetPolicyAsync(
        Guid policyId, CancellationToken ct = default)
    {
        _policies.TryGetValue(policyId, out var policy);
        return global::System.Threading.Tasks.Task.FromResult(policy);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<PlatformPolicy>> ListPoliciesAsync(
        string? tenantId, PlatformPolicyType? policyType, bool? isEnabled,
        int offset, int limit, CancellationToken ct = default)
    {
        IEnumerable<PlatformPolicy> query = _policies.Values.OrderBy(p => p.Priority);
        if (!string.IsNullOrWhiteSpace(tenantId)) query = query.Where(p => p.TenantId == tenantId);
        if (policyType.HasValue) query = query.Where(p => p.PolicyType == policyType.Value);
        if (isEnabled.HasValue) query = query.Where(p => p.IsEnabled == isEnabled.Value);
        IReadOnlyList<PlatformPolicy> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<bool> RemovePolicyAsync(
        Guid policyId, CancellationToken ct = default)
    {
        bool removed = _policies.TryRemove(policyId, out _);
        return global::System.Threading.Tasks.Task.FromResult(removed);
    }

    // ── Configurations ────────────────────────────────────────

    private static string ConfigKey(string tenantId, string scope, string key) =>
        $"{tenantId}:{scope}:{key}";

    public global::System.Threading.Tasks.Task<PlatformConfiguration> UpsertConfigurationAsync(
        PlatformConfiguration config, CancellationToken ct = default)
    {
        var compositeKey = ConfigKey(config.TenantId, config.Scope, config.Key);
        _configurations[compositeKey] = config;
        return global::System.Threading.Tasks.Task.FromResult(config);
    }

    public global::System.Threading.Tasks.Task<PlatformConfiguration?> GetConfigurationAsync(
        string tenantId, string scope, string key, CancellationToken ct = default)
    {
        _configurations.TryGetValue(ConfigKey(tenantId, scope, key), out var config);
        return global::System.Threading.Tasks.Task.FromResult(config);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<PlatformConfiguration>> ListConfigurationsAsync(
        string? tenantId, string? scope, int offset, int limit, CancellationToken ct = default)
    {
        IEnumerable<PlatformConfiguration> query = _configurations.Values
            .OrderBy(c => c.TenantId).ThenBy(c => c.Scope).ThenBy(c => c.Key);
        if (!string.IsNullOrWhiteSpace(tenantId)) query = query.Where(c => c.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(scope)) query = query.Where(c => c.Scope == scope);
        IReadOnlyList<PlatformConfiguration> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<bool> RemoveConfigurationAsync(
        string tenantId, string scope, string key, CancellationToken ct = default)
    {
        bool removed = _configurations.TryRemove(ConfigKey(tenantId, scope, key), out _);
        return global::System.Threading.Tasks.Task.FromResult(removed);
    }

    // ── Counts ────────────────────────────────────────────────

    public global::System.Threading.Tasks.Task<int> CountTenantsAsync(
        TenantStatus? status = null, CancellationToken ct = default)
    {
        int count = status.HasValue
            ? _tenants.Values.Count(t => t.Status == status.Value)
            : _tenants.Count;
        return global::System.Threading.Tasks.Task.FromResult(count);
    }

    public global::System.Threading.Tasks.Task<int> CountWorkflowsAsync(
        string? tenantId = null, CancellationToken ct = default)
    {
        int count = string.IsNullOrWhiteSpace(tenantId)
            ? _workflows.Count
            : _workflows.Values.Count(w => w.TenantId == tenantId);
        return global::System.Threading.Tasks.Task.FromResult(count);
    }

    public global::System.Threading.Tasks.Task<int> CountAgentsAsync(
        string? tenantId = null, CancellationToken ct = default)
    {
        int count = string.IsNullOrWhiteSpace(tenantId)
            ? _agents.Count
            : _agents.Values.Count(a => a.TenantId == tenantId);
        return global::System.Threading.Tasks.Task.FromResult(count);
    }

    public global::System.Threading.Tasks.Task<int> CountPoliciesAsync(
        string? tenantId = null, CancellationToken ct = default)
    {
        int count = string.IsNullOrWhiteSpace(tenantId)
            ? _policies.Count
            : _policies.Values.Count(p => p.TenantId == tenantId);
        return global::System.Threading.Tasks.Task.FromResult(count);
    }

    public global::System.Threading.Tasks.Task<int> CountConfigurationsAsync(
        string? tenantId = null, CancellationToken ct = default)
    {
        int count = string.IsNullOrWhiteSpace(tenantId)
            ? _configurations.Count
            : _configurations.Values.Count(c => c.TenantId == tenantId);
        return global::System.Threading.Tasks.Task.FromResult(count);
    }
}
