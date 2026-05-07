namespace ArchonAI.Core.Models.Identity;

public sealed record Organization(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);
