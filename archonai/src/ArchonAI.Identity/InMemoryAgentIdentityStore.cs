using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Options;

namespace ArchonAI.Identity;

public sealed class InMemoryAgentIdentityStore : IAgentIdentityStore
{
    private readonly IdentityOptions _options;
    private readonly ConcurrentDictionary<Guid, AgentIdentityProfile> _profiles = new();

    public InMemoryAgentIdentityStore(IOptions<IdentityOptions> options)
    {
        _options = options.Value;
    }

    public global::System.Threading.Tasks.Task RegisterOrUpdateAsync(
        Agent agent,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _profiles.AddOrUpdate(
            agent.Id,
            _ => new AgentIdentityProfile(
                AgentId: agent.Id,
                Capabilities: agent.Capabilities.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Permissions: permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                PerformanceMetrics: new AgentPerformanceMetrics(0, 0, 0, 0, 0, DateTimeOffset.MinValue),
                ExecutionHistory: Array.Empty<AgentExecutionHistoryEntry>(),
                UpdatedAtUtc: DateTimeOffset.UtcNow),
            (_, current) => current with
            {
                Capabilities = agent.Capabilities.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Permissions = permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<AgentIdentityProfile?> GetAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _profiles.TryGetValue(agentId, out AgentIdentityProfile? profile);
        return global::System.Threading.Tasks.Task.FromResult(profile);
    }

    public global::System.Threading.Tasks.Task RecordExecutionAsync(
        Guid agentId,
        Guid taskId,
        bool success,
        double executionTimeMs,
        decimal cost,
        string errorType,
        DateTimeOffset executedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _profiles.AddOrUpdate(
            agentId,
            _ => BuildNewProfile(agentId, taskId, success, executionTimeMs, cost, errorType, executedAtUtc),
            (_, current) => UpdateExistingProfile(current, taskId, success, executionTimeMs, cost, errorType, executedAtUtc));

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    private AgentIdentityProfile BuildNewProfile(
        Guid agentId,
        Guid taskId,
        bool success,
        double executionTimeMs,
        decimal cost,
        string errorType,
        DateTimeOffset executedAtUtc)
    {
        var history = new[]
        {
            new AgentExecutionHistoryEntry(taskId, success, executionTimeMs, cost, errorType, executedAtUtc)
        };

        var metrics = new AgentPerformanceMetrics(
            TotalExecutions: 1,
            SuccessfulExecutions: success ? 1 : 0,
            FailedExecutions: success ? 0 : 1,
            AverageExecutionTimeMs: executionTimeMs,
            TotalCost: cost,
            LastExecutionAtUtc: executedAtUtc);

        return new AgentIdentityProfile(
            agentId,
            Array.Empty<string>(),
            Array.Empty<string>(),
            metrics,
            history,
            DateTimeOffset.UtcNow);
    }

    private AgentIdentityProfile UpdateExistingProfile(
        AgentIdentityProfile current,
        Guid taskId,
        bool success,
        double executionTimeMs,
        decimal cost,
        string errorType,
        DateTimeOffset executedAtUtc)
    {
        int total = current.PerformanceMetrics.TotalExecutions + 1;
        int successCount = current.PerformanceMetrics.SuccessfulExecutions + (success ? 1 : 0);
        int failedCount = current.PerformanceMetrics.FailedExecutions + (success ? 0 : 1);
        double avg = ((current.PerformanceMetrics.AverageExecutionTimeMs * current.PerformanceMetrics.TotalExecutions) + executionTimeMs) / total;

        var history = current.ExecutionHistory
            .Append(new AgentExecutionHistoryEntry(taskId, success, executionTimeMs, cost, errorType, executedAtUtc))
            .TakeLast(Math.Max(1, _options.MaxExecutionHistoryEntries))
            .ToArray();

        return current with
        {
            PerformanceMetrics = new AgentPerformanceMetrics(
                TotalExecutions: total,
                SuccessfulExecutions: successCount,
                FailedExecutions: failedCount,
                AverageExecutionTimeMs: avg,
                TotalCost: current.PerformanceMetrics.TotalCost + cost,
                LastExecutionAtUtc: executedAtUtc),
            ExecutionHistory = history,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
