namespace ArchonAI.Api.Dtos;

public sealed record RecordAgentLoadRequest(Guid AgentId, string AgentName, int ActiveTasks, int QueuedTasks, double ExecutionTimeMs, double CpuPercent, double MemoryPercent);
public sealed record RecordModelLatencyRequest(string Provider, string Model, double LatencyMs, bool Success);
public sealed record RegisterClusterNodeRequest(string HostName, string Role, int MaxConcurrentTasks, int MaxAgents, double CpuCores, long MemoryBytes, int GpuSlots, IReadOnlyList<string> Capabilities, Dictionary<string, string>? Labels = null);
public sealed record NodeHeartbeatRequest(int ActiveTasks, int QueuedTasks, int ActiveAgents, double CpuUtilizationPercent, double MemoryUtilizationPercent, int GpuSlotsUsed);
public sealed record SystemPauseRequest(string Reason);
public sealed record RaiseAlertRequest(string Severity, string Component, string Message);
public sealed record IngestSignalRequest(string SignalType, string SourceSystem, string EntityId, DateTimeOffset? Timestamp, Dictionary<string, string> Payload);
public sealed record IngestSignalBatchRequest(IReadOnlyList<IngestSignalRequest> Signals);
public sealed record OrgMemorySearchRequest(string QueryText, string? FilterType = null, string? FilterCategory = null, int? TopK = null);
public sealed record CompressMemoryRequest(string Scope);
public sealed record DeduplicateMemoryRequest(string Scope);
public sealed record ClusterMemoryRequest(string Scope);
public sealed record SummarizeMemoryRequest(string Scope, IReadOnlyList<Guid> SourceRecordIds);
public sealed record SearchMemoryRequest(string Scope, IReadOnlyList<float> QueryEmbedding, int? TopK = null);
public sealed record RebuildIndexRequest(string Scope);
