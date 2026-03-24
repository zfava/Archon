using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Telemetry;

namespace ArchonAI.Telemetry;

public sealed class InMemoryTaskTelemetryStore : ITaskTelemetryStore
{
    private readonly ConcurrentQueue<TaskExecutionTelemetry> _entries = new();

    public global::System.Threading.Tasks.Task RecordAsync(TaskExecutionTelemetry telemetry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.Enqueue(telemetry);
        while (_entries.Count > 5000 && _entries.TryDequeue(out _))
        {
        }

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<TaskExecutionTelemetry>> QueryByObjectiveAsync(Guid objectiveId, int limit = 200, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<TaskExecutionTelemetry> results = _entries
            .Where(entry => entry.ObjectiveId == objectiveId)
            .OrderByDescending(entry => entry.RecordedAtUtc)
            .Take(Math.Clamp(limit, 1, 2000))
            .ToArray();

        return global::System.Threading.Tasks.Task.FromResult(results);
    }
}
