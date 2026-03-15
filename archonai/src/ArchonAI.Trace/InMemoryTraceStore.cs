using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Trace;
using Microsoft.Extensions.Options;

namespace ArchonAI.Trace;

public sealed class InMemoryTraceStore : ITraceStore
{
    private readonly TraceOptions _options;
    private readonly ConcurrentQueue<TraceEntry> _entries = new();

    public InMemoryTraceStore(IOptions<TraceOptions> options)
    {
        _options = options.Value;
    }

    public global::System.Threading.Tasks.Task RecordAsync(TraceEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _entries.Enqueue(entry);
        int maxEntries = Math.Max(100, _options.MaxEntries);

        while (_entries.Count > maxEntries && _entries.TryDequeue(out _))
        {
        }

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
}
