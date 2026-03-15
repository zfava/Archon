namespace ArchonAI.Core.Models;

public sealed record PlanningFeedback(
    string Strategy,
    string Capability,
    bool WasSuccessful,
    string Rationale,
    DateTimeOffset RecordedAtUtc);
