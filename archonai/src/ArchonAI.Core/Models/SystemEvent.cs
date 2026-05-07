namespace ArchonAI.Core.Models;
public sealed record SystemEvent(Guid Id,string EventType,string Source,Guid CorrelationId,IReadOnlyDictionary<string,string> Payload,DateTimeOffset OccurredAtUtc);
