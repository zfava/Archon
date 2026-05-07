using ArchonAI.Core.Models;

namespace ArchonAI.Core.Models.Collaboration;

// ══════════════════════════════════════════════════════════════
//  Collaboration session
// ══════════════════════════════════════════════════════════════

public enum CollaborationSessionStatus
{
    Active,
    Completed,
    Failed,
    Expired
}

public sealed record CollaborationSession(
    Guid SessionId,
    string Purpose,
    Guid InitiatorAgentId,
    string InitiatorAgentName,
    IReadOnlyList<CollaborationParticipant> Participants,
    IReadOnlyDictionary<string, string> SharedContext,
    CollaborationSessionStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record CollaborationParticipant(
    Guid AgentId,
    string AgentName,
    string Role,
    DateTimeOffset JoinedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Smart delegation
// ══════════════════════════════════════════════════════════════

public sealed record SmartDelegationRequest(
    Guid RequestId,
    Guid DelegatingAgentId,
    string DelegatingAgentName,
    string RequiredCapability,
    string? PreferredTaskType,
    IReadOnlyDictionary<string, string> TaskInputs,
    IReadOnlyList<Guid>? FallbackAgentIds,
    TimeSpan Timeout,
    DateTimeOffset RequestedAtUtc);

public sealed record SmartDelegationResult(
    Guid RequestId,
    Guid SelectedAgentId,
    string SelectedAgentName,
    string SelectionReason,
    int AttemptCount,
    DelegationExecutionOutcome Outcome,
    ExecutionResult? Result,
    DateTimeOffset CompletedAtUtc);

public enum DelegationExecutionOutcome
{
    Completed,
    Failed,
    AllAgentsExhausted,
    TimedOut
}

// ══════════════════════════════════════════════════════════════
//  Assistance request (fan-out to multiple agents)
// ══════════════════════════════════════════════════════════════

public sealed record AssistanceRequest(
    Guid RequestId,
    Guid RequestingAgentId,
    string RequestingAgentName,
    string Objective,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyDictionary<string, string> Context,
    int MaxResponders,
    TimeSpan Timeout,
    DateTimeOffset RequestedAtUtc);

public sealed record AssistanceResponse(
    Guid AgentId,
    string AgentName,
    string Capability,
    bool IsSuccess,
    string Summary,
    IReadOnlyDictionary<string, string> Outputs,
    double LatencyMs,
    DateTimeOffset RespondedAtUtc);

public sealed record AssistanceResult(
    Guid RequestId,
    IReadOnlyList<AssistanceResponse> Responses,
    int TotalRequested,
    int TotalResponded,
    int TotalSucceeded,
    DateTimeOffset CompletedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Collaboration status
// ══════════════════════════════════════════════════════════════

public sealed record CollaborationStatus(
    int ActiveSessions,
    int CompletedSessions,
    long SmartDelegations,
    long SmartDelegationsSucceeded,
    long SmartDelegationsFailed,
    long AssistanceRequests,
    long AssistanceRequestsCompleted,
    long ContextShareEvents,
    DateTimeOffset StatusAsOfUtc);

public sealed record CollaborationOptions
{
    public const string SectionName = "AgentCollaboration";
    public int DefaultTimeoutSeconds { get; set; } = 120;
    public int MaxConcurrentSessions { get; set; } = 50;
    public int MaxParticipantsPerSession { get; set; } = 10;
    public int MaxConcurrentDelegations { get; set; } = 100;
    public int MaxAssistanceResponders { get; set; } = 5;
    public int SessionExpirationMinutes { get; set; } = 60;
}
