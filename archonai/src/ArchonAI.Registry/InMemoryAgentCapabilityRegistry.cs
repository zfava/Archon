using System.Collections.Concurrent;
using ArchonAI.Core.Models;

namespace ArchonAI.Registry;

public sealed class InMemoryAgentCapabilityRegistry : IAgentCapabilityRegistry
{
    private readonly ConcurrentDictionary<Guid, AgentCapabilityProfile> _profiles = new();

    public global::System.Threading.Tasks.Task RegisterOrUpdateAgentAsync(
        Agent agent,
        IReadOnlyList<string> tools,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _profiles.AddOrUpdate(
            agent.Id,
            _ => new AgentCapabilityProfile(
                AgentId: agent.Id,
                AgentName: agent.Name,
                Version: agent.Version,
                Capabilities: agent.Capabilities.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Tools: tools.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Permissions: permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                AverageLatencyMs: 0,
                AverageCost: 0,
                Executions: 0,
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

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task ReportExecutionAsync(
        Guid agentId,
        double latencyMs,
        decimal cost,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _profiles.AddOrUpdate(agentId,
            _ => new AgentCapabilityProfile(
                AgentId: agentId,
                AgentName: "unknown",
                Version: "unknown",
                Capabilities: Array.Empty<string>(),
                Tools: Array.Empty<string>(),
                Permissions: Array.Empty<string>(),
                AverageLatencyMs: Math.Max(0, latencyMs),
                AverageCost: Math.Max(0, cost),
                Executions: 1,
                UpdatedAtUtc: DateTimeOffset.UtcNow),
            (_, existing) =>
            {
                long newExecutions = existing.Executions + 1;
                double nextLatency = ((existing.AverageLatencyMs * existing.Executions) + Math.Max(0, latencyMs)) / newExecutions;
                decimal nextCost = ((existing.AverageCost * existing.Executions) + Math.Max(0, cost)) / newExecutions;

                return existing with
                {
                    AverageLatencyMs = nextLatency,
                    AverageCost = nextCost,
                    Executions = newExecutions,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
            });

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<AgentCapabilityProfile>> QueryByCapabilityAsync(
        string capability,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = _profiles.Values
            .Where(profile => profile.Capabilities.Any(c => c.Equals(capability, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(profile => profile.AverageLatencyMs)
            .ThenBy(profile => profile.AverageCost)
            .ToArray();

        return global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentCapabilityProfile>>(items);
    }

    public global::System.Threading.Tasks.Task<AgentCapabilityProfile?> GetAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _profiles.TryGetValue(agentId, out AgentCapabilityProfile? profile);
        return global::System.Threading.Tasks.Task.FromResult(profile);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<AgentCapabilityProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentCapabilityProfile>>(
            _profiles.Values.OrderBy(p => p.AgentName).ToArray());
    }
}
