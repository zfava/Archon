using ArchonAI.Core.Models.Trace;

namespace ArchonAI.Core.Interfaces;

public interface ITraceStore
{
    global::System.Threading.Tasks.Task RecordAsync(TraceEntry entry, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<TraceEntry>> QueryAsync(
        string? scope = null,
        string? category = null,
        int limit = 200,
        CancellationToken cancellationToken = default);
}
