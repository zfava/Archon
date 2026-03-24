namespace ArchonAI.Core.Models;
public sealed record MemoryRecord(Guid Id,string MemoryType,string Scope,string Content,IReadOnlyDictionary<string,string> Metadata,DateTimeOffset CreatedAtUtc,DateTimeOffset? ExpiresAtUtc);
