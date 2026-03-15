namespace ArchonAI.Core.Models;
public sealed record Task(Guid Id,Guid ObjectiveId,int Order,string Name,string Description,string RequiredCapability,IReadOnlyDictionary<string,string> Inputs,DateTimeOffset CreatedAtUtc,DateTimeOffset? StartedAtUtc,DateTimeOffset? CompletedAtUtc);
