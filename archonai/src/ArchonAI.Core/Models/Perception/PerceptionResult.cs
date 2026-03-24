using ArchonAI.Core.Models;

namespace ArchonAI.Core.Models.Perception;

public sealed record PerceptionResult(
    Objective NormalizedObjective,
    bool IsValid,
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyDictionary<string, string> ExtractedContext,
    IReadOnlyList<string> RemovedNoiseTokens,
    DateTimeOffset ProcessedAtUtc);
