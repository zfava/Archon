using ArchonAI.Core.Models.Collaboration;

namespace ArchonAI.Core.Interfaces;

public interface IAgentCollaborationManager
{
    // ── Session management ──────────────────────────────────────
    global::System.Threading.Tasks.Task<CollaborationSession> CreateSessionAsync(
        Guid initiatorAgentId,
        string initiatorAgentName,
        string purpose,
        IReadOnlyDictionary<string, string>? initialContext,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<CollaborationSession> JoinSessionAsync(
        Guid sessionId,
        Guid agentId,
        string agentName,
        string role,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task ShareContextAsync(
        Guid sessionId,
        Guid agentId,
        IReadOnlyDictionary<string, string> context,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<CollaborationSession> CompleteSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<CollaborationSession?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    // ── Smart delegation ────────────────────────────────────────
    global::System.Threading.Tasks.Task<SmartDelegationResult> DelegateSmartAsync(
        SmartDelegationRequest request,
        CancellationToken cancellationToken = default);

    // ── Assistance fan-out ──────────────────────────────────────
    global::System.Threading.Tasks.Task<AssistanceResult> RequestAssistanceAsync(
        AssistanceRequest request,
        CancellationToken cancellationToken = default);

    // ── Status ──────────────────────────────────────────────────
    CollaborationStatus GetStatus();
}
