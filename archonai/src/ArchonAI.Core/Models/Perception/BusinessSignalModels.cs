namespace ArchonAI.Core.Models.Perception;

public enum SignalType
{
    ApiCall,
    EventBusMessage,
    ScheduledPoll,
    Webhook,
    StateChange
}

public enum SourceSystem
{
    CRM,
    ERP,
    Finance,
    Marketing,
    Logistics
}

public enum ObservationSeverity
{
    Info,
    Low,
    Medium,
    High,
    Critical
}

public enum ObservationCategory
{
    Revenue,
    Cost,
    CustomerActivity,
    SupplyChain,
    InventoryMovement,
    OrderLifecycle,
    CampaignPerformance,
    ComplianceEvent,
    OperationalAnomaly,
    SystemHealth
}

public sealed record BusinessSignal(
    Guid SignalId,
    SignalType SignalType,
    SourceSystem SourceSystem,
    string EntityId,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string> Payload);

public sealed record OperationalObservation(
    Guid ObservationId,
    Guid SourceSignalId,
    SourceSystem SourceSystem,
    ObservationCategory Category,
    ObservationSeverity Severity,
    string EntityId,
    string Summary,
    IReadOnlyDictionary<string, string> StructuredData,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset ProcessedAtUtc);

public sealed record SignalIngestionResult(
    Guid SignalId,
    bool Accepted,
    string? RejectionReason);

public sealed record PerceptionDashboard(
    long TotalSignalsIngested,
    long TotalObservationsProduced,
    IReadOnlyDictionary<SourceSystem, long> SignalsBySource,
    IReadOnlyDictionary<ObservationCategory, long> ObservationsByCategory,
    IReadOnlyList<OperationalObservation> RecentObservations,
    DateTimeOffset GeneratedAtUtc);
