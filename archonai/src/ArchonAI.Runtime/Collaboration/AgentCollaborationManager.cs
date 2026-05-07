using System.Collections.Concurrent;
using System.Diagnostics;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Collaboration;
using ArchonAI.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreTask = ArchonAI.Core.Models.Task;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;

namespace ArchonAI.Runtime.Collaboration;

/// <summary>
/// Manages cooperative agent execution: sessions with shared context,
/// smart delegation with fallback chains, and fan-out assistance requests.
/// All operations are concurrency-safe via per-session locks and bounded semaphores.
/// </summary>
public sealed class AgentCollaborationManager : IAgentCollaborationManager
{
    private readonly IEnumerable<IAgent> _agentImplementations;
    private readonly IAgentCapabilityRegistry _capabilityRegistry;
    private readonly IEventBus _eventBus;
    private readonly IMemoryStore _memoryStore;
    private readonly CollaborationOptions _options;
    private readonly ILogger<AgentCollaborationManager> _logger;

    // Active sessions keyed by SessionId
    private readonly ConcurrentDictionary<Guid, MutableSession> _sessions = new();

    // Per-session locks for atomic mutations
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _sessionLocks = new();

    // Bounded concurrency for delegation and assistance fan-out
    private readonly SemaphoreSlim _delegationThrottle;
    private readonly SemaphoreSlim _assistanceThrottle;

    // Metrics
    private long _completedSessions;
    private long _smartDelegations;
    private long _smartDelegationsSucceeded;
    private long _smartDelegationsFailed;
    private long _assistanceRequests;
    private long _assistanceRequestsCompleted;
    private long _contextShareEvents;

    public AgentCollaborationManager(
        IEnumerable<IAgent> agentImplementations,
        IAgentCapabilityRegistry capabilityRegistry,
        IEventBus eventBus,
        IMemoryStore memoryStore,
        IOptions<CollaborationOptions> options,
        ILogger<AgentCollaborationManager> logger)
    {
        _agentImplementations = agentImplementations;
        _capabilityRegistry = capabilityRegistry;
        _eventBus = eventBus;
        _memoryStore = memoryStore;
        _options = options.Value;
        _logger = logger;

        _delegationThrottle = new SemaphoreSlim(_options.MaxConcurrentDelegations);
        _assistanceThrottle = new SemaphoreSlim(_options.MaxAssistanceResponders * 2);
    }

    // ══════════════════════════════════════════════════════════════
    //  Session management
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<CollaborationSession> CreateSessionAsync(
        Guid initiatorAgentId,
        string initiatorAgentName,
        string purpose,
        IReadOnlyDictionary<string, string>? initialContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_sessions.Count >= _options.MaxConcurrentSessions)
        {
            // Expire old sessions before rejecting
            ExpireStaleSessionsUnsafe();
            if (_sessions.Count >= _options.MaxConcurrentSessions)
                throw new InvalidOperationException(
                    $"Maximum concurrent collaboration sessions ({_options.MaxConcurrentSessions}) reached.");
        }

        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var mutable = new MutableSession
        {
            SessionId = sessionId,
            Purpose = purpose,
            InitiatorAgentId = initiatorAgentId,
            InitiatorAgentName = initiatorAgentName,
            Status = CollaborationSessionStatus.Active,
            CreatedAtUtc = now,
            Participants = { new CollaborationParticipant(initiatorAgentId, initiatorAgentName, "initiator", now) },
            SharedContext = new ConcurrentDictionary<string, string>(
                initialContext ?? new Dictionary<string, string>())
        };

        _sessions[sessionId] = mutable;
        _sessionLocks[sessionId] = new SemaphoreSlim(1, 1);

        await EmitEventAsync("collaboration.session.created", sessionId, new Dictionary<string, string>
        {
            ["sessionId"] = sessionId.ToString(),
            ["initiatorAgentId"] = initiatorAgentId.ToString(),
            ["initiatorAgentName"] = initiatorAgentName,
            ["purpose"] = purpose
        }, cancellationToken);

        _logger.LogInformation(
            "Collaboration session {SessionId} created by {Agent} for '{Purpose}'",
            sessionId, initiatorAgentName, purpose);

        return mutable.ToSnapshot();
    }

    public async global::System.Threading.Tasks.Task<CollaborationSession> JoinSessionAsync(
        Guid sessionId,
        Guid agentId,
        string agentName,
        string role,
        CancellationToken cancellationToken = default)
    {
        var (session, sessionLock) = GetSessionOrThrow(sessionId);

        await sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (session.Status != CollaborationSessionStatus.Active)
                throw new InvalidOperationException($"Session {sessionId} is not active (status: {session.Status}).");

            if (session.Participants.Count >= _options.MaxParticipantsPerSession)
                throw new InvalidOperationException(
                    $"Session {sessionId} has reached max participants ({_options.MaxParticipantsPerSession}).");

            if (session.Participants.Any(p => p.AgentId == agentId))
                return session.ToSnapshot(); // already joined

            session.Participants.Add(new CollaborationParticipant(agentId, agentName, role, DateTimeOffset.UtcNow));
        }
        finally
        {
            sessionLock.Release();
        }

        await EmitEventAsync("collaboration.session.joined", sessionId, new Dictionary<string, string>
        {
            ["sessionId"] = sessionId.ToString(),
            ["agentId"] = agentId.ToString(),
            ["agentName"] = agentName,
            ["role"] = role
        }, cancellationToken);

        _logger.LogInformation("Agent {AgentName} joined session {SessionId} as {Role}", agentName, sessionId, role);

        return session.ToSnapshot();
    }

    public async global::System.Threading.Tasks.Task ShareContextAsync(
        Guid sessionId,
        Guid agentId,
        IReadOnlyDictionary<string, string> context,
        CancellationToken cancellationToken = default)
    {
        var (session, sessionLock) = GetSessionOrThrow(sessionId);

        await sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (session.Status != CollaborationSessionStatus.Active)
                throw new InvalidOperationException($"Session {sessionId} is not active.");

            if (!session.Participants.Any(p => p.AgentId == agentId))
                throw new InvalidOperationException($"Agent {agentId} is not a participant of session {sessionId}.");

            foreach (var (key, value) in context)
                session.SharedContext[key] = value;
        }
        finally
        {
            sessionLock.Release();
        }

        Interlocked.Increment(ref _contextShareEvents);

        // Persist to memory store for durability
        await _memoryStore.SaveAsync(new MemoryRecord(
            Id: Guid.NewGuid(),
            MemoryType: "collaboration-context",
            Scope: $"session:{sessionId}",
            Content: string.Join("; ", context.Select(kv => $"{kv.Key}={kv.Value}")),
            Metadata: new Dictionary<string, string>
            {
                ["sessionId"] = sessionId.ToString(),
                ["agentId"] = agentId.ToString(),
                ["keyCount"] = context.Count.ToString()
            },
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: null), cancellationToken);

        await EmitEventAsync("collaboration.context.shared", sessionId, new Dictionary<string, string>
        {
            ["sessionId"] = sessionId.ToString(),
            ["agentId"] = agentId.ToString(),
            ["keysShared"] = string.Join(",", context.Keys)
        }, cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<CollaborationSession> CompleteSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var (session, sessionLock) = GetSessionOrThrow(sessionId);

        await sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (session.Status != CollaborationSessionStatus.Active)
                return session.ToSnapshot();

            session.Status = CollaborationSessionStatus.Completed;
            session.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            sessionLock.Release();
        }

        Interlocked.Increment(ref _completedSessions);

        await EmitEventAsync("collaboration.session.completed", sessionId, new Dictionary<string, string>
        {
            ["sessionId"] = sessionId.ToString(),
            ["participants"] = session.Participants.Count.ToString(),
            ["contextKeys"] = session.SharedContext.Count.ToString()
        }, cancellationToken);

        _logger.LogInformation("Collaboration session {SessionId} completed with {Participants} participants",
            sessionId, session.Participants.Count);

        return session.ToSnapshot();
    }

    public global::System.Threading.Tasks.Task<CollaborationSession?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessions.TryGetValue(sessionId, out var session);
        return global::System.Threading.Tasks.Task.FromResult(session?.ToSnapshot());
    }

    // ══════════════════════════════════════════════════════════════
    //  Smart delegation with fallback chain
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<SmartDelegationResult> DelegateSmartAsync(
        SmartDelegationRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("collaboration.delegation.smart");
        activity?.SetTag("delegating.agent.id", request.DelegatingAgentId.ToString());
        activity?.SetTag("required.capability", request.RequiredCapability);

        Interlocked.Increment(ref _smartDelegations);

        await _delegationThrottle.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteSmartDelegationAsync(request, cancellationToken);
        }
        finally
        {
            _delegationThrottle.Release();
        }
    }

    private async global::System.Threading.Tasks.Task<SmartDelegationResult> ExecuteSmartDelegationAsync(
        SmartDelegationRequest request,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = request.Timeout > TimeSpan.Zero
            ? request.Timeout
            : TimeSpan.FromSeconds(_options.DefaultTimeoutSeconds);
        timeoutCts.CancelAfter(timeout);

        // Build ordered candidate list: registry-scored first, then fallback IDs
        var candidates = await BuildCandidateListAsync(request, cancellationToken);

        if (candidates.Count == 0)
        {
            Interlocked.Increment(ref _smartDelegationsFailed);
            return new SmartDelegationResult(
                RequestId: request.RequestId,
                SelectedAgentId: Guid.Empty,
                SelectedAgentName: string.Empty,
                SelectionReason: $"No agents found for capability '{request.RequiredCapability}'",
                AttemptCount: 0,
                Outcome: DelegationExecutionOutcome.AllAgentsExhausted,
                Result: null,
                CompletedAtUtc: DateTimeOffset.UtcNow);
        }

        int attempt = 0;
        foreach (var (agentImpl, profile, reason) in candidates)
        {
            attempt++;
            try
            {
                var result = await ExecuteOnAgentAsync(
                    agentImpl, request.RequiredCapability, request.TaskInputs,
                    request.DelegatingAgentId, timeoutCts.Token);

                if (result.IsSuccess)
                {
                    Interlocked.Increment(ref _smartDelegationsSucceeded);

                    await EmitEventAsync("collaboration.delegation.success", request.RequestId, new Dictionary<string, string>
                    {
                        ["requestId"] = request.RequestId.ToString(),
                        ["selectedAgentId"] = profile.AgentId.ToString(),
                        ["selectedAgentName"] = profile.AgentName,
                        ["attempt"] = attempt.ToString()
                    }, cancellationToken);

                    return new SmartDelegationResult(
                        RequestId: request.RequestId,
                        SelectedAgentId: profile.AgentId,
                        SelectedAgentName: profile.AgentName,
                        SelectionReason: reason,
                        AttemptCount: attempt,
                        Outcome: DelegationExecutionOutcome.Completed,
                        Result: result,
                        CompletedAtUtc: DateTimeOffset.UtcNow);
                }

                _logger.LogWarning(
                    "Smart delegation attempt {Attempt} failed on agent {AgentName}: {Summary}",
                    attempt, profile.AgentName, result.Summary);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Smart delegation timed out on attempt {Attempt} for agent {AgentName}",
                    attempt, profile.AgentName);

                Interlocked.Increment(ref _smartDelegationsFailed);
                return new SmartDelegationResult(
                    RequestId: request.RequestId,
                    SelectedAgentId: profile.AgentId,
                    SelectedAgentName: profile.AgentName,
                    SelectionReason: "timed out",
                    AttemptCount: attempt,
                    Outcome: DelegationExecutionOutcome.TimedOut,
                    Result: null,
                    CompletedAtUtc: DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Smart delegation attempt {Attempt} threw on agent {AgentName}",
                    attempt, profile.AgentName);
            }
        }

        Interlocked.Increment(ref _smartDelegationsFailed);

        await EmitEventAsync("collaboration.delegation.exhausted", request.RequestId, new Dictionary<string, string>
        {
            ["requestId"] = request.RequestId.ToString(),
            ["capability"] = request.RequiredCapability,
            ["totalAttempts"] = attempt.ToString()
        }, cancellationToken);

        return new SmartDelegationResult(
            RequestId: request.RequestId,
            SelectedAgentId: Guid.Empty,
            SelectedAgentName: string.Empty,
            SelectionReason: $"All {attempt} candidate agents exhausted",
            AttemptCount: attempt,
            Outcome: DelegationExecutionOutcome.AllAgentsExhausted,
            Result: null,
            CompletedAtUtc: DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════════════
    //  Assistance fan-out
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<AssistanceResult> RequestAssistanceAsync(
        AssistanceRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("collaboration.assistance.fanout");
        activity?.SetTag("requesting.agent.id", request.RequestingAgentId.ToString());
        activity?.SetTag("capabilities.count", request.RequiredCapabilities.Count);

        Interlocked.Increment(ref _assistanceRequests);

        var timeout = request.Timeout > TimeSpan.Zero
            ? request.Timeout
            : TimeSpan.FromSeconds(_options.DefaultTimeoutSeconds);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        int maxResponders = Math.Min(request.MaxResponders, _options.MaxAssistanceResponders);
        var responses = new ConcurrentBag<AssistanceResponse>();

        // Fan out: find best agent for each capability, run in parallel
        var tasks = new List<global::System.Threading.Tasks.Task>();

        foreach (var capability in request.RequiredCapabilities)
        {
            if (responses.Count >= maxResponders) break;

            tasks.Add(global::System.Threading.Tasks.Task.Run(async () =>
            {
                await _assistanceThrottle.WaitAsync(timeoutCts.Token);
                try
                {
                    var selection = await _capabilityRegistry.SelectBestAgentAsync(
                        capability, taskType: null, timeoutCts.Token);

                    if (selection is null)
                    {
                        responses.Add(new AssistanceResponse(
                            AgentId: Guid.Empty,
                            AgentName: "none",
                            Capability: capability,
                            IsSuccess: false,
                            Summary: $"No agent found for capability '{capability}'",
                            Outputs: new Dictionary<string, string>(),
                            LatencyMs: 0,
                            RespondedAtUtc: DateTimeOffset.UtcNow));
                        return;
                    }

                    var agentImpl = _agentImplementations.FirstOrDefault(a => a.Describe().Id == selection.AgentId);
                    if (agentImpl is null)
                    {
                        responses.Add(new AssistanceResponse(
                            AgentId: selection.AgentId,
                            AgentName: selection.AgentName,
                            Capability: capability,
                            IsSuccess: false,
                            Summary: "Agent implementation not found in runtime",
                            Outputs: new Dictionary<string, string>(),
                            LatencyMs: 0,
                            RespondedAtUtc: DateTimeOffset.UtcNow));
                        return;
                    }

                    var sw = Stopwatch.StartNew();
                    var result = await ExecuteOnAgentAsync(
                        agentImpl, capability, request.Context,
                        request.RequestingAgentId, timeoutCts.Token);
                    sw.Stop();

                    responses.Add(new AssistanceResponse(
                        AgentId: selection.AgentId,
                        AgentName: selection.AgentName,
                        Capability: capability,
                        IsSuccess: result.IsSuccess,
                        Summary: result.Summary,
                        Outputs: result.Outputs,
                        LatencyMs: sw.Elapsed.TotalMilliseconds,
                        RespondedAtUtc: DateTimeOffset.UtcNow));
                }
                catch (OperationCanceledException)
                {
                    // Timeout — do not add a response
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Assistance fan-out failed for capability '{Capability}'", capability);
                    responses.Add(new AssistanceResponse(
                        AgentId: Guid.Empty,
                        AgentName: "error",
                        Capability: capability,
                        IsSuccess: false,
                        Summary: ex.Message,
                        Outputs: new Dictionary<string, string>(),
                        LatencyMs: 0,
                        RespondedAtUtc: DateTimeOffset.UtcNow));
                }
                finally
                {
                    _assistanceThrottle.Release();
                }
            }, timeoutCts.Token));
        }

        try
        {
            await global::System.Threading.Tasks.Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Assistance request {RequestId} partially timed out", request.RequestId);
        }

        Interlocked.Increment(ref _assistanceRequestsCompleted);

        var responseList = responses.ToList();

        await EmitEventAsync("collaboration.assistance.completed", request.RequestId, new Dictionary<string, string>
        {
            ["requestId"] = request.RequestId.ToString(),
            ["totalRequested"] = request.RequiredCapabilities.Count.ToString(),
            ["totalResponded"] = responseList.Count.ToString(),
            ["totalSucceeded"] = responseList.Count(r => r.IsSuccess).ToString()
        }, cancellationToken);

        return new AssistanceResult(
            RequestId: request.RequestId,
            Responses: responseList,
            TotalRequested: request.RequiredCapabilities.Count,
            TotalResponded: responseList.Count,
            TotalSucceeded: responseList.Count(r => r.IsSuccess),
            CompletedAtUtc: DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════════════
    //  Status
    // ══════════════════════════════════════════════════════════════

    public CollaborationStatus GetStatus()
    {
        return new CollaborationStatus(
            ActiveSessions: _sessions.Values.Count(s => s.Status == CollaborationSessionStatus.Active),
            CompletedSessions: (int)Interlocked.Read(ref _completedSessions),
            SmartDelegations: Interlocked.Read(ref _smartDelegations),
            SmartDelegationsSucceeded: Interlocked.Read(ref _smartDelegationsSucceeded),
            SmartDelegationsFailed: Interlocked.Read(ref _smartDelegationsFailed),
            AssistanceRequests: Interlocked.Read(ref _assistanceRequests),
            AssistanceRequestsCompleted: Interlocked.Read(ref _assistanceRequestsCompleted),
            ContextShareEvents: Interlocked.Read(ref _contextShareEvents),
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════════════
    //  Internals
    // ══════════════════════════════════════════════════════════════

    private async global::System.Threading.Tasks.Task<List<(IAgent Impl, AgentCapabilityProfile Profile, string Reason)>> BuildCandidateListAsync(
        SmartDelegationRequest request,
        CancellationToken cancellationToken)
    {
        var candidates = new List<(IAgent Impl, AgentCapabilityProfile Profile, string Reason)>();
        var seenIds = new HashSet<Guid>();

        // 1. Registry-scored best agent
        var selection = await _capabilityRegistry.SelectBestAgentAsync(
            request.RequiredCapability, request.PreferredTaskType, cancellationToken);

        if (selection is not null && seenIds.Add(selection.AgentId))
        {
            var impl = _agentImplementations.FirstOrDefault(a => a.Describe().Id == selection.AgentId);
            if (impl is not null)
            {
                var profile = await _capabilityRegistry.GetAgentAsync(selection.AgentId, cancellationToken);
                if (profile is not null)
                    candidates.Add((impl, profile, selection.SelectionReason));
            }
        }

        // 2. All agents matching the capability, ordered by registry score
        var allMatching = await _capabilityRegistry.QueryByCapabilityAsync(
            request.RequiredCapability, cancellationToken);

        foreach (var profile in allMatching)
        {
            if (profile.AgentId == request.DelegatingAgentId) continue;
            if (!seenIds.Add(profile.AgentId)) continue;

            var impl = _agentImplementations.FirstOrDefault(a => a.Describe().Id == profile.AgentId);
            if (impl is not null)
                candidates.Add((impl, profile, "capability match (ranked by performance)"));
        }

        // 3. Explicit fallback agent IDs
        if (request.FallbackAgentIds is not null)
        {
            foreach (var fallbackId in request.FallbackAgentIds)
            {
                if (!seenIds.Add(fallbackId)) continue;

                var profile = await _capabilityRegistry.GetAgentAsync(fallbackId, cancellationToken);
                var impl = _agentImplementations.FirstOrDefault(a => a.Describe().Id == fallbackId);
                if (impl is not null && profile is not null)
                    candidates.Add((impl, profile, "explicit fallback"));
            }
        }

        return candidates;
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteOnAgentAsync(
        IAgent agent,
        string capability,
        IReadOnlyDictionary<string, string> inputs,
        Guid correlationAgentId,
        CancellationToken cancellationToken)
    {
        var task = new CoreTask(
            Id: Guid.NewGuid(),
            ObjectiveId: correlationAgentId,
            Order: 0,
            Name: $"Collaboration: {capability}",
            Description: $"Collaborative execution for capability '{capability}'",
            RequiredCapability: capability,
            Inputs: inputs,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            StartedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: null);

        var context = new CoreExecutionContext(
            CorrelationId: Guid.NewGuid(),
            ObjectiveId: correlationAgentId,
            TaskId: task.Id,
            TenantId: "collaboration",
            Metadata: new Dictionary<string, string>
            {
                ["collaborationType"] = "smart-delegation",
                ["correlationAgentId"] = correlationAgentId.ToString()
            },
            RequestedAtUtc: DateTimeOffset.UtcNow);

        return await agent.ExecuteAsync(task, context, cancellationToken);
    }

    private (MutableSession Session, SemaphoreSlim Lock) GetSessionOrThrow(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            throw new KeyNotFoundException($"Collaboration session '{sessionId}' not found.");

        if (!_sessionLocks.TryGetValue(sessionId, out var sessionLock))
            throw new InvalidOperationException($"Session lock missing for '{sessionId}'.");

        return (session, sessionLock);
    }

    private void ExpireStaleSessionsUnsafe()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_options.SessionExpirationMinutes);
        var stale = _sessions
            .Where(kv => kv.Value.Status == CollaborationSessionStatus.Active && kv.Value.CreatedAtUtc < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in stale)
        {
            if (_sessions.TryGetValue(id, out var session))
            {
                session.Status = CollaborationSessionStatus.Expired;
                session.CompletedAtUtc = DateTimeOffset.UtcNow;
            }

            _logger.LogInformation("Expired stale collaboration session {SessionId}", id);
        }
    }

    private async global::System.Threading.Tasks.Task EmitEventAsync(
        string eventType, Guid correlationId,
        Dictionary<string, string> payload, CancellationToken cancellationToken)
    {
        try
        {
            await _eventBus.PublishAsync(new SystemEvent(
                Guid.NewGuid(), eventType, nameof(AgentCollaborationManager),
                correlationId, payload.AsReadOnly(), DateTimeOffset.UtcNow), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit {EventType} event", eventType);
        }
    }

    // ── Mutable session wrapper for thread-safe updates ─────────

    private sealed class MutableSession
    {
        public Guid SessionId { get; init; }
        public string Purpose { get; init; } = string.Empty;
        public Guid InitiatorAgentId { get; init; }
        public string InitiatorAgentName { get; init; } = string.Empty;
        public List<CollaborationParticipant> Participants { get; init; } = new();
        public ConcurrentDictionary<string, string> SharedContext { get; init; } = new();
        public CollaborationSessionStatus Status { get; set; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? CompletedAtUtc { get; set; }

        public CollaborationSession ToSnapshot() => new(
            SessionId: SessionId,
            Purpose: Purpose,
            InitiatorAgentId: InitiatorAgentId,
            InitiatorAgentName: InitiatorAgentName,
            Participants: Participants.ToList(),
            SharedContext: new Dictionary<string, string>(SharedContext),
            Status: Status,
            CreatedAtUtc: CreatedAtUtc,
            CompletedAtUtc: CompletedAtUtc);
    }
}
