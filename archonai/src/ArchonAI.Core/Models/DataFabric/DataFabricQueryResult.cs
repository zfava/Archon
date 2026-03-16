namespace ArchonAI.Core.Models.DataFabric;

public sealed record DataFabricQueryResult(
    bool IsAllowed,
    string Reason,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows,
    IReadOnlyDictionary<string, string> AppliedSchemaMapping,
    DateTimeOffset QueriedAtUtc);
