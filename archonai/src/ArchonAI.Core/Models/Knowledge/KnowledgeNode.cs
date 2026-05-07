namespace ArchonAI.Core.Models.Knowledge;

public sealed record KnowledgeNode(
    string NodeId,
    string NodeType,
    string DisplayName,
    IReadOnlyDictionary<string, string> Properties,
    DateTimeOffset UpdatedAtUtc);
