using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Common.Persistence;

/// <summary>
/// Reusable file-backed persistence layer: in-memory ConcurrentDictionary hot cache
/// with async flush to a JSON file on disk. Loads from disk on construction.
///
/// For production: replace with a database-backed store.
/// </summary>
public class FileBackedStore<TKey, TValue> : IDisposable
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _cache = new();
    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    protected static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public FileBackedStore(string filePath, ILogger logger)
    {
        _filePath = filePath;
        _logger = logger;
        LoadFromDisk();
    }

    protected ConcurrentDictionary<TKey, TValue> Cache => _cache;

    public TValue? Get(TKey key)
    {
        _cache.TryGetValue(key, out var value);
        return value;
    }

    public IReadOnlyCollection<TValue> GetAll() => _cache.Values.ToList();

    public void Upsert(TKey key, TValue value)
    {
        _cache[key] = value;
        ScheduleFlush();
    }

    public bool Remove(TKey key)
    {
        bool removed = _cache.TryRemove(key, out _);
        if (removed) ScheduleFlush();
        return removed;
    }

    public int Count => _cache.Count;

    /// <summary>
    /// Fire-and-forget flush. Callers that need durability guarantees should call FlushAsync directly.
    /// </summary>
    protected void ScheduleFlush() => _ = FlushAsync();

    public async Task FlushAsync()
    {
        if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(5)))
            return;

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var snapshot = _cache.Values.ToList();
            var json = JsonSerializer.Serialize(snapshot, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush state to {Path}", _filePath);
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
                _logger.LogInformation("No existing state file at {Path}, starting fresh", _filePath);
                return;
            }

            var json = File.ReadAllText(_filePath);
            var records = JsonSerializer.Deserialize<List<TValue>>(json, JsonOpts);
            if (records is not null)
            {
                foreach (var record in records)
                {
                    var key = ExtractKey(record);
                    if (key is not null)
                        _cache[key] = record;
                }
                _logger.LogInformation("Loaded {Count} records from {Path}", records.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load state from {Path}", _filePath);
        }
    }

    /// <summary>
    /// Override in derived classes to extract the key from a deserialized value.
    /// Default uses the first constructor parameter (assumes Guid Id for records).
    /// </summary>
    protected virtual TKey? ExtractKey(TValue value)
    {
        // Default: try to get an "Id" property
        var idProp = typeof(TValue).GetProperty("Id");
        if (idProp is not null)
        {
            var id = idProp.GetValue(value);
            if (id is TKey typedKey) return typedKey;
        }
        return default;
    }

    public void Dispose()
    {
        _writeLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
