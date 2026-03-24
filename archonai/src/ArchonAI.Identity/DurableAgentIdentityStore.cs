using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Identity;

/// <summary>
/// File-backed persistent agent identity store. Persists agent profiles,
/// capabilities, permissions, and execution history to JSON on disk.
/// </summary>
public sealed class DurableAgentIdentityStore : IAgentIdentityStore, IDisposable
{
    private readonly IdentityOptions _options;
    private readonly ConcurrentDictionary<Guid, AgentIdentityProfile> _profiles = new();
    private readonly string _filePath;
    private readonly ILogger<DurableAgentIdentityStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public DurableAgentIdentityStore(
        IOptions<IdentityOptions> options,
        ILogger<DurableAgentIdentityStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _filePath = options.Value.AgentIdentityPersistencePath ?? Path.Combine(
            AppContext.BaseDirectory, "data", "agent-identities.json");
        LoadFromDisk();
    }

    public global::System.Threading.Tasks.Task RegisterOrUpdateAsync(
        Agent agent, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default)
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

        _ = FlushAsync();
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<AgentIdentityProfile?> GetAsync(
        Guid agentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _profiles.TryGetValue(agentId, out AgentIdentityProfile? profile);
        return global::System.Threading.Tasks.Task.FromResult(profile);
    }

    public global::System.Threading.Tasks.Task RecordExecutionAsync(
        Guid agentId, Guid taskId, bool success, double executionTimeMs,
        decimal cost, string errorType, DateTimeOffset executedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _profiles.AddOrUpdate(
            agentId,
            _ => BuildNewProfile(agentId, taskId, success, executionTimeMs, cost, errorType, executedAtUtc),
            (_, current) => UpdateExistingProfile(current, taskId, success, executionTimeMs, cost, errorType, executedAtUtc));

        _ = FlushAsync();
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    private AgentIdentityProfile BuildNewProfile(
        Guid agentId, Guid taskId, bool success, double executionTimeMs,
        decimal cost, string errorType, DateTimeOffset executedAtUtc)
    {
        var history = new[] { new AgentExecutionHistoryEntry(taskId, success, executionTimeMs, cost, errorType, executedAtUtc) };
        var metrics = new AgentPerformanceMetrics(1, success ? 1 : 0, success ? 0 : 1, executionTimeMs, cost, executedAtUtc);
        return new AgentIdentityProfile(agentId, Array.Empty<string>(), Array.Empty<string>(), metrics, history, DateTimeOffset.UtcNow);
    }

    private AgentIdentityProfile UpdateExistingProfile(
        AgentIdentityProfile current, Guid taskId, bool success, double executionTimeMs,
        decimal cost, string errorType, DateTimeOffset executedAtUtc)
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
            PerformanceMetrics = new AgentPerformanceMetrics(total, successCount, failedCount, avg,
                current.PerformanceMetrics.TotalCost + cost, executedAtUtc),
            ExecutionHistory = history,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
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

            var snapshot = _profiles.Values.ToList();
            var json = JsonSerializer.Serialize(snapshot, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush agent identity state to {Path}", _filePath);
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
                _logger.LogInformation("No agent identity state file at {Path}, starting fresh", _filePath);
                return;
            }

            var json = File.ReadAllText(_filePath);
            var records = JsonSerializer.Deserialize<List<AgentIdentityProfile>>(json, JsonOpts);
            if (records is not null)
            {
                foreach (var r in records)
                    _profiles[r.AgentId] = r;
                _logger.LogInformation("Loaded {Count} agent identity profiles from {Path}", records.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load agent identity state from {Path}", _filePath);
        }
    }

    public void Dispose() => _writeLock.Dispose();
}
