using ArchonAI.Core.Models.Perception;

namespace ArchonAI.Core.Interfaces;

public interface IBusinessPerceptionEngine
{
    global::System.Threading.Tasks.Task<OperationalObservation> ProcessSignalAsync(
        BusinessSignal signal,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<OperationalObservation>> ProcessBatchAsync(
        IReadOnlyList<BusinessSignal> signals,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<PerceptionDashboard> GetDashboardAsync(
        CancellationToken cancellationToken = default);
}
