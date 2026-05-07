using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Trace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Trace;

/// <summary>
/// File-backed persistent trace store. Maintains a bounded in-memory queue
/// flushed to a JSON file on disk. Loads prior traces on startup.
/// </summary>
public sealed class DurableTraceStore : ITraceStore, IDisposable
{
    private readonly TraceOptions _options;
    private readonly ConcurrentQueue<TraceEntry> _entries = new();
    private readonly string _filePath;
    private readonly ILogger<DurableTraceStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _count;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public DurableTraceStore(IOptions<TraceOptions> options, ILogger<DurableTraceStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _filePath = options.Value.PersistencePath ?? Path.Combine(
            AppContext.BaseDirectory, "data", "trace-entries.json");
        LoadFromDisk();
    }

    public global::System.Threading.Tasks.Task RecordAsync(TraceEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _entries.Enqueue(entry);
        Interlocked.Increment(ref _count);
        int maxEntries = Math.Max(100, _options.MaxEntries);

        while (_count > maxEntries && _entries.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);
        }

        _ = FlushAsync();
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<TraceEntry>> QueryAsync(
        string? scope = null,
        string? category = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int boundedLimit = Math.Clamp(limit, 1, 2000);

        IReadOnlyList<TraceEntry> entries = _entries
            .Where(entry => string.IsNullOrWhiteSpace(scope) || entry.Scope.Equals(scope, StringComparison.OrdinalIgnoreCase))
            .Where(entry => string.IsNullOrWhiteSpace(category) || entry.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.RecordedAtUtc)
            .Take(boundedLimit)
            .ToArray();

        return global::System.Threading.Tasks.Task.FromResult(entries);
    }

    internal async Task FlushAsync()
    {
        if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(5)))
            return;
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var snapshot = _entries.ToArray();
            var json = JsonSerializer.Serialize(snapshot, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush trace state to {Path}", _filePath);
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
                _logger.LogInformation("No trace state file at {Path}, starting fresh", _filePath);
                return;
            }

            var json = File.ReadAllText(_filePath);
            var records = JsonSerializer.Deserialize<List<TraceEntry>>(json, JsonOpts);
            if (records is not null)
            {
                foreach (var record in records)
                    _entries.Enqueue(record);
                _count = records.Count;
                _logger.LogInformation("Loaded {Count} trace entries from {Path}", records.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load trace state from {Path}", _filePath);
        }
    }

    public void Dispose() => _writeLock.Dispose();
}
