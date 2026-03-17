using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Strategy;

/// <summary>
/// File-backed persistent strategy store. Seeds from config on first use,
/// persists all strategies to JSON on disk.
/// </summary>
public sealed class DurableStrategyStore : IStrategyStore, IDisposable
{
    private readonly ConcurrentDictionary<Guid, OperationalStrategy> _strategies = new();
    private readonly IMemoryStore _memoryStore;
    private readonly string _filePath;
    private readonly ILogger<DurableStrategyStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public DurableStrategyStore(
        IOptions<StrategyOptions> options,
        IMemoryStore memoryStore,
        ILogger<DurableStrategyStore> logger)
    {
        _memoryStore = memoryStore;
        _logger = logger;
        _filePath = options.Value.PersistencePath ?? Path.Combine(
            AppContext.BaseDirectory, "data", "strategies.json");

        LoadFromDisk();

        // Seed strategies only if no persisted data was loaded
        if (_strategies.IsEmpty)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (StrategySeed seed in options.Value.Seeds)
            {
                var strategy = new OperationalStrategy(
                    Id: Guid.NewGuid(),
                    ObjectiveType: seed.ObjectiveType,
                    WorkflowTemplate: seed.WorkflowTemplate,
                    RecommendedAgents: seed.RecommendedAgents,
                    SuccessMetrics: seed.SuccessMetrics,
                    CreatedAtUtc: now,
                    UpdatedAtUtc: now);
                _strategies[strategy.Id] = strategy;
            }
            if (!_strategies.IsEmpty)
                _ = FlushAsync();
        }
    }

    public async global::System.Threading.Tasks.Task SaveAsync(OperationalStrategy strategy, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = strategy with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = strategy.CreatedAtUtc == default ? DateTimeOffset.UtcNow : strategy.CreatedAtUtc
        };

        _strategies[normalized.Id] = normalized;
        _ = FlushAsync();

        var memoryRecord = new MemoryRecord(
            Id: Guid.NewGuid(),
            MemoryType: "strategy",
            Scope: $"strategy:{normalized.ObjectiveType}",
            Content: normalized.WorkflowTemplate,
            Metadata: new Dictionary<string, string>(normalized.SuccessMetrics)
            {
                ["strategyId"] = normalized.Id.ToString(),
                ["objectiveType"] = normalized.ObjectiveType,
                ["recommendedAgents"] = string.Join(',', normalized.RecommendedAgents)
            },
            CreatedAtUtc: normalized.UpdatedAtUtc,
            ExpiresAtUtc: null);

        await _memoryStore.SaveAsync(memoryRecord, cancellationToken);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<OperationalStrategy>> QueryByObjectiveTypeAsync(
        string objectiveType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<OperationalStrategy> results = _strategies.Values
            .Where(s => s.ObjectiveType.Equals(objectiveType, StringComparison.OrdinalIgnoreCase)
                     || s.ObjectiveType.Equals("default", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => ParseScore(s.SuccessMetrics, "successRate"))
            .ThenByDescending(s => s.UpdatedAtUtc)
            .ToArray();

        return global::System.Threading.Tasks.Task.FromResult(results);
    }

    private static double ParseScore(IReadOnlyDictionary<string, string> metrics, string key) =>
        metrics.TryGetValue(key, out string? value) && double.TryParse(value, out double parsed) ? parsed : 0;

    internal async global::System.Threading.Tasks.Task FlushAsync()
    {
        if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(5)))
            return;
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var snapshot = _strategies.Values.ToList();
            var json = JsonSerializer.Serialize(snapshot, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush strategy state to {Path}", _filePath);
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
                _logger.LogInformation("No strategy state file at {Path}, starting fresh", _filePath);
                return;
            }

            var json = File.ReadAllText(_filePath);
            var records = JsonSerializer.Deserialize<List<OperationalStrategy>>(json, JsonOpts);
            if (records is not null)
            {
                foreach (var r in records)
                    _strategies[r.Id] = r;
                _logger.LogInformation("Loaded {Count} strategies from {Path}", records.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load strategy state from {Path}", _filePath);
        }
    }

    public void Dispose() => _writeLock.Dispose();
}
