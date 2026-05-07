namespace ArchonAI.Core.Models.Identity;

public sealed record UserIdentity(
    Guid Id,
    string Email,
    string DisplayName,
    string PasswordHash,
    Guid OrganizationId,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);
