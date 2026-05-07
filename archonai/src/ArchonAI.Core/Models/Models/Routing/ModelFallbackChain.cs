namespace ArchonAI.Core.Models.Models.Routing;

public sealed record ModelFallbackChain(
    string Name,
    IReadOnlyList<ModelFallbackEntry> Entries);

public sealed record ModelFallbackEntry(
    string Provider,
    string Model,
    int Priority);
