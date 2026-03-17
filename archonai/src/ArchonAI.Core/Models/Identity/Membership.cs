namespace ArchonAI.Core.Models.Identity;

public sealed record Membership(
    Guid Id,
    Guid UserId,
    Guid OrganizationId,
    string Role,
    DateTimeOffset JoinedAtUtc);
