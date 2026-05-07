using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Perception;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Perception;

public sealed class BusinessPerceptionEngine : IBusinessPerceptionEngine
{
    private readonly IEventBus _eventBus;
    private readonly IStrategicPlanner _strategicPlanner;
    private readonly ILogger<BusinessPerceptionEngine> _logger;

    private long _totalSignalsIngested;
    private long _totalObservationsProduced;
    private readonly ConcurrentDictionary<SourceSystem, long> _signalsBySource = new();
    private readonly ConcurrentDictionary<ObservationCategory, long> _observationsByCategory = new();
    private readonly ConcurrentQueue<OperationalObservation> _recentObservations = new();

    private const int MaxRecentObservations = 200;

    public BusinessPerceptionEngine(
        IEventBus eventBus,
        IStrategicPlanner strategicPlanner,
        ILogger<BusinessPerceptionEngine> logger)
    {
        _eventBus = eventBus;
        _strategicPlanner = strategicPlanner;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task<OperationalObservation> ProcessSignalAsync(
        BusinessSignal signal,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalSignalsIngested);
        _signalsBySource.AddOrUpdate(signal.SourceSystem, 1, (_, v) => v + 1);

        var observation = ConvertToObservation(signal);

        _observationsByCategory.AddOrUpdate(observation.Category, 1, (_, v) => v + 1);
        Interlocked.Increment(ref _totalObservationsProduced);

        EnqueueObservation(observation);

        await PublishToEventBusAsync(observation, cancellationToken);

        if (observation.Severity >= ObservationSeverity.High)
        {
            await NotifyStrategicPlannerAsync(observation, cancellationToken);
        }

        _logger.LogDebug(
            "Signal {SignalId} from {Source} → Observation {ObservationId} [{Category}/{Severity}]",
            signal.SignalId, signal.SourceSystem, observation.ObservationId, observation.Category, observation.Severity);

        return observation;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<OperationalObservation>> ProcessBatchAsync(
        IReadOnlyList<BusinessSignal> signals,
        CancellationToken cancellationToken = default)
    {
        var results = new List<OperationalObservation>(signals.Count);

        foreach (var signal in signals)
        {
            var observation = await ProcessSignalAsync(signal, cancellationToken);
            results.Add(observation);
        }

        return results;
    }

    public global::System.Threading.Tasks.Task<PerceptionDashboard> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dashboard = new PerceptionDashboard(
            TotalSignalsIngested: Interlocked.Read(ref _totalSignalsIngested),
            TotalObservationsProduced: Interlocked.Read(ref _totalObservationsProduced),
            SignalsBySource: new Dictionary<SourceSystem, long>(_signalsBySource),
            ObservationsByCategory: new Dictionary<ObservationCategory, long>(_observationsByCategory),
            RecentObservations: _recentObservations.ToArray().TakeLast(50).Reverse().ToList(),
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        return global::System.Threading.Tasks.Task.FromResult(dashboard);
    }

    private OperationalObservation ConvertToObservation(BusinessSignal signal)
    {
        var category = ClassifyCategory(signal);
        var severity = ClassifySeverity(signal);
        var summary = BuildSummary(signal, category);
        var structuredData = ExtractStructuredData(signal);

        return new OperationalObservation(
            ObservationId: Guid.NewGuid(),
            SourceSignalId: signal.SignalId,
            SourceSystem: signal.SourceSystem,
            Category: category,
            Severity: severity,
            EntityId: signal.EntityId,
            Summary: summary,
            StructuredData: structuredData,
            ObservedAtUtc: signal.Timestamp,
            ProcessedAtUtc: DateTimeOffset.UtcNow);
    }

    private static ObservationCategory ClassifyCategory(BusinessSignal signal)
    {
        var payload = signal.Payload;

        if (payload.ContainsKey("eventType"))
        {
            var eventType = payload["eventType"].ToLowerInvariant();
            if (eventType.Contains("order")) return ObservationCategory.OrderLifecycle;
            if (eventType.Contains("payment") || eventType.Contains("invoice")) return ObservationCategory.Revenue;
            if (eventType.Contains("campaign")) return ObservationCategory.CampaignPerformance;
            if (eventType.Contains("shipment") || eventType.Contains("delivery")) return ObservationCategory.SupplyChain;
            if (eventType.Contains("inventory")) return ObservationCategory.InventoryMovement;
            if (eventType.Contains("compliance") || eventType.Contains("audit")) return ObservationCategory.ComplianceEvent;
            if (eventType.Contains("cost") || eventType.Contains("expense")) return ObservationCategory.Cost;
        }

        return signal.SourceSystem switch
        {
            SourceSystem.CRM => ObservationCategory.CustomerActivity,
            SourceSystem.ERP => ObservationCategory.OrderLifecycle,
            SourceSystem.Finance => ObservationCategory.Revenue,
            SourceSystem.Marketing => ObservationCategory.CampaignPerformance,
            SourceSystem.Logistics => ObservationCategory.SupplyChain,
            _ => ObservationCategory.SystemHealth
        };
    }

    private static ObservationSeverity ClassifySeverity(BusinessSignal signal)
    {
        if (signal.Payload.TryGetValue("severity", out var sev))
        {
            return sev.ToLowerInvariant() switch
            {
                "critical" => ObservationSeverity.Critical,
                "high" => ObservationSeverity.High,
                "medium" => ObservationSeverity.Medium,
                "low" => ObservationSeverity.Low,
                _ => ObservationSeverity.Info
            };
        }

        if (signal.Payload.TryGetValue("amount", out var amountStr) &&
            decimal.TryParse(amountStr, out var amount))
        {
            if (amount > 100_000) return ObservationSeverity.High;
            if (amount > 10_000) return ObservationSeverity.Medium;
        }

        if (signal.Payload.ContainsKey("error") || signal.Payload.ContainsKey("failure"))
        {
            return ObservationSeverity.High;
        }

        return ObservationSeverity.Info;
    }

    private static string BuildSummary(BusinessSignal signal, ObservationCategory category)
    {
        var eventType = signal.Payload.GetValueOrDefault("eventType", "event");
        return $"[{signal.SourceSystem}] {category}: {eventType} for entity '{signal.EntityId}'";
    }

    private static IReadOnlyDictionary<string, string> ExtractStructuredData(BusinessSignal signal)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["signalType"] = signal.SignalType.ToString(),
            ["sourceSystem"] = signal.SourceSystem.ToString(),
            ["entityId"] = signal.EntityId,
            ["signalTimestamp"] = signal.Timestamp.ToString("O")
        };

        foreach (var (key, value) in signal.Payload)
        {
            data[$"payload.{key}"] = value;
        }

        return data;
    }

    private async global::System.Threading.Tasks.Task PublishToEventBusAsync(
        OperationalObservation observation,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["observationId"] = observation.ObservationId.ToString(),
            ["sourceSignalId"] = observation.SourceSignalId.ToString(),
            ["sourceSystem"] = observation.SourceSystem.ToString(),
            ["category"] = observation.Category.ToString(),
            ["severity"] = observation.Severity.ToString(),
            ["entityId"] = observation.EntityId,
            ["summary"] = observation.Summary
        };

        var systemEvent = new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: "perception.observation.created",
            Source: $"perception:{observation.SourceSystem}",
            CorrelationId: observation.SourceSignalId,
            Payload: payload,
            OccurredAtUtc: DateTimeOffset.UtcNow);

        try
        {
            await _eventBus.PublishAsync(systemEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish observation {ObservationId} to EventBus", observation.ObservationId);
        }
    }

    private async global::System.Threading.Tasks.Task NotifyStrategicPlannerAsync(
        OperationalObservation observation,
        CancellationToken cancellationToken)
    {
        try
        {
            var constraints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["objectiveType"] = "perception-driven",
                ["source"] = observation.SourceSystem.ToString(),
                ["category"] = observation.Category.ToString(),
                ["severity"] = observation.Severity.ToString(),
                ["entityId"] = observation.EntityId,
                ["observationId"] = observation.ObservationId.ToString()
            };

            var objective = new Objective(
                Id: Guid.NewGuid(),
                Title: $"Respond to {observation.Severity} {observation.Category} observation",
                Description: observation.Summary,
                Constraints: constraints,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                DueAtUtc: observation.Severity == ObservationSeverity.Critical
                    ? DateTimeOffset.UtcNow.AddHours(1)
                    : DateTimeOffset.UtcNow.AddHours(24));

            await _strategicPlanner.BuildWorkflowAsync(objective, cancellationToken);

            _logger.LogInformation(
                "Strategic workflow triggered for {Severity} observation {ObservationId}",
                observation.Severity, observation.ObservationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify StrategicPlanner for observation {ObservationId}", observation.ObservationId);
        }
    }

    private void EnqueueObservation(OperationalObservation observation)
    {
        _recentObservations.Enqueue(observation);

        while (_recentObservations.Count > MaxRecentObservations)
        {
            _recentObservations.TryDequeue(out _);
        }
    }
}
