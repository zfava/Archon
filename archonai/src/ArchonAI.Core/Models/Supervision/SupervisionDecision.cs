namespace ArchonAI.Core.Models.Supervision;

public sealed record SupervisionDecision(
    bool IsAllowed,
    string Reason,
    string Action,
    IReadOnlyList<string> Signals);
