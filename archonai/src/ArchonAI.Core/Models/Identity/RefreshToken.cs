namespace ArchonAI.Core.Models.Identity;

public sealed record RefreshToken(
    Guid Id,
    Guid UserId,
    string TokenHash,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    bool IsRevoked);
