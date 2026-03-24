using ArchonAI.Core.Models.Memory;

namespace ArchonAI.Api.Dtos;

public sealed record StoreEnterpriseMemoryRequest(
    string Layer,
    string Subject,
    string Content,
    string? Category,
    Dictionary<string, string>? Metadata,
    IReadOnlyList<MemoryEntityLinkDto>? LinkedEntities,
    IReadOnlyList<string>? Tags,
    double? Importance,
    DateTimeOffset? ExpiresAtUtc);

public sealed record MemoryEntityLinkDto(
    string EntityType,
    string EntityId,
    string Relationship);

public sealed record UpsertTwinEntityRequest(
    Guid? Id, string EntityType, string Name, string? Description,
    string? Status, Dictionary<string, string>? Properties,
    IReadOnlyList<string>? Tags);

public sealed record AddTwinDependencyRequest(
    Guid FromEntityId, Guid ToEntityId, string Type,
    string? Label, double? CriticalityScore);

public sealed record RecordTwinKpiRequest(
    Guid EntityId, string MetricName, double CurrentValue,
    double? TargetValue, double? ThresholdWarning, double? ThresholdCritical,
    string? Direction, string? Unit);

public sealed record ReportBottleneckRequest(
    Guid AffectedEntityId, string Description, string Severity,
    string? RootCause);

public sealed record LinkTwinArtifactRequest(
    string ArtifactType, string ArtifactId, string Relationship);

public sealed record CreateScenarioRequest(
    string Title, string? Description, string Type,
    IReadOnlyList<AssumptionDto>? Assumptions,
    IReadOnlyList<string>? LinkedKpiIds,
    IReadOnlyList<string>? LinkedDecisionIds,
    IReadOnlyList<string>? LinkedEntityIds);

public sealed record AssumptionDto(
    string Name, string CurrentValue, string ProposedValue,
    string? Unit, string? Rationale);

public sealed record UpdateAssumptionsRequest(
    IReadOnlyList<AssumptionDto> Assumptions);

public sealed record CompareScenarioRequest(
    IReadOnlyList<Guid> ScenarioIds);

public sealed record RaiseExceptionRequest(
    string Category, string Severity, string Title,
    string? Description, string? Domain,
    double? Urgency, double? EconomicImpactEstimate,
    double? Confidence, string? EscalationLevel,
    string? AssignedTo, string? EscalationPath,
    IReadOnlyList<ExceptionArtifactLinkDto>? LinkedArtifacts,
    RecommendedActionDto? RecommendedAction);

public sealed record ExceptionArtifactLinkDto(
    string ArtifactType, string ArtifactId, string? Label);

public sealed record RecommendedActionDto(
    string ActionType, string Description,
    string? TargetArtifactType, string? TargetArtifactId,
    string? Confidence);

public sealed record UpdateExceptionStatusRequest(
    string Status, string? AssignedTo);

public sealed record SetRecommendedActionRequest(
    string ActionType, string Description,
    string? TargetArtifactType, string? TargetArtifactId,
    string? Confidence);

public sealed record StartHeroWorkflowRequest(
    string WorkflowType,
    string Title,
    Dictionary<string, string>? Inputs);

public sealed record AdvanceHeroWorkflowRequest(
    Dictionary<string, string>? Inputs);
