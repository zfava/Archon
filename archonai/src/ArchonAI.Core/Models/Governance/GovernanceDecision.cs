using ArchonAI.Core.Models.Policy;

namespace ArchonAI.Core.Models.Governance;

public sealed record GovernanceDecision(
    bool IsAllowed,
    string Reason,
    IReadOnlyList<string> Violations,
    PolicyDecision PolicyDecision);
