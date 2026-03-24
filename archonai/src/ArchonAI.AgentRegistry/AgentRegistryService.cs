using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.AgentRegistry;
using Microsoft.Extensions.Logging;

namespace ArchonAI.AgentRegistry;

/// <summary>
/// Central service for managing the agent registry — registration, capability
/// updates, enable/disable lifecycle, heartbeat tracking, and metrics collection.
/// </summary>
public sealed class AgentRegistryService : IAgentRegistryService
{
    private readonly IAgentRegistryRepository _repository;
    private readonly IEventBus _eventBus;
    private readonly ILogger<AgentRegistryService> _logger;

    private long _registrations;
    private long _capabilityUpdates;
    private long _statusChanges;
    private long _metricsRecorded;

    public AgentRegistryService(
        IAgentRegistryRepository repository,
        IEventBus eventBus,
        ILogger<AgentRegistryService> logger)
    {
        _repository = repository;
        _eventBus = eventBus;
        _logger = logger;
    }

    // ── Registration ─────────────────────────────────────────────────

    public async global::System.Threading.Tasks.Task<RegisteredAgent> RegisterAgentAsync(
        string name, string description, string version,
        IReadOnlyList<AgentCapabilityRecord> capabilities,
        IReadOnlyDictionary<string, string>? configuration = null,
        CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("AgentRegistry.Register");
        Interlocked.Increment(ref _registrations);
        Telemetry.AgentRegistryRegistrations.Add(1);

        var agentId = Guid.NewGuid();
        var stamped = capabilities.Select(c => c with
        {
            Id = c.Id == Guid.Empty ? Guid.NewGuid() : c.Id,
            AgentId = agentId,
            AddedAtUtc = DateTimeOffset.UtcNow
        }).ToList();

        var agent = new RegisteredAgent(
            Id: agentId,
            Name: name,
            Description: description,
            Version: version,
            Status: RegisteredAgentStatus.Active,
            Capabilities: stamped,
            Configuration: configuration ?? new Dictionary<string, string>(),
            RegisteredAtUtc: DateTimeOffset.UtcNow,
            LastHeartbeatUtc: DateTimeOffset.UtcNow,
            DisabledAtUtc: null);

        await _repository.UpsertAgentAsync(agent, ct);

        _logger.LogInformation(
            "Agent registered: {AgentId} '{Name}' v{Version} with {CapCount} capabilities",
            agent.Id, name, version, stamped.Count);

        await EmitEventAsync("agentregistry.agent.registered", agent.Id.ToString(),
            $"Agent '{name}' v{version} registered with {stamped.Count} capabilities");

        return agent;
    }

    public async global::System.Threading.Tasks.Task<RegisteredAgent?> GetAgentAsync(
        Guid agentId, CancellationToken ct = default)
    {
        return await _repository.GetAgentAsync(agentId, ct);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<RegisteredAgent>> ListAgentsAsync(
        RegisteredAgentStatus? status = null, string? capability = null,
        int offset = 0, int limit = 50,
        CancellationToken ct = default)
    {
        return await _repository.ListAgentsAsync(status, capability, offset, limit, ct);
    }

    // ── Capability management ────────────────────────────────────────

    public async global::System.Threading.Tasks.Task<RegisteredAgent> UpdateCapabilitiesAsync(
        Guid agentId, IReadOnlyList<AgentCapabilityRecord> capabilities,
        CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("AgentRegistry.UpdateCapabilities");
        Interlocked.Increment(ref _capabilityUpdates);
        Telemetry.AgentRegistryCapabilityUpdates.Add(1);

        var agent = await _repository.GetAgentAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Agent {agentId} not found");

        var stamped = capabilities.Select(c => c with
        {
            Id = c.Id == Guid.Empty ? Guid.NewGuid() : c.Id,
            AgentId = agentId,
            AddedAtUtc = DateTimeOffset.UtcNow
        }).ToList();

        var updated = agent with { Capabilities = stamped };
        await _repository.UpsertAgentAsync(updated, ct);

        _logger.LogInformation(
            "Capabilities updated for agent {AgentId} '{Name}': {Count} capabilities",
            agentId, agent.Name, stamped.Count);

        await EmitEventAsync("agentregistry.capabilities.updated", agentId.ToString(),
            $"{stamped.Count} capabilities for '{agent.Name}'");

        return updated;
    }

    // ── Enable / disable ─────────────────────────────────────────────

    public async global::System.Threading.Tasks.Task<RegisteredAgent> EnableAgentAsync(
        Guid agentId, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("AgentRegistry.Enable");
        Interlocked.Increment(ref _statusChanges);
        Telemetry.AgentRegistryStatusChanges.Add(1);

        var agent = await _repository.GetAgentAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Agent {agentId} not found");

        if (agent.Status == RegisteredAgentStatus.Active)
            return agent;

        var enabled = agent with
        {
            Status = RegisteredAgentStatus.Active,
            DisabledAtUtc = null,
            LastHeartbeatUtc = DateTimeOffset.UtcNow
        };
        await _repository.UpsertAgentAsync(enabled, ct);

        _logger.LogInformation("Agent enabled: {AgentId} '{Name}'", agentId, agent.Name);
        await EmitEventAsync("agentregistry.agent.enabled", agentId.ToString(), agent.Name);

        return enabled;
    }

    public async global::System.Threading.Tasks.Task<RegisteredAgent> DisableAgentAsync(
        Guid agentId, string reason, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("AgentRegistry.Disable");
        Interlocked.Increment(ref _statusChanges);
        Telemetry.AgentRegistryStatusChanges.Add(1);

        var agent = await _repository.GetAgentAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Agent {agentId} not found");

        if (agent.Status == RegisteredAgentStatus.Disabled)
            return agent;

        var disabled = agent with
        {
            Status = RegisteredAgentStatus.Disabled,
            DisabledAtUtc = DateTimeOffset.UtcNow
        };
        await _repository.UpsertAgentAsync(disabled, ct);

        _logger.LogWarning(
            "Agent disabled: {AgentId} '{Name}', reason={Reason}",
            agentId, agent.Name, reason);
        await EmitEventAsync("agentregistry.agent.disabled", agentId.ToString(),
            $"Reason: {reason}");

        return disabled;
    }

    // ── Heartbeat ────────────────────────────────────────────────────

    public async global::System.Threading.Tasks.Task RecordHeartbeatAsync(
        Guid agentId, CancellationToken ct = default)
    {
        var agent = await _repository.GetAgentAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Agent {agentId} not found");

        var updated = agent with { LastHeartbeatUtc = DateTimeOffset.UtcNow };
        await _repository.UpsertAgentAsync(updated, ct);

        _logger.LogDebug("Heartbeat recorded for agent {AgentId}", agentId);
    }

    // ── Metrics ──────────────────────────────────────────────────────

    public async global::System.Threading.Tasks.Task<AgentMetricSnapshot> RecordMetricsAsync(
        Guid agentId, long totalExecutions, long successfulExecutions,
        long failedExecutions, double averageLatencyMs, double p95LatencyMs,
        double uptimePercent, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("AgentRegistry.RecordMetrics");
        Interlocked.Increment(ref _metricsRecorded);
        Telemetry.AgentRegistryMetricsRecorded.Add(1);

        _ = await _repository.GetAgentAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Agent {agentId} not found");

        var metric = new AgentMetricSnapshot(
            Id: Guid.NewGuid(),
            AgentId: agentId,
            TotalExecutions: totalExecutions,
            SuccessfulExecutions: successfulExecutions,
            FailedExecutions: failedExecutions,
            AverageLatencyMs: Math.Max(0, averageLatencyMs),
            P95LatencyMs: Math.Max(0, p95LatencyMs),
            UptimePercent: Math.Clamp(uptimePercent, 0, 100),
            CollectedAtUtc: DateTimeOffset.UtcNow);

        await _repository.AddMetricAsync(metric, ct);

        _logger.LogDebug(
            "Metrics recorded for agent {AgentId}: executions={Total}, latency={Latency}ms",
            agentId, totalExecutions, averageLatencyMs);

        return metric;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<AgentMetricSnapshot>> GetMetricsAsync(
        Guid agentId, int limit = 20, CancellationToken ct = default)
    {
        return await _repository.GetMetricsAsync(agentId, limit, ct);
    }

    // ── Dashboard ────────────────────────────────────────────────────

    public async global::System.Threading.Tasks.Task<AgentRegistryDashboard> GetDashboardAsync(
        CancellationToken ct = default)
    {
        int total = await _repository.CountAgentsAsync(ct: ct);
        int active = await _repository.CountAgentsAsync(RegisteredAgentStatus.Active, ct);
        int disabled = await _repository.CountAgentsAsync(RegisteredAgentStatus.Disabled, ct);
        int offline = await _repository.CountAgentsAsync(RegisteredAgentStatus.Offline, ct);
        var recentMetrics = await _repository.GetRecentMetricsAsync(50, ct);

        return new AgentRegistryDashboard(
            TotalAgents: total,
            ActiveAgents: active,
            DisabledAgents: disabled,
            OfflineAgents: offline,
            RecentMetrics: recentMetrics,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    // ── Deregistration ───────────────────────────────────────────────

    public async global::System.Threading.Tasks.Task DeregisterAgentAsync(
        Guid agentId, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("AgentRegistry.Deregister");
        Interlocked.Increment(ref _statusChanges);
        Telemetry.AgentRegistryStatusChanges.Add(1);

        var agent = await _repository.GetAgentAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Agent {agentId} not found");

        await _repository.RemoveAgentAsync(agentId, ct);

        _logger.LogInformation("Agent deregistered: {AgentId} '{Name}'", agentId, agent.Name);
        await EmitEventAsync("agentregistry.agent.deregistered", agentId.ToString(), agent.Name);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async global::System.Threading.Tasks.Task EmitEventAsync(
        string eventType, string source, string detail)
    {
        try
        {
            var payload = new Dictionary<string, string> { ["detail"] = detail };
            var evt = new Core.Models.SystemEvent(
                Id: Guid.NewGuid(),
                EventType: eventType,
                Source: source,
                CorrelationId: Guid.NewGuid(),
                Payload: payload,
                OccurredAtUtc: DateTimeOffset.UtcNow);
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit event {EventType}", eventType);
        }
    }
}
