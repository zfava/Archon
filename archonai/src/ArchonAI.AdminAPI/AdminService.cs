using System.Collections.Concurrent;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Admin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.AdminAPI;

public sealed class AdminService : IAdminService
{
    private readonly IReadOnlyList<IAgent> _agents;
    private readonly IEventBus _eventBus;
    private readonly ILogger<AdminService> _logger;
    private readonly AdminOptions _options;

    private readonly ConcurrentDictionary<Guid, bool> _agentEnabledOverrides = new();
    private readonly ConcurrentBag<WorkflowInfo> _workflows = [];
    private PolicyConfiguration _policyConfig = new(
        ForbiddenCapabilities: [],
        HighRiskCapabilities: [],
        ApprovalCheckpointCapabilities: [],
        MinConfidenceThreshold: 0.7,
        AutoBlockRiskThreshold: 90,
        ApprovalRiskThreshold: 70,
        RequireApprovalForHighRisk: true);

    private long _agentQueries;
    private long _workflowQueries;
    private long _policyUpdates;
    private long _monitoringSnapshots;

    public AdminService(
        IEnumerable<IAgent> agents,
        IEventBus eventBus,
        ILogger<AdminService> logger,
        IOptions<AdminOptions> options)
    {
        _agents = agents.ToList();
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
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

        var auditEvent = new SystemEvent(
            Guid.NewGuid(),
            "Admin.AgentEnabledChanged",
            nameof(AdminService),
            agentId,
            new Dictionary<string, string>
            {
                ["agentId"] = agentId.ToString(),
                ["enabled"] = enabled.ToString()
            },
            DateTimeOffset.UtcNow);

        await _eventBus.PublishAsync(auditEvent, cancellationToken);
        _logger.LogInformation("Agent {AgentId} enabled state set to {Enabled}", agentId, enabled);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<WorkflowInfo>> GetWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        Telemetry.AdminWorkflowQueries.Add(1);
        Interlocked.Increment(ref _workflowQueries);

        var result = _workflows
            .Take(_options.MaxWorkflowHistoryEntries)
            .ToList();

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
            Guid.NewGuid(),
            "Admin.WorkflowCancelled",
            nameof(AdminService),
            workflowId,
            new Dictionary<string, string>
            {
                ["workflowId"] = workflowId.ToString()
            },
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

        var auditEvent = new SystemEvent(
            Guid.NewGuid(),
            "Admin.PolicyUpdated",
            nameof(AdminService),
            Guid.NewGuid(),
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

        var activeAgents = _agents.Count(a => a.Describe().IsEnabled);
        var runningWorkflows = _workflows.Count(w => w.Status == "Running");

        var snapshot = new SystemMonitoringSnapshot(
            ActiveAgents: activeAgents,
            RunningWorkflows: runningWorkflows,
            TotalTasksExecuted: 0,
            TotalTasksFailed: 0,
            SystemCpuPercent: 0.0,
            SystemMemoryMb: GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024),
            ConnectorStatuses: new Dictionary<string, string>(),
            AdditionalMetrics: new Dictionary<string, string>(),
            CapturedAtUtc: DateTimeOffset.UtcNow);

        return global::System.Threading.Tasks.Task.FromResult(snapshot);
    }

    public AdminServiceStatus GetStatus()
    {
        return new AdminServiceStatus(
            IsActive: true,
            AgentQueries: Interlocked.Read(ref _agentQueries),
            WorkflowQueries: Interlocked.Read(ref _workflowQueries),
            PolicyUpdates: Interlocked.Read(ref _policyUpdates),
            MonitoringSnapshots: Interlocked.Read(ref _monitoringSnapshots),
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    private AgentInfo ToAgentInfo(IAgent agent)
    {
        var desc = agent.Describe();
        var isEnabled = _agentEnabledOverrides.TryGetValue(desc.Id, out var overrideEnabled)
            ? overrideEnabled
            : desc.IsEnabled;

        return new AgentInfo(
            Id: desc.Id,
            Name: desc.Name,
            Version: desc.Version,
            Capabilities: desc.Capabilities.Select(c => c.Name).ToList(),
            IsEnabled: isEnabled,
            ExecutionCount: 0,
            FailureCount: 0,
            RegisteredAtUtc: desc.RegisteredAtUtc);
    }
}
