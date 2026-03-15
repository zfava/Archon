namespace ArchonAI.Core.Models;
public sealed record Agent(Guid Id,string Name,string Version,IReadOnlyList<AgentCapability> Capabilities,bool IsEnabled,DateTimeOffset RegisteredAtUtc);
