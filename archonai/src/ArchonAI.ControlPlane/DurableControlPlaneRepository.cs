using System.Text.Json;
using System.Text.Json.Serialization;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ControlPlane;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.ControlPlane;

/// <summary>
/// File-backed persistent implementation of IControlPlaneRepository.
/// Stores all control plane state (tenants, workflows, agents, policies, configs)
/// in a JSON file with an in-memory hot cache.
/// </summary>
public sealed class DurableControlPlaneRepository : IControlPlaneRepository, IDisposable
{
    private readonly ControlPlaneState _state;
    private readonly string _filePath;
    private readonly ILogger<DurableControlPlaneRepository> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public DurableControlPlaneRepository(
        IOptions<ControlPlaneOptions> options,
        ILogger<DurableControlPlaneRepository> logger)
    {
        _logger = logger;
        _filePath = options.Value.PersistencePath ?? Path.Combine(
            AppContext.BaseDirectory, "data", "control-plane.json");
        _state = LoadFromDisk();
    }

    // ── Tenants ───────────────────────────────────────────────

    public global::System.Threading.Tasks.Task<Tenant> UpsertTenantAsync(Tenant tenant, CancellationToken ct = default)
    {
        _state.Tenants[tenant.Id] = tenant;
        ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(tenant);
    }

    public global::System.Threading.Tasks.Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        _state.Tenants.TryGetValue(tenantId, out var tenant);
        return global::System.Threading.Tasks.Task.FromResult(tenant);
    }

    public global::System.Threading.Tasks.Task<Tenant?> GetTenantByNameAsync(string name, CancellationToken ct = default)
    {
        var tenant = _state.Tenants.Values.FirstOrDefault(
            t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return global::System.Threading.Tasks.Task.FromResult(tenant);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<Tenant>> ListTenantsAsync(
        TenantStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        IEnumerable<Tenant> query = _state.Tenants.Values.OrderByDescending(t => t.CreatedAtUtc);
        if (status.HasValue) query = query.Where(t => t.Status == status.Value);
        IReadOnlyList<Tenant> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<bool> RemoveTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        bool removed = _state.Tenants.TryRemove(tenantId, out _);
        if (removed) ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(removed);
    }

    // ── Managed workflows ─────────────────────────────────────

    public global::System.Threading.Tasks.Task<ManagedWorkflow> UpsertWorkflowAsync(ManagedWorkflow workflow, CancellationToken ct = default)
    {
        _state.Workflows[workflow.Id] = workflow;
        ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(workflow);
    }

    public global::System.Threading.Tasks.Task<ManagedWorkflow?> GetWorkflowAsync(Guid workflowId, CancellationToken ct = default)
    {
        _state.Workflows.TryGetValue(workflowId, out var workflow);
        return global::System.Threading.Tasks.Task.FromResult(workflow);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<ManagedWorkflow>> ListWorkflowsAsync(
        string? tenantId, ManagedWorkflowStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        IEnumerable<ManagedWorkflow> query = _state.Workflows.Values.OrderByDescending(w => w.CreatedAtUtc);
        if (!string.IsNullOrWhiteSpace(tenantId)) query = query.Where(w => w.TenantId == tenantId);
        if (status.HasValue) query = query.Where(w => w.Status == status.Value);
        IReadOnlyList<ManagedWorkflow> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    // ── Managed agents ────────────────────────────────────────

    public global::System.Threading.Tasks.Task<ManagedAgent> UpsertAgentAsync(ManagedAgent agent, CancellationToken ct = default)
    {
        _state.Agents[agent.Id] = agent;
        ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(agent);
    }

    public global::System.Threading.Tasks.Task<ManagedAgent?> GetAgentAsync(Guid agentId, CancellationToken ct = default)
    {
        _state.Agents.TryGetValue(agentId, out var agent);
        return global::System.Threading.Tasks.Task.FromResult(agent);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<ManagedAgent>> ListAgentsAsync(
        string? tenantId, ManagedAgentStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        IEnumerable<ManagedAgent> query = _state.Agents.Values.OrderByDescending(a => a.RegisteredAtUtc);
        if (!string.IsNullOrWhiteSpace(tenantId)) query = query.Where(a => a.TenantId == tenantId);
        if (status.HasValue) query = query.Where(a => a.Status == status.Value);
        IReadOnlyList<ManagedAgent> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<bool> RemoveAgentAsync(Guid agentId, CancellationToken ct = default)
    {
        bool removed = _state.Agents.TryRemove(agentId, out _);
        if (removed) ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(removed);
    }

    // ── Policies ──────────────────────────────────────────────

    public global::System.Threading.Tasks.Task<PlatformPolicy> UpsertPolicyAsync(PlatformPolicy policy, CancellationToken ct = default)
    {
        _state.Policies[policy.Id] = policy;
        ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(policy);
    }

    public global::System.Threading.Tasks.Task<PlatformPolicy?> GetPolicyAsync(Guid policyId, CancellationToken ct = default)
    {
        _state.Policies.TryGetValue(policyId, out var policy);
        return global::System.Threading.Tasks.Task.FromResult(policy);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<PlatformPolicy>> ListPoliciesAsync(
        string? tenantId, PlatformPolicyType? policyType, bool? isEnabled,
        int offset, int limit, CancellationToken ct = default)
    {
        IEnumerable<PlatformPolicy> query = _state.Policies.Values.OrderBy(p => p.Priority);
        if (!string.IsNullOrWhiteSpace(tenantId)) query = query.Where(p => p.TenantId == tenantId);
        if (policyType.HasValue) query = query.Where(p => p.PolicyType == policyType.Value);
        if (isEnabled.HasValue) query = query.Where(p => p.IsEnabled == isEnabled.Value);
        IReadOnlyList<PlatformPolicy> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<bool> RemovePolicyAsync(Guid policyId, CancellationToken ct = default)
    {
        bool removed = _state.Policies.TryRemove(policyId, out _);
        if (removed) ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(removed);
    }

    // ── Configurations ────────────────────────────────────────

    private static string ConfigKey(string tenantId, string scope, string key) =>
        $"{tenantId}:{scope}:{key}";

    public global::System.Threading.Tasks.Task<PlatformConfiguration> UpsertConfigurationAsync(PlatformConfiguration config, CancellationToken ct = default)
    {
        _state.Configurations[ConfigKey(config.TenantId, config.Scope, config.Key)] = config;
        ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(config);
    }

    public global::System.Threading.Tasks.Task<PlatformConfiguration?> GetConfigurationAsync(
        string tenantId, string scope, string key, CancellationToken ct = default)
    {
        _state.Configurations.TryGetValue(ConfigKey(tenantId, scope, key), out var config);
        return global::System.Threading.Tasks.Task.FromResult(config);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<PlatformConfiguration>> ListConfigurationsAsync(
        string? tenantId, string? scope, int offset, int limit, CancellationToken ct = default)
    {
        IEnumerable<PlatformConfiguration> query = _state.Configurations.Values
            .OrderBy(c => c.TenantId).ThenBy(c => c.Scope).ThenBy(c => c.Key);
        if (!string.IsNullOrWhiteSpace(tenantId)) query = query.Where(c => c.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(scope)) query = query.Where(c => c.Scope == scope);
        IReadOnlyList<PlatformConfiguration> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<bool> RemoveConfigurationAsync(
        string tenantId, string scope, string key, CancellationToken ct = default)
    {
        bool removed = _state.Configurations.TryRemove(ConfigKey(tenantId, scope, key), out _);
        if (removed) ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(removed);
    }

    // ── Counts ────────────────────────────────────────────────

    public global::System.Threading.Tasks.Task<int> CountTenantsAsync(TenantStatus? status = null, CancellationToken ct = default)
    {
        int count = status.HasValue ? _state.Tenants.Values.Count(t => t.Status == status.Value) : _state.Tenants.Count;
        return global::System.Threading.Tasks.Task.FromResult(count);
    }

    public global::System.Threading.Tasks.Task<int> CountWorkflowsAsync(string? tenantId = null, CancellationToken ct = default)
    {
        int count = string.IsNullOrWhiteSpace(tenantId) ? _state.Workflows.Count : _state.Workflows.Values.Count(w => w.TenantId == tenantId);
        return global::System.Threading.Tasks.Task.FromResult(count);
    }

    public global::System.Threading.Tasks.Task<int> CountAgentsAsync(string? tenantId = null, CancellationToken ct = default)
    {
        int count = string.IsNullOrWhiteSpace(tenantId) ? _state.Agents.Count : _state.Agents.Values.Count(a => a.TenantId == tenantId);
        return global::System.Threading.Tasks.Task.FromResult(count);
    }

    public global::System.Threading.Tasks.Task<int> CountPoliciesAsync(string? tenantId = null, CancellationToken ct = default)
    {
        int count = string.IsNullOrWhiteSpace(tenantId) ? _state.Policies.Count : _state.Policies.Values.Count(p => p.TenantId == tenantId);
        return global::System.Threading.Tasks.Task.FromResult(count);
    }

    public global::System.Threading.Tasks.Task<int> CountConfigurationsAsync(string? tenantId = null, CancellationToken ct = default)
    {
        int count = string.IsNullOrWhiteSpace(tenantId) ? _state.Configurations.Count : _state.Configurations.Values.Count(c => c.TenantId == tenantId);
        return global::System.Threading.Tasks.Task.FromResult(count);
    }

    // ── Persistence ───────────────────────────────────────────

    private void ScheduleFlush() => _ = FlushAsync();

    internal async Task FlushAsync()
    {
        if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(5)))
            return;

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var snapshot = new ControlPlaneSnapshot(
                _state.Tenants.Values.ToList(),
                _state.Workflows.Values.ToList(),
                _state.Agents.Values.ToList(),
                _state.Policies.Values.ToList(),
                _state.Configurations.Values.ToList());

            var json = JsonSerializer.Serialize(snapshot, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush control plane state to {Path}", _filePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private ControlPlaneState LoadFromDisk()
    {
        var state = new ControlPlaneState();
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("No control plane state file at {Path}, starting fresh", _filePath);
                return state;
            }

            var json = File.ReadAllText(_filePath);
            var snapshot = JsonSerializer.Deserialize<ControlPlaneSnapshot>(json, JsonOpts);
            if (snapshot is not null)
            {
                foreach (var t in snapshot.Tenants) state.Tenants[t.Id] = t;
                foreach (var w in snapshot.Workflows) state.Workflows[w.Id] = w;
                foreach (var a in snapshot.Agents) state.Agents[a.Id] = a;
                foreach (var p in snapshot.Policies) state.Policies[p.Id] = p;
                foreach (var c in snapshot.Configurations)
                    state.Configurations[ConfigKey(c.TenantId, c.Scope, c.Key)] = c;

                _logger.LogInformation(
                    "Loaded control plane state: {Tenants} tenants, {Workflows} workflows, {Agents} agents, {Policies} policies, {Configs} configs",
                    snapshot.Tenants.Count, snapshot.Workflows.Count, snapshot.Agents.Count,
                    snapshot.Policies.Count, snapshot.Configurations.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load control plane state from {Path}", _filePath);
        }
        return state;
    }

    public void Dispose() => _writeLock.Dispose();

    // ── Internal state container ──────────────────────────────

    private sealed class ControlPlaneState
    {
        public System.Collections.Concurrent.ConcurrentDictionary<Guid, Tenant> Tenants { get; } = new();
        public System.Collections.Concurrent.ConcurrentDictionary<Guid, ManagedWorkflow> Workflows { get; } = new();
        public System.Collections.Concurrent.ConcurrentDictionary<Guid, ManagedAgent> Agents { get; } = new();
        public System.Collections.Concurrent.ConcurrentDictionary<Guid, PlatformPolicy> Policies { get; } = new();
        public System.Collections.Concurrent.ConcurrentDictionary<string, PlatformConfiguration> Configurations { get; } = new();
    }

    private sealed record ControlPlaneSnapshot(
        List<Tenant> Tenants,
        List<ManagedWorkflow> Workflows,
        List<ManagedAgent> Agents,
        List<PlatformPolicy> Policies,
        List<PlatformConfiguration> Configurations);
}
