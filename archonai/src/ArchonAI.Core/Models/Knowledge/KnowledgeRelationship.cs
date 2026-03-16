namespace ArchonAI.Core.Models.Knowledge;

public sealed record KnowledgeRelationship(
    string RelationshipId,
    string FromNodeId,
    string RelationshipType,
    string ToNodeId,
    IReadOnlyDictionary<string, string> Properties,
    DateTimeOffset UpdatedAtUtc);
