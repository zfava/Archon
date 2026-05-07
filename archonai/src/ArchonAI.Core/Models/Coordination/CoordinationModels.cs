namespace ArchonAI.Core.Models.Coordination;

public sealed record TaskSupportRequest(
    Guid Id,
    Guid RequestingAgentId,
    string RequestingAgentName,
    Guid TaskId,
    string RequiredCapability,
    string Reason,
    IReadOnlyDictionary<string, string> Context,
    TimeSpan Timeout,
    DateTimeOffset RequestedAtUtc);

public sealed record TaskSupportResponse(
    Guid RequestId,
    Guid RespondingAgentId,
    string RespondingAgentName,
    TaskSupportOutcome Outcome,
    string? Summary,
    IReadOnlyDictionary<string, string> Outputs,
    DateTimeOffset RespondedAtUtc);

public enum TaskSupportOutcome
{
    Accepted,
    Completed,
    Declined,
    TimedOut,
    Failed
}

public sealed record KnowledgeSharePayload(
    Guid Id,
    Guid SourceAgentId,
    string SourceAgentName,
    Guid? TargetAgentId,
    string Topic,
    string Content,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset SharedAtUtc);

public sealed record TaskDelegation(
    Guid Id,
    Guid DelegatingAgentId,
    string DelegatingAgentName,
    Guid TargetAgentId,
    string TargetAgentName,
    Guid OriginalTaskId,
    string RequiredCapability,
    IReadOnlyDictionary<string, string> TaskInputs,
    TimeSpan Timeout,
    DateTimeOffset DelegatedAtUtc);

public sealed record TaskDelegationResult(
    Guid DelegationId,
    Guid TargetAgentId,
    DelegationOutcome Outcome,
    ExecutionResult? Result,
    DateTimeOffset CompletedAtUtc);

public enum DelegationOutcome
{
    Completed,
    Declined,
    TimedOut,
    Failed
}

public sealed record CoordinationStatus(
    long SupportRequestsSent,
    long SupportRequestsReceived,
    long SupportRequestsCompleted,
    long SupportRequestsTimedOut,
    long KnowledgeShareEvents,
    long TaskDelegations,
    long TaskDelegationsCompleted,
    long TaskDelegationsTimedOut,
    int ActiveSupportRequests,
    int ActiveDelegations,
    DateTimeOffset StatusAsOfUtc);

public sealed record CoordinationOptions
{
    public const string SectionName = "AgentCoordination";
    public int DefaultTimeoutSeconds { get; set; } = 60;
    public int MaxConcurrentSupportRequests { get; set; } = 100;
    public int MaxConcurrentDelegations { get; set; } = 50;
    public int SupportRequestHistorySize { get; set; } = 500;
    public int DelegationHistorySize { get; set; } = 500;
}
