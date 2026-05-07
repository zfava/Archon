using ArchonAI.Core.Models.Perception;

namespace ArchonAI.Core.Interfaces;

public interface ISignalIngestionService
{
    global::System.Threading.Tasks.Task<SignalIngestionResult> IngestApiSignalAsync(
        BusinessSignal signal,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<SignalIngestionResult> IngestEventBusSignalAsync(
        BusinessSignal signal,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<SignalIngestionResult>> IngestScheduledPollSignalsAsync(
        SourceSystem source,
        IReadOnlyList<BusinessSignal> signals,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task StartPollingAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task StopPollingAsync(
        CancellationToken cancellationToken = default);
}
