using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Admin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.AdminAPI;

/// <summary>
/// File-backed persistent admin service. Persists agent overrides and policy
/// configuration to JSON on disk. Workflow info and metrics are derived from
/// other stores and remain ephemeral.
/// </summary>
public sealed class DurableAdminService : IAdminService, IDisposable
{
    private readonly IReadOnlyList<IAgent> _agents;
    private readonly IEventBus _eventBus;
    private readonly ILogger<DurableAdminService> _logger;
    private readonly AdminOptions _options;
    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly ConcurrentDictionary<Guid, bool> _agentEnabledOverrides = new();
    private readonly ConcurrentBag<WorkflowInfo> _workflows = [];
    private PolicyConfiguration _policyConfig;

    private long _agentQueries;
    private long _workflowQueries;
    private long _policyUpdates;
    private long _monitoringSnapshots;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public DurableAdminService(
        IEnumerable<IAgent> agents,
        IEventBus eventBus,
        ILogger<DurableAdminService> logger,
        IOptions<AdminOptions> options)
    {
        _agents = agents.ToList();
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
        _filePath = options.Value.PersistencePath ?? Path.Combine(
            AppContext.BaseDirectory, "data", "admin-state.json");

        _policyConfig = new PolicyConfiguration(
            ForbiddenCapabilities: [],
            HighRiskCapabilities: [],
            ApprovalCheckpointCapabilities: [],
            MinConfidenceThreshold: 0.7,
            AutoBlockRiskThreshold: 90,
            ApprovalRiskThreshold: 70,
            RequireApprovalForHighRisk: true);

        LoadFromDisk();
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<AgentInfo>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        Telemetry.AdminAgentQueries.Add(1);
        Interlocked.Increment(ref _agentQueries);
        var result = _agents.Select(ToAgentInfo).ToList();
        _logger.LogDebug("Returning {Count} agents", result.Count);
        return global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentInfo>>(result);
    }

    public global::System.Threading.Tasks.Task<AgentInfo?> GetAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        Telemetry.AdminAgentQueries.Add(1);
        Interlocked.Increment(ref _agentQueries);
        var agent = _agents.FirstOrDefault(a => a.Describe().Id == agentId);
        return global::System.Threading.Tasks.Task.FromResult(agent is null ? null : ToAgentInfo(agent));
    }

    public async global::System.Threading.Tasks.Task SetAgentEnabledAsync(Guid agentId, bool enabled, CancellationToken cancellationToken = default)
    {
        _agentEnabledOverrides[agentId] = enabled;
        _ = FlushAsync();

        var auditEvent = new SystemEvent(
            Guid.NewGuid(), "Admin.AgentEnabledChanged", nameof(DurableAdminService), agentId,
            new Dictionary<string, string> { ["agentId"] = agentId.ToString(), ["enabled"] = enabled.ToString() },
            DateTimeOffset.UtcNow);
        await _eventBus.PublishAsync(auditEvent, cancellationToken);
        _logger.LogInformation("Agent {AgentId} enabled state set to {Enabled}", agentId, enabled);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<WorkflowInfo>> GetWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        Telemetry.AdminWorkflowQueries.Add(1);
        Interlocked.Increment(ref _workflowQueries);
        var result = _workflows.Take(_options.MaxWorkflowHistoryEntries).ToList();
        return global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<WorkflowInfo>>(result);
    }

    public global::System.Threading.Tasks.Task<WorkflowInfo?> GetWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        Telemetry.AdminWorkflowQueries.Add(1);
        Interlocked.Increment(ref _workflowQueries);
        var workflow = _workflows.FirstOrDefault(w => w.Id == workflowId);
        return global::System.Threading.Tasks.Task.FromResult(workflow);
    }

    public async global::System.Threading.Tasks.Task CancelWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        var auditEvent = new SystemEvent(
            Guid.NewGuid(), "Admin.WorkflowCancelled", nameof(DurableAdminService), workflowId,
            new Dictionary<string, string> { ["workflowId"] = workflowId.ToString() },
            DateTimeOffset.UtcNow);
        await _eventBus.PublishAsync(auditEvent, cancellationToken);
        _logger.LogInformation("Workflow {WorkflowId} cancellation requested", workflowId);
    }

    public global::System.Threading.Tasks.Task<PolicyConfiguration> GetPolicyConfigAsync(CancellationToken cancellationToken = default)
    {
        Telemetry.AdminPolicyUpdates.Add(1);
        Interlocked.Increment(ref _policyUpdates);
        return global::System.Threading.Tasks.Task.FromResult(_policyConfig);
    }

    public async global::System.Threading.Tasks.Task UpdatePolicyConfigAsync(PolicyConfiguration policy, CancellationToken cancellationToken = default)
    {
        Telemetry.AdminPolicyUpdates.Add(1);
        Interlocked.Increment(ref _policyUpdates);
        _policyConfig = policy;
        _ = FlushAsync();

        var auditEvent = new SystemEvent(
            Guid.NewGuid(), "Admin.PolicyUpdated", nameof(DurableAdminService), Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["minConfidenceThreshold"] = policy.MinConfidenceThreshold.ToString("F2"),
                ["autoBlockRiskThreshold"] = policy.AutoBlockRiskThreshold.ToString(),
                ["approvalRiskThreshold"] = policy.ApprovalRiskThreshold.ToString(),
                ["requireApprovalForHighRisk"] = policy.RequireApprovalForHighRisk.ToString()
            },
            DateTimeOffset.UtcNow);
        await _eventBus.PublishAsync(auditEvent, cancellationToken);
        _logger.LogInformation("Policy configuration updated");
    }

    public global::System.Threading.Tasks.Task<SystemMonitoringSnapshot> GetSystemSnapshotAsync(CancellationToken cancellationToken = default)
    {
        Telemetry.AdminMonitoringSnapshots.Add(1);
        Interlocked.Increment(ref _monitoringSnapshots);

        var snapshot = new SystemMonitoringSnapshot(
            ActiveAgents: _agents.Count(a => a.Describe().IsEnabled),
            RunningWorkflows: _workflows.Count(w => w.Status == "Running"),
            TotalTasksExecuted: 0, TotalTasksFailed: 0,
            SystemCpuPercent: 0.0,
            SystemMemoryMb: GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024),
            ConnectorStatuses: new Dictionary<string, string>(),
            AdditionalMetrics: new Dictionary<string, string>(),
            CapturedAtUtc: DateTimeOffset.UtcNow);
        return global::System.Threading.Tasks.Task.FromResult(snapshot);
    }

    public AdminServiceStatus GetStatus() => new(
        IsActive: true,
        AgentQueries: Interlocked.Read(ref _agentQueries),
        WorkflowQueries: Interlocked.Read(ref _workflowQueries),
        PolicyUpdates: Interlocked.Read(ref _policyUpdates),
        MonitoringSnapshots: Interlocked.Read(ref _monitoringSnapshots),
        StatusAsOfUtc: DateTimeOffset.UtcNow);

    private AgentInfo ToAgentInfo(IAgent agent)
    {
        var desc = agent.Describe();
        var isEnabled = _agentEnabledOverrides.TryGetValue(desc.Id, out var overrideEnabled) ? overrideEnabled : desc.IsEnabled;
        return new AgentInfo(desc.Id, desc.Name, desc.Version,
            desc.Capabilities.Select(c => c.Name).ToList(),
            isEnabled, 0, 0, desc.RegisteredAtUtc);
    }

    // ── Persistence ───────────────────────────────────────────

    internal async global::System.Threading.Tasks.Task FlushAsync()
    {
        if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(5)))
            return;
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var snapshot = new AdminStateSnapshot(
                _agentEnabledOverrides.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                _policyConfig);
            var json = JsonSerializer.Serialize(snapshot, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush admin state to {Path}", _filePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("No admin state file at {Path}, starting fresh", _filePath);
                return;
            }

            var json = File.ReadAllText(_filePath);
            var snapshot = JsonSerializer.Deserialize<AdminStateSnapshot>(json, JsonOpts);
            if (snapshot is not null)
            {
                foreach (var kvp in snapshot.AgentOverrides)
                {
                    if (Guid.TryParse(kvp.Key, out var id))
                        _agentEnabledOverrides[id] = kvp.Value;
                }
                _policyConfig = snapshot.PolicyConfig;
                _logger.LogInformation("Loaded admin state: {Overrides} agent overrides", snapshot.AgentOverrides.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load admin state from {Path}", _filePath);
        }
    }

    public void Dispose() => _writeLock.Dispose();

    private sealed record AdminStateSnapshot(
        Dictionary<string, bool> AgentOverrides,
        PolicyConfiguration PolicyConfig);
}
