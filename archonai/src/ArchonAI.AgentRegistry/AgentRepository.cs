using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.AgentRegistry;
using Microsoft.Extensions.Logging;

namespace ArchonAI.AgentRegistry;

/// <summary>
/// In-memory implementation of <see cref="IAgentRegistryRepository"/>.
/// </summary>
public sealed class AgentRepository : IAgentRegistryRepository
{
    private readonly ConcurrentDictionary<Guid, RegisteredAgent> _agents = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<AgentMetricSnapshot>> _metrics = new();
    private const int MaxMetricsPerAgent = 500;

    private readonly ILogger<AgentRepository> _logger;

    public AgentRepository(ILogger<AgentRepository> logger)
    {
        _logger = logger;
    }

    // ── Agents ───────────────────────────────────────────────────────

    public global::System.Threading.Tasks.Task<RegisteredAgent> UpsertAgentAsync(
        RegisteredAgent agent, CancellationToken ct = default)
    {
        _agents[agent.Id] = agent;
        _logger.LogDebug("Upserted agent {AgentId} '{Name}'", agent.Id, agent.Name);
        return global::System.Threading.Tasks.Task.FromResult(agent);
    }

    public global::System.Threading.Tasks.Task<RegisteredAgent?> GetAgentAsync(
        Guid agentId, CancellationToken ct = default)
    {
        _agents.TryGetValue(agentId, out var agent);
        return global::System.Threading.Tasks.Task.FromResult(agent);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<RegisteredAgent>> ListAgentsAsync(
        RegisteredAgentStatus? status, string? capability,
        int offset, int limit, CancellationToken ct = default)
    {
        IEnumerable<RegisteredAgent> query = _agents.Values
            .OrderByDescending(a => a.RegisteredAtUtc);

        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(capability))
            query = query.Where(a => a.Capabilities.Any(
                c => c.Name.Equals(capability, StringComparison.OrdinalIgnoreCase)));

        IReadOnlyList<RegisteredAgent> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<bool> RemoveAgentAsync(
        Guid agentId, CancellationToken ct = default)
    {
        bool removed = _agents.TryRemove(agentId, out _);
        _metrics.TryRemove(agentId, out _);
        if (removed)
            _logger.LogDebug("Removed agent {AgentId}", agentId);
        return global::System.Threading.Tasks.Task.FromResult(removed);
    }

    public global::System.Threading.Tasks.Task<int> CountAgentsAsync(
        RegisteredAgentStatus? status = null, CancellationToken ct = default)
    {
        int count = status.HasValue
            ? _agents.Values.Count(a => a.Status == status.Value)
            : _agents.Count;
        return global::System.Threading.Tasks.Task.FromResult(count);
    }

    // ── Metrics ──────────────────────────────────────────────────────

    public global::System.Threading.Tasks.Task AddMetricAsync(
        AgentMetricSnapshot metric, CancellationToken ct = default)
    {
        var queue = _metrics.GetOrAdd(metric.AgentId, _ => new ConcurrentQueue<AgentMetricSnapshot>());
        queue.Enqueue(metric);
        while (queue.Count > MaxMetricsPerAgent) queue.TryDequeue(out _);
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<AgentMetricSnapshot>> GetMetricsAsync(
        Guid agentId, int limit, CancellationToken ct = default)
    {
        if (!_metrics.TryGetValue(agentId, out var queue))
            return global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentMetricSnapshot>>(
                Array.Empty<AgentMetricSnapshot>());

        IReadOnlyList<AgentMetricSnapshot> result = queue
            .OrderByDescending(m => m.CollectedAtUtc)
            .Take(limit)
            .ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<AgentMetricSnapshot>> GetRecentMetricsAsync(
        int limit = 50, CancellationToken ct = default)
    {
        IReadOnlyList<AgentMetricSnapshot> result = _metrics.Values
            .SelectMany(q => q)
            .OrderByDescending(m => m.CollectedAtUtc)
            .Take(limit)
            .ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }
}
