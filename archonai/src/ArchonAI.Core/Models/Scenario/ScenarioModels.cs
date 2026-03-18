namespace ArchonAI.Core.Models.Scenario;

// ══════════════════════════════════════════════════════════════
//  Scenario types — what kind of planning exercise
// ══════════════════════════════════════════════════════════════

public enum ScenarioType
{
    WhatIf,
    CostReduction,
    GrowthPlanning,
    RiskMitigation,
    ResourceReallocation,
    ProcessChange,
    StrategicPivot,
}

public enum ScenarioStatus
{
    Draft,
    Active,
    Compared,
    Archived,
}

// ══════════════════════════════════════════════════════════════
//  Input assumption — a single parameterized change
// ══════════════════════════════════════════════════════════════

public sealed record ScenarioAssumption(
    string Name,
    string CurrentValue,
    string ProposedValue,
    string? Unit,
    string? Rationale);

// ══════════════════════════════════════════════════════════════
//  Projected effect — structured expected outcome
// ══════════════════════════════════════════════════════════════

public sealed record ProjectedEffect(
    string Area,
    string Metric,
    double? BaselineValue,
    double? ProjectedValue,
    string? Unit,
    string Direction,
    string Confidence);

// ══════════════════════════════════════════════════════════════
//  Artifact reference — links scenario to platform objects
// ══════════════════════════════════════════════════════════════

public sealed record ScenarioLink(
    string ArtifactType,
    string ArtifactId,
    string? Label);

// ══════════════════════════════════════════════════════════════
//  Core scenario record
// ══════════════════════════════════════════════════════════════

public sealed record Scenario(
    Guid Id,
    Guid TenantId,
    string Title,
    string? Description,
    ScenarioType Type,
    ScenarioStatus Status,
    IReadOnlyList<ScenarioAssumption> Assumptions,
    IReadOnlyList<ProjectedEffect> ProjectedEffects,
    IReadOnlyList<ScenarioLink> LinkedKpis,
    IReadOnlyList<ScenarioLink> LinkedDecisions,
    IReadOnlyList<ScenarioLink> LinkedEntities,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Scenario comparison result
// ══════════════════════════════════════════════════════════════

public sealed record ScenarioComparisonAxis(
    string Metric,
    string? Unit,
    IReadOnlyDictionary<Guid, double?> ValuesByScenarioId);

public sealed record ScenarioComparison(
    IReadOnlyList<Guid> ScenarioIds,
    IReadOnlyList<ScenarioComparisonAxis> Axes,
    DateTimeOffset GeneratedAtUtc);
