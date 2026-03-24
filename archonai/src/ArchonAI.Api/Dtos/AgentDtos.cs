namespace ArchonAI.Api.Dtos;

public sealed record AgentCapabilityInput(string Name, string Description, string Category, string Version);
public sealed record RegisterAgentRequest(string Name, string Description, string Version, IReadOnlyList<AgentCapabilityInput> Capabilities, Dictionary<string, string>? Configuration = null);
public sealed record UpdateAgentCapabilitiesRequest(IReadOnlyList<AgentCapabilityInput> Capabilities);
public sealed record DisableAgentRequest(string Reason);
public sealed record RecordAgentMetricsRequest(long TotalExecutions, long SuccessfulExecutions, long FailedExecutions, double AverageLatencyMs, double P95LatencyMs, double UptimePercent);
public sealed record RegisterTaskTypesRequest(IReadOnlyList<string> TaskTypes);
public sealed record RequestTaskSupportInput(Guid RequestingAgentId, string RequestingAgentName, Guid TaskId, string RequiredCapability, string Reason, Dictionary<string, string>? Context = null, int? TimeoutSeconds = null);
public sealed record ShareKnowledgeInput(Guid SourceAgentId, string SourceAgentName, Guid? TargetAgentId, string Topic, string Content, Dictionary<string, string>? Metadata = null);
public sealed record DelegateTaskInput(Guid DelegatingAgentId, string DelegatingAgentName, Guid TargetAgentId, string TargetAgentName, Guid OriginalTaskId, string RequiredCapability, Dictionary<string, string>? TaskInputs = null, int? TimeoutSeconds = null);
public sealed record CreateCollaborationSessionInput(
    Guid InitiatorAgentId,
    string InitiatorAgentName,
    string Purpose,
    Dictionary<string, string>? InitialContext);
public sealed record JoinCollaborationSessionInput(
    Guid AgentId,
    string AgentName,
    string Role);
public sealed record ShareCollaborationContextInput(
    Guid AgentId,
    Dictionary<string, string> Context);
public sealed record SmartDelegationInput(
    Guid DelegatingAgentId,
    string DelegatingAgentName,
    string RequiredCapability,
    string? PreferredTaskType,
    Dictionary<string, string>? TaskInputs,
    IReadOnlyList<Guid>? FallbackAgentIds,
    int? TimeoutSeconds);
public sealed record AssistanceRequestInput(
    Guid RequestingAgentId,
    string RequestingAgentName,
    string Objective,
    IReadOnlyList<string> RequiredCapabilities,
    Dictionary<string, string>? Context,
    int? MaxResponders,
    int? TimeoutSeconds);
