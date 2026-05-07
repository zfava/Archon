using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Perception;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Perception;

public sealed class SignalIngestionService : ISignalIngestionService
{
    private readonly IBusinessPerceptionEngine _perceptionEngine;
    private readonly ILogger<SignalIngestionService> _logger;
    private readonly PerceptionOptions _options;

    private readonly ConcurrentDictionary<SourceSystem, DateTimeOffset> _lastPollTimes = new();
    private volatile bool _pollingActive;
    private CancellationTokenSource? _pollingCts;

    public SignalIngestionService(
        IBusinessPerceptionEngine perceptionEngine,
        IOptions<PerceptionOptions> options,
        ILogger<SignalIngestionService> logger)
    {
        _perceptionEngine = perceptionEngine;
        _options = options.Value;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task<SignalIngestionResult> IngestApiSignalAsync(
        BusinessSignal signal,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateSignal(signal);
        if (validationError is not null)
        {
            return new SignalIngestionResult(signal.SignalId, false, validationError);
        }

        var enriched = signal with { SignalType = SignalType.ApiCall };

        try
        {
            await _perceptionEngine.ProcessSignalAsync(enriched, cancellationToken);
            _logger.LogDebug("API signal {SignalId} from {Source} ingested", signal.SignalId, signal.SourceSystem);
            return new SignalIngestionResult(signal.SignalId, true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process API signal {SignalId}", signal.SignalId);
            return new SignalIngestionResult(signal.SignalId, false, $"Processing failed: {ex.Message}");
        }
    }

    public async global::System.Threading.Tasks.Task<SignalIngestionResult> IngestEventBusSignalAsync(
        BusinessSignal signal,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateSignal(signal);
        if (validationError is not null)
        {
            return new SignalIngestionResult(signal.SignalId, false, validationError);
        }

        var enriched = signal with { SignalType = SignalType.EventBusMessage };

        try
        {
            await _perceptionEngine.ProcessSignalAsync(enriched, cancellationToken);
            _logger.LogDebug("EventBus signal {SignalId} from {Source} ingested", signal.SignalId, signal.SourceSystem);
            return new SignalIngestionResult(signal.SignalId, true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process EventBus signal {SignalId}", signal.SignalId);
            return new SignalIngestionResult(signal.SignalId, false, $"Processing failed: {ex.Message}");
        }
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<SignalIngestionResult>> IngestScheduledPollSignalsAsync(
        SourceSystem source,
        IReadOnlyList<BusinessSignal> signals,
        CancellationToken cancellationToken = default)
    {
        _lastPollTimes[source] = DateTimeOffset.UtcNow;

        var results = new List<SignalIngestionResult>(signals.Count);

        foreach (var signal in signals)
        {
            var validationError = ValidateSignal(signal);
            if (validationError is not null)
            {
                results.Add(new SignalIngestionResult(signal.SignalId, false, validationError));
                continue;
            }

            var enriched = signal with { SignalType = SignalType.ScheduledPoll };

            try
            {
                await _perceptionEngine.ProcessSignalAsync(enriched, cancellationToken);
                results.Add(new SignalIngestionResult(signal.SignalId, true, null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process poll signal {SignalId}", signal.SignalId);
                results.Add(new SignalIngestionResult(signal.SignalId, false, $"Processing failed: {ex.Message}"));
            }
        }

        _logger.LogInformation(
            "Polled {Source}: {Total} signals, {Accepted} accepted, {Rejected} rejected",
            source, signals.Count, results.Count(r => r.Accepted), results.Count(r => !r.Accepted));

        return results;
    }

    public global::System.Threading.Tasks.Task StartPollingAsync(CancellationToken cancellationToken = default)
    {
        if (_pollingActive) return global::System.Threading.Tasks.Task.CompletedTask;

        _pollingActive = true;
        _pollingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _logger.LogInformation("Signal polling started with interval {Interval}s", _options.PollIntervalSeconds);

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task StopPollingAsync(CancellationToken cancellationToken = default)
    {
        _pollingActive = false;
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
        _logger.LogInformation("Signal polling stopped");

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    private string? ValidateSignal(BusinessSignal signal)
    {
        if (signal.SignalId == Guid.Empty)
            return "SignalId is required.";

        if (string.IsNullOrWhiteSpace(signal.EntityId))
            return "EntityId is required.";

        if (signal.Timestamp == default)
            return "Timestamp is required.";

        if (signal.Payload is null || signal.Payload.Count == 0)
            return "Payload must contain at least one entry.";

        if (!_options.EnabledSources.Contains(signal.SourceSystem))
            return $"Source system '{signal.SourceSystem}' is not enabled.";

        return null;
    }
}
