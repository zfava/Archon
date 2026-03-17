namespace ArchonAI.Core.Models.Identity;

public sealed record AuthTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc);
