namespace ArchonAI.Core.Models.Identity;

public sealed record InviteToken(
    Guid Id,
    Guid OrganizationId,
    string Email,
    string Role,
    string TokenHash,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    bool IsAccepted);
