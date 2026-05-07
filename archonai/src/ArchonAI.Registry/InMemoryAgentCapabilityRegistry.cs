using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Registry;

public sealed class InMemoryAgentCapabilityRegistry : IAgentCapabilityRegistry
{
    private const double SuccessWeight = 0.40;
    private const double LatencyWeight = 0.30;
    private const double CostWeight = 0.20;
    private const double ThroughputWeight = 0.10;

    private readonly ConcurrentDictionary<Guid, AgentCapabilityProfile> _profiles = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<double>> _latencyWindows = new();
    private readonly ConcurrentDictionary<Guid, long> _executionTimestamps = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<InMemoryAgentCapabilityRegistry> _logger;

    public InMemoryAgentCapabilityRegistry(IEventBus eventBus, ILogger<InMemoryAgentCapabilityRegistry> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task RegisterOrUpdateAgentAsync(
        Agent agent,
        IReadOnlyList<string> tools,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool isNew = !_profiles.ContainsKey(agent.Id);

        _profiles.AddOrUpdate(
            agent.Id,
            _ => new AgentCapabilityProfile(
                AgentId: agent.Id,
                AgentName: agent.Name,
                Version: agent.Version,
                Capabilities: agent.Capabilities.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Tools: tools.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Permissions: permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                SupportedTaskTypes: Array.Empty<string>(),
                AverageLatencyMs: 0,
                P95LatencyMs: 0,
                AverageCost: 0,
                Executions: 0,
                SuccessCount: 0,
                FailureCount: 0,
                SuccessRate: 0,
                Throughput: 0,
                UpdatedAtUtc: DateTimeOffset.UtcNow),
            (_, existing) => existing with
            {
                AgentName = agent.Name,
                Version = agent.Version,
                Capabilities = agent.Capabilities.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Tools = tools.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Permissions = permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

        string eventType = isNew ? "registry.agent.registered" : "registry.agent.updated";
        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: eventType,
            Source: "agent-capability-registry",
            CorrelationId: agent.Id,
            Payload: new Dictionary<string, string>
            {
                ["agentId"] = agent.Id.ToString(),
                ["agentName"] = agent.Name,
                ["version"] = agent.Version,
                ["capabilities"] = string.Join(",", agent.Capabilities.Select(c => c.Name)),
                ["tools"] = string.Join(",", tools)
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        _logger.LogInformation("Registry {Action} agent {AgentName} ({AgentId})",
            isNew ? "registered" : "updated", agent.Name, agent.Id);
    }

    public global::System.Threading.Tasks.Task RegisterSupportedTaskTypesAsync(
        Guid agentId,
        IReadOnlyList<string> taskTypes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_profiles.TryGetValue(agentId, out var existing))
        {
            var merged = existing.SupportedTaskTypes
                .Concat(taskTypes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _profiles[agentId] = existing with
            {
                SupportedTaskTypes = merged,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        }

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public async global::System.Threading.Tasks.Task ReportExecutionAsync(
        Guid agentId,
        string taskType,
        bool success,
        double latencyMs,
        decimal cost,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var safeLatency = Math.Max(0, latencyMs);
        var safeCost = Math.Max(0, cost);

        // Track latency window for P95 calculation
        var latencyQueue = _latencyWindows.GetOrAdd(agentId, _ => new ConcurrentQueue<double>());
        latencyQueue.Enqueue(safeLatency);
        while (latencyQueue.Count > 1000) latencyQueue.TryDequeue(out _);

        // Track execution timestamp for throughput
        _executionTimestamps[agentId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        _profiles.AddOrUpdate(agentId,
            _ => new AgentCapabilityProfile(
                AgentId: agentId,
                AgentName: "unknown",
                Version: "unknown",
                Capabilities: Array.Empty<string>(),
                Tools: Array.Empty<string>(),
                Permissions: Array.Empty<string>(),
                SupportedTaskTypes: string.IsNullOrWhiteSpace(taskType) ? Array.Empty<string>() : new[] { taskType },
                AverageLatencyMs: safeLatency,
                P95LatencyMs: safeLatency,
                AverageCost: safeCost,
                Executions: 1,
                SuccessCount: success ? 1 : 0,
                FailureCount: success ? 0 : 1,
                SuccessRate: success ? 1.0 : 0.0,
                Throughput: 0,
                UpdatedAtUtc: DateTimeOffset.UtcNow),
            (_, existing) =>
            {
                long newExec = existing.Executions + 1;
                long newSuccess = existing.SuccessCount + (success ? 1 : 0);
                long newFailure = existing.FailureCount + (success ? 0 : 1);
                double nextLatency = ((existing.AverageLatencyMs * existing.Executions) + safeLatency) / newExec;
                decimal nextCost = ((existing.AverageCost * existing.Executions) + safeCost) / newExec;
                double p95 = ComputeP95(latencyQueue);

                var taskTypes = existing.SupportedTaskTypes.ToList();
                if (!string.IsNullOrWhiteSpace(taskType) &&
                    !taskTypes.Contains(taskType, StringComparer.OrdinalIgnoreCase))
                {
                    taskTypes.Add(taskType);
                }

                return existing with
                {
                    SupportedTaskTypes = taskTypes,
                    AverageLatencyMs = nextLatency,
                    P95LatencyMs = p95,
                    AverageCost = nextCost,
                    Executions = newExec,
                    SuccessCount = newSuccess,
                    FailureCount = newFailure,
                    SuccessRate = (double)newSuccess / newExec,
                    Throughput = ComputeThroughput(newExec, existing.UpdatedAtUtc),
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
            });

        // Publish execution event
        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: success ? "registry.execution.success" : "registry.execution.failure",
            Source: "agent-capability-registry",
            CorrelationId: agentId,
            Payload: new Dictionary<string, string>
            {
                ["agentId"] = agentId.ToString(),
                ["taskType"] = taskType ?? "",
                ["success"] = success.ToString(),
                ["latencyMs"] = safeLatency.ToString("F2"),
                ["cost"] = safeCost.ToString()
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), cancellationToken);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<AgentCapabilityProfile>> QueryByCapabilityAsync(
        string capability,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = _profiles.Values
            .Where(p => !p.IsSuspended && p.Capabilities.Any(c => c.Equals(capability, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(p => ComputeAgentScore(p))
            .ToArray();

        return global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentCapabilityProfile>>(items);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<AgentCapabilityProfile>> QueryByTaskTypeAsync(
        string taskType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = _profiles.Values
            .Where(p => !p.IsSuspended && p.SupportedTaskTypes.Any(t => t.Equals(taskType, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(p => ComputeAgentScore(p))
            .ToArray();

        return global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentCapabilityProfile>>(items);
    }

    public global::System.Threading.Tasks.Task<AgentSelectionResult?> SelectBestAgentAsync(
        string requiredCapability,
        string? taskType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = _profiles.Values
            .Where(p => !p.IsSuspended && p.Capabilities.Any(c => c.Equals(requiredCapability, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (candidates.Count == 0)
            return global::System.Threading.Tasks.Task.FromResult<AgentSelectionResult?>(null);

        // Prefer agents that explicitly support the task type
        var taskTypeMatches = taskType is not null
            ? candidates.Where(p => p.SupportedTaskTypes.Any(t => t.Equals(taskType, StringComparison.OrdinalIgnoreCase))).ToList()
            : null;

        var pool = taskTypeMatches is { Count: > 0 } ? taskTypeMatches : candidates;

        var scored = pool
            .Select(p => (Profile: p, Score: ComputeAgentScore(p)))
            .OrderByDescending(x => x.Score)
            .First();

        string reason = BuildSelectionReason(scored.Profile, taskType, taskTypeMatches?.Count ?? 0);

        var result = new AgentSelectionResult(
            AgentId: scored.Profile.AgentId,
            AgentName: scored.Profile.AgentName,
            Score: scored.Score,
            SuccessRate: scored.Profile.SuccessRate,
            AverageLatencyMs: scored.Profile.AverageLatencyMs,
            AverageCost: scored.Profile.AverageCost,
            SelectionReason: reason);

        _logger.LogInformation(
            "Selected agent {AgentName} (score={Score:F3}) for capability '{Capability}' taskType='{TaskType}'",
            result.AgentName, result.Score, requiredCapability, taskType ?? "any");

        return global::System.Threading.Tasks.Task.FromResult<AgentSelectionResult?>(result);
    }

    public global::System.Threading.Tasks.Task<AgentCapabilityProfile?> GetAgentAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _profiles.TryGetValue(agentId, out AgentCapabilityProfile? profile);
        return global::System.Threading.Tasks.Task.FromResult(profile);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<AgentCapabilityProfile>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentCapabilityProfile>>(
            _profiles.Values.OrderBy(p => p.AgentName).ToArray());
    }

    public global::System.Threading.Tasks.Task<AgentPerformanceSnapshot?> GetPerformanceSnapshotAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_profiles.TryGetValue(agentId, out var profile))
            return global::System.Threading.Tasks.Task.FromResult<AgentPerformanceSnapshot?>(null);

        var snapshot = new AgentPerformanceSnapshot(
            AgentId: profile.AgentId,
            AgentName: profile.AgentName,
            AverageLatencyMs: profile.AverageLatencyMs,
            P95LatencyMs: profile.P95LatencyMs,
            AverageCost: profile.AverageCost,
            Executions: profile.Executions,
            SuccessRate: profile.SuccessRate,
            Throughput: profile.Throughput,
            Score: ComputeAgentScore(profile),
            SnapshotAtUtc: DateTimeOffset.UtcNow);

        return global::System.Threading.Tasks.Task.FromResult<AgentPerformanceSnapshot?>(snapshot);
    }

    public global::System.Threading.Tasks.Task SuspendAgentAsync(
        Guid agentId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_profiles.TryGetValue(agentId, out var existing))
        {
            _profiles[agentId] = existing with
            {
                IsSuspended = true,
                SuspendReason = reason,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _logger.LogWarning(
                "Agent {AgentId} '{AgentName}' suspended in capability registry: {Reason}",
                agentId, existing.AgentName, reason);
        }

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task ReinstateAgentAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_profiles.TryGetValue(agentId, out var existing) && existing.IsSuspended)
        {
            _profiles[agentId] = existing with
            {
                IsSuspended = false,
                SuspendReason = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _logger.LogInformation(
                "Agent {AgentId} '{AgentName}' reinstated in capability registry",
                agentId, existing.AgentName);
        }

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    // ══════════════════════════════════════════════════════════════
    //  Scoring and helpers
    // ══════════════════════════════════════════════════════════════

    private static double ComputeAgentScore(AgentCapabilityProfile profile)
    {
        if (profile.Executions == 0) return 0.5; // neutral for untested agents

        // Normalize each metric to [0, 1]
        double successScore = profile.SuccessRate;
        double latencyScore = 1.0 / (1.0 + (profile.AverageLatencyMs / 1000.0));
        double costScore = 1.0 / (1.0 + (double)profile.AverageCost);
        double throughputScore = Math.Min(1.0, profile.Throughput / 10.0);

        return (SuccessWeight * successScore)
             + (LatencyWeight * latencyScore)
             + (CostWeight * costScore)
             + (ThroughputWeight * throughputScore);
    }

    private static double ComputeP95(ConcurrentQueue<double> latencyQueue)
    {
        var sorted = latencyQueue.ToArray();
        Array.Sort(sorted);
        if (sorted.Length == 0) return 0;
        int index = (int)Math.Ceiling(sorted.Length * 0.95) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static double ComputeThroughput(long executions, DateTimeOffset firstSeen)
    {
        var elapsed = DateTimeOffset.UtcNow - firstSeen;
        if (elapsed.TotalMinutes < 1) return executions;
        return executions / elapsed.TotalMinutes;
    }

    private static string BuildSelectionReason(AgentCapabilityProfile profile, string? taskType, int taskTypeMatchCount)
    {
        var parts = new List<string>();

        if (taskType is not null && profile.SupportedTaskTypes.Any(t => t.Equals(taskType, StringComparison.OrdinalIgnoreCase)))
            parts.Add($"supports task type '{taskType}'");

        if (profile.Executions > 0)
        {
            parts.Add($"success rate {profile.SuccessRate:P0}");
            parts.Add($"avg latency {profile.AverageLatencyMs:F0}ms");
            parts.Add($"avg cost {profile.AverageCost:F4}");
        }
        else
        {
            parts.Add("no execution history (neutral score)");
        }

        if (taskTypeMatchCount == 0 && taskType is not null)
            parts.Add($"no agents explicitly support task type '{taskType}', selected by capability match");

        return string.Join("; ", parts);
    }
}
