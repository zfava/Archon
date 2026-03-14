namespace ArchonAI.Core.Models;
public sealed record Objective(Guid Id,string Title,string Description,IReadOnlyDictionary<string,string> Constraints,DateTimeOffset CreatedAtUtc,DateTimeOffset? DueAtUtc);
