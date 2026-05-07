namespace ArchonAI.Core.Models.OperationalTwin;

// ══════════════════════════════════════════════════════════════
//  Entity types in the operational twin
// ══════════════════════════════════════════════════════════════

public enum TwinEntityType
{
    Team,
    Function,
    System,
    Integration,
    Workflow,
    Kpi,
    Objective,
}

public enum TwinEntityStatus
{
    Active,
    Degraded,
    Inactive,
    Archived,
}

// ══════════════════════════════════════════════════════════════
//  Core entity — a node in the operational twin graph
// ══════════════════════════════════════════════════════════════

public sealed record TwinEntity(
    Guid Id,
    Guid TenantId,
    TwinEntityType EntityType,
    string Name,
    string? Description,
    TwinEntityStatus Status,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<string> Tags,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Dependency — a directed edge between twin entities
// ══════════════════════════════════════════════════════════════

public enum DependencyType
{
    DependsOn,
    Feeds,
    Owns,
    Monitors,
    Blocks,
}

public sealed record TwinDependency(
    Guid Id,
    Guid TenantId,
    Guid FromEntityId,
    Guid ToEntityId,
    DependencyType Type,
    string? Label,
    double? CriticalityScore,
    DateTimeOffset CreatedAtUtc);

// ══════════════════════════════════════════════════════════════
//  KPI with target tracking
// ══════════════════════════════════════════════════════════════

public enum KpiDirection { HigherIsBetter, LowerIsBetter }

public sealed record TwinKpi(
    Guid EntityId,
    string MetricName,
    double CurrentValue,
    double? TargetValue,
    double? ThresholdWarning,
    double? ThresholdCritical,
    KpiDirection Direction,
    string Unit,
    DateTimeOffset MeasuredAtUtc);

// ══════════════════════════════════════════════════════════════
//  Bottleneck — surfaced operational constraint
// ══════════════════════════════════════════════════════════════

public enum BottleneckSeverity { Low, Medium, High, Critical }

public sealed record TwinBottleneck(
    Guid Id,
    Guid TenantId,
    Guid AffectedEntityId,
    string Description,
    BottleneckSeverity Severity,
    string? RootCause,
    bool IsResolved,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset? ResolvedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Entity link — connects twin entities to platform artifacts
// ══════════════════════════════════════════════════════════════

public sealed record TwinArtifactLink(
    Guid Id,
    Guid TwinEntityId,
    string ArtifactType,
    string ArtifactId,
    string Relationship,
    DateTimeOffset LinkedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Twin overview — aggregated view
// ══════════════════════════════════════════════════════════════

public sealed record TwinOverview(
    Guid TenantId,
    IReadOnlyDictionary<string, int> EntityCounts,
    IReadOnlyList<TwinBottleneck> ActiveBottlenecks,
    IReadOnlyList<TwinKpi> WarningKpis,
    int TotalDependencies,
    DateTimeOffset GeneratedAtUtc);
