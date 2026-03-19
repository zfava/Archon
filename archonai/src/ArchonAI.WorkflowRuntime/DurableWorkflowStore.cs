using System.Collections.Concurrent;
using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Workflow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.WorkflowRuntime;

/// <summary>
/// File-backed durable workflow execution store.
/// Uses an in-memory ConcurrentDictionary as the hot cache, periodically flushed
/// to a JSON file on disk. On startup, the file is loaded to restore state.
///
/// For production: replace with a database-backed implementation of IWorkflowExecutionStore.
/// </summary>
public sealed class DurableWorkflowStore : IWorkflowExecutionStore, IDisposable
{
    private readonly ConcurrentDictionary<Guid, WorkflowExecutionRecord> _records = new();
    private readonly string _filePath;
    private readonly ILogger<DurableWorkflowStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DurableWorkflowStore(
        IOptions<WorkflowRuntimeOptions> options,
        ILogger<DurableWorkflowStore> logger)
    {
        _logger = logger;
        _filePath = options.Value.PersistencePath ?? Path.Combine(
            AppContext.BaseDirectory, "data", "workflow-executions.json");

        LoadFromDisk();
    }

    public Task<WorkflowExecutionRecord> CreateAsync(WorkflowExecutionRecord record, CancellationToken ct = default)
    {
        _records[record.Id] = record;
        _ = FlushAsync();
        return Task.FromResult(record);
    }

    public Task<WorkflowExecutionRecord?> GetAsync(Guid workflowId, CancellationToken ct = default)
    {
        _records.TryGetValue(workflowId, out var record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<WorkflowExecutionRecord>> ListAsync(
        string? tenantId = null, WorkflowExecutionStatus? status = null,
        int limit = 50, CancellationToken ct = default)
    {
        var query = _records.Values.AsEnumerable();
        if (tenantId is not null)
            query = query.Where(r => r.TenantId == tenantId);
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        IReadOnlyList<WorkflowExecutionRecord> result = query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(limit)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<WorkflowExecutionRecord> UpdateAsync(WorkflowExecutionRecord record, CancellationToken ct = default)
    {
        _records[record.Id] = record;
        _ = FlushAsync();
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<WorkflowExecutionRecord>> GetResumableAsync(CancellationToken ct = default)
    {
        IReadOnlyList<WorkflowExecutionRecord> result = _records.Values
            .Where(r => r.Status is WorkflowExecutionStatus.Running
                or WorkflowExecutionStatus.Queued
                or WorkflowExecutionStatus.Waiting)
            .OrderBy(r => r.CreatedAtUtc)
            .ToList();
        return Task.FromResult(result);
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("No existing workflow state file at {Path}, starting fresh", _filePath);
                return;
            }

            var json = File.ReadAllText(_filePath);
            var records = JsonSerializer.Deserialize<List<WorkflowExecutionRecord>>(json, JsonOpts);
            if (records is not null)
            {
                foreach (var record in records)
                    _records[record.Id] = record;
                _logger.LogInformation("Loaded {Count} workflow records from {Path}", records.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load workflow state from {Path}", _filePath);
        }
    }

    private async Task FlushAsync()
    {
        if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(5)))
            return; // Skip if another flush is in progress

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var records = _records.Values.ToList();
            var json = JsonSerializer.Serialize(records, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush workflow state to {Path}", _filePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Awaits any in-flight flush so callers can guarantee state is persisted.
    /// Useful for graceful shutdown and deterministic testing.
    /// </summary>
    public async Task FlushPendingAsync()
    {
        await _writeLock.WaitAsync();
        _writeLock.Release();
    }

    public void Dispose() => _writeLock.Dispose();
}
