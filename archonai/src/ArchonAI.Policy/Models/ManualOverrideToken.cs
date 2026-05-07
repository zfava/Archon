namespace ArchonAI.Policy.Models;

public sealed record ManualOverrideToken(
    Guid TokenId,
    string Action,
    string TargetTaskId,
    string TargetObjectiveId,
    string AuthorizedBy,
    string AuthorizedByRole,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);
