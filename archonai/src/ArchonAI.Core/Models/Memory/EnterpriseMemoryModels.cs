namespace ArchonAI.Core.Models.Memory;

/// <summary>
/// Six-layer enterprise memory hierarchy. Each layer has distinct retention,
/// access patterns, and behavioral characteristics.
/// </summary>
public enum MemoryLayer
{
    /// <summary>Ephemeral context within a single user/agent session. Auto-expires.</summary>
    Session = 0,

    /// <summary>Active operational state: running workflows, recent actions, task queues.</summary>
    Operational = 1,

    /// <summary>Institutional knowledge: processes, norms, org structure, team capabilities.</summary>
    Organizational = 2,

    /// <summary>Long-horizon goals, strategies, competitive positioning, market intelligence.</summary>
    Strategic = 3,

    /// <summary>Relationship context: customers, vendors, partners, internal teams, interaction history.</summary>
    Relational = 4,

    /// <summary>Financial state: budgets, forecasts, consequence records, revenue/cost patterns.</summary>
    Financial = 5,
}

/// <summary>
/// A memory record classified into the enterprise hierarchy with entity linkage.
/// </summary>
public sealed record EnterpriseMemoryRecord(
    Guid Id,
    Guid TenantId,
    MemoryLayer Layer,
    string Category,
    string Subject,
    string Content,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<MemoryEntityLink> LinkedEntities,
    IReadOnlyList<string> Tags,
    double Importance,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>
/// Links a memory record to a first-class entity in the system.
/// </summary>
public sealed record MemoryEntityLink(
    string EntityType,
    string EntityId,
    string Relationship);

/// <summary>
/// Query result with layer-based faceting.
/// </summary>
public sealed record EnterpriseMemoryQueryResult(
    IReadOnlyList<EnterpriseMemoryRecord> Records,
    int TotalCount,
    IReadOnlyDictionary<string, int> LayerCounts);

/// <summary>
/// Entity-centric memory view: all memory linked to a specific entity.
/// </summary>
public sealed record EntityMemoryView(
    string EntityType,
    string EntityId,
    IReadOnlyList<EnterpriseMemoryRecord> Memories,
    IReadOnlyDictionary<string, int> LayerDistribution);
