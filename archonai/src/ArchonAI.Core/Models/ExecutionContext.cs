namespace ArchonAI.Core.Models;
public sealed record ExecutionContext(Guid CorrelationId,Guid ObjectiveId,Guid TaskId,string TenantId,IReadOnlyDictionary<string,string> Metadata,DateTimeOffset RequestedAtUtc);
