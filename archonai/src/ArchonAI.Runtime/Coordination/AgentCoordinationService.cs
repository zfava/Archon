using System.Collections.Concurrent;
using System.Text.Json;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Coordination;
using ArchonAI.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreTask = ArchonAI.Core.Models.Task;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;

namespace ArchonAI.Runtime.Coordination;

public sealed class AgentCoordinationService : IAgentCoordinationService
{
    private const string CoordinationSupportRequestEvent = "coordination.support.request";
    private const string CoordinationSupportResponseEvent = "coordination.support.response";
    private const string CoordinationKnowledgeShareEvent = "coordination.knowledge.share";
    private const string CoordinationDelegationEvent = "coordination.delegation";
    private const string CoordinationDelegationResultEvent = "coordination.delegation.result";

    private readonly IEnumerable<IAgent> _agentImplementations;
    private readonly IAgentCapabilityRegistry _capabilityRegistry;
    private readonly IEventBus _eventBus;
    private readonly IMemoryStore _memoryStore;
    private readonly CoordinationOptions _options;
    private readonly ILogger<AgentCoordinationService> _logger;

    // Pending support requests awaiting responses
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<TaskSupportResponse>> _pendingSupportRequests = new();

    // Pending delegations awaiting results
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<TaskDelegationResult>> _pendingDelegations = new();

    // Metrics
    private long _supportRequestsSent;
    private long _supportRequestsReceived;
    private long _supportRequestsCompleted;
    private long _supportRequestsTimedOut;
    private long _knowledgeShareEvents;
    private long _taskDelegations;
    private long _taskDelegationsCompleted;
    private long _taskDelegationsTimedOut;

    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
    private bool _subscriptionsInitialized;

    public AgentCoordinationService(
        IEnumerable<IAgent> agentImplementations,
        IAgentCapabilityRegistry capabilityRegistry,
        IEventBus eventBus,
        IMemoryStore memoryStore,
        IOptions<CoordinationOptions> options,
        ILogger<AgentCoordinationService> logger)
    {
        _agentImplementations = agentImplementations;
        _capabilityRegistry = capabilityRegistry;
        _eventBus = eventBus;
        _memoryStore = memoryStore;
        _options = options.Value;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task<TaskSupportResponse> RequestTaskSupportAsync(
        TaskSupportRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureEventSubscriptionsAsync(cancellationToken);

        using var activity = Telemetry.ActivitySource.StartActivity("coordination.support.request");
        activity?.SetTag("requesting.agent.id", request.RequestingAgentId.ToString());
        activity?.SetTag("required.capability", request.RequiredCapability);

        Interlocked.Increment(ref _supportRequestsSent);
        Telemetry.CoordinationSupportRequestsSent.Add(1);

        // Find an agent with the required capability
        var candidates = await _capabilityRegistry.QueryByCapabilityAsync(request.RequiredCapability, cancellationToken);
        var available = candidates
            .Where(c => c.AgentId != request.RequestingAgentId)
            .ToList();

        if (available.Count == 0)
        {
            _logger.LogWarning(
                "No agent available with capability {Capability} to support agent {RequestingAgentId}",
                request.RequiredCapability, request.RequestingAgentId);

            return new TaskSupportResponse(
                RequestId: request.Id,
                RespondingAgentId: Guid.Empty,
                RespondingAgentName: string.Empty,
                Outcome: TaskSupportOutcome.Declined,
                Summary: $"No agent available with capability '{request.RequiredCapability}'",
                Outputs: new Dictionary<string, string>(),
                RespondedAtUtc: DateTimeOffset.UtcNow);
        }

        // Select the best candidate (lowest latency)
        var best = available.OrderBy(a => a.AverageLatencyMs).First();
        var respondingImpl = _agentImplementations.FirstOrDefault(a => a.Describe().Id == best.AgentId);

        if (respondingImpl is null)
        {
            return new TaskSupportResponse(
                RequestId: request.Id,
                RespondingAgentId: best.AgentId,
                RespondingAgentName: best.AgentName,
                Outcome: TaskSupportOutcome.Declined,
                Summary: "Agent implementation not found in runtime",
                Outputs: new Dictionary<string, string>(),
                RespondedAtUtc: DateTimeOffset.UtcNow);
        }

        // Register a completion source for the response
        var tcs = new TaskCompletionSource<TaskSupportResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingSupportRequests[request.Id] = tcs;

        // Publish the support request event
        await EmitEventAsync(CoordinationSupportRequestEvent, request.RequestingAgentId, new Dictionary<string, string>
        {
            ["requestId"] = request.Id.ToString(),
            ["requestingAgentId"] = request.RequestingAgentId.ToString(),
            ["requestingAgentName"] = request.RequestingAgentName,
            ["targetAgentId"] = best.AgentId.ToString(),
            ["targetAgentName"] = best.AgentName,
            ["requiredCapability"] = request.RequiredCapability,
            ["reason"] = request.Reason
        }, cancellationToken);

        Interlocked.Increment(ref _supportRequestsReceived);
        Telemetry.CoordinationSupportRequestsReceived.Add(1);

        _logger.LogInformation(
            "Agent {RequestingAgent} requesting support from {RespondingAgent} for capability {Capability}",
            request.RequestingAgentName, best.AgentName, request.RequiredCapability);

        // Execute the support task on the responding agent with timeout
        var timeout = request.Timeout > TimeSpan.Zero
            ? request.Timeout
            : TimeSpan.FromSeconds(_options.DefaultTimeoutSeconds);

        _ = ExecuteSupportTaskAsync(request, respondingImpl, best, timeout, cancellationToken);

        // Wait for the response with timeout
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var response = await tcs.Task.WaitAsync(timeoutCts.Token);
            Interlocked.Increment(ref _supportRequestsCompleted);
            Telemetry.CoordinationSupportRequestsCompleted.Add(1);
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref _supportRequestsTimedOut);
            Telemetry.CoordinationSupportRequestsTimedOut.Add(1);
            _pendingSupportRequests.TryRemove(request.Id, out _);

            _logger.LogWarning("Support request {RequestId} timed out after {Timeout}s",
                request.Id, timeout.TotalSeconds);

            return new TaskSupportResponse(
                RequestId: request.Id,
                RespondingAgentId: best.AgentId,
                RespondingAgentName: best.AgentName,
                Outcome: TaskSupportOutcome.TimedOut,
                Summary: $"Support request timed out after {timeout.TotalSeconds}s",
                Outputs: new Dictionary<string, string>(),
                RespondedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    public async global::System.Threading.Tasks.Task ShareKnowledgeAsync(
        KnowledgeSharePayload payload, CancellationToken cancellationToken = default)
    {
        await EnsureEventSubscriptionsAsync(cancellationToken);

        using var activity = Telemetry.ActivitySource.StartActivity("coordination.knowledge.share");
        activity?.SetTag("source.agent.id", payload.SourceAgentId.ToString());
        activity?.SetTag("topic", payload.Topic);

        Interlocked.Increment(ref _knowledgeShareEvents);
        Telemetry.CoordinationKnowledgeShares.Add(1);

        // Persist knowledge to memory store
        var scope = payload.TargetAgentId.HasValue
            ? $"coordination:knowledge:{payload.TargetAgentId.Value}"
            : "coordination:knowledge:broadcast";

        var metadata = new Dictionary<string, string>(payload.Metadata)
        {
            ["sourceAgentId"] = payload.SourceAgentId.ToString(),
            ["sourceAgentName"] = payload.SourceAgentName,
            ["topic"] = payload.Topic
        };

        if (payload.TargetAgentId.HasValue)
            metadata["targetAgentId"] = payload.TargetAgentId.Value.ToString();

        var memoryRecord = new MemoryRecord(
            Id: payload.Id,
            MemoryType: "coordination-knowledge",
            Scope: scope,
            Content: payload.Content,
            Metadata: metadata,
            CreatedAtUtc: payload.SharedAtUtc,
            ExpiresAtUtc: null);

        await _memoryStore.SaveAsync(memoryRecord, cancellationToken);

        // Publish knowledge share event
        await EmitEventAsync(CoordinationKnowledgeShareEvent, payload.SourceAgentId, new Dictionary<string, string>
        {
            ["payloadId"] = payload.Id.ToString(),
            ["sourceAgentId"] = payload.SourceAgentId.ToString(),
            ["sourceAgentName"] = payload.SourceAgentName,
            ["targetAgentId"] = payload.TargetAgentId?.ToString() ?? "broadcast",
            ["topic"] = payload.Topic
        }, cancellationToken);

        _logger.LogInformation(
            "Agent {SourceAgent} shared knowledge on topic '{Topic}' (target: {Target})",
            payload.SourceAgentName, payload.Topic,
            payload.TargetAgentId?.ToString() ?? "broadcast");
    }

    public async global::System.Threading.Tasks.Task<TaskDelegationResult> DelegateTaskAsync(
        TaskDelegation delegation, CancellationToken cancellationToken = default)
    {
        await EnsureEventSubscriptionsAsync(cancellationToken);

        using var activity = Telemetry.ActivitySource.StartActivity("coordination.delegation");
        activity?.SetTag("delegating.agent.id", delegation.DelegatingAgentId.ToString());
        activity?.SetTag("target.agent.id", delegation.TargetAgentId.ToString());

        Interlocked.Increment(ref _taskDelegations);
        Telemetry.CoordinationDelegations.Add(1);

        // Find the target agent implementation
        var targetImpl = _agentImplementations.FirstOrDefault(a => a.Describe().Id == delegation.TargetAgentId);

        if (targetImpl is null)
        {
            _logger.LogWarning("Delegation target agent {TargetAgentId} not found in runtime",
                delegation.TargetAgentId);

            return new TaskDelegationResult(
                DelegationId: delegation.Id,
                TargetAgentId: delegation.TargetAgentId,
                Outcome: DelegationOutcome.Declined,
                Result: null,
                CompletedAtUtc: DateTimeOffset.UtcNow);
        }

        // Register completion source
        var tcs = new TaskCompletionSource<TaskDelegationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingDelegations[delegation.Id] = tcs;

        // Publish delegation event
        await EmitEventAsync(CoordinationDelegationEvent, delegation.DelegatingAgentId, new Dictionary<string, string>
        {
            ["delegationId"] = delegation.Id.ToString(),
            ["delegatingAgentId"] = delegation.DelegatingAgentId.ToString(),
            ["delegatingAgentName"] = delegation.DelegatingAgentName,
            ["targetAgentId"] = delegation.TargetAgentId.ToString(),
            ["targetAgentName"] = delegation.TargetAgentName,
            ["originalTaskId"] = delegation.OriginalTaskId.ToString(),
            ["requiredCapability"] = delegation.RequiredCapability
        }, cancellationToken);

        _logger.LogInformation(
            "Agent {DelegatingAgent} delegating task to {TargetAgent} for capability {Capability}",
            delegation.DelegatingAgentName, delegation.TargetAgentName, delegation.RequiredCapability);

        // Execute the delegated task with timeout
        var timeout = delegation.Timeout > TimeSpan.Zero
            ? delegation.Timeout
            : TimeSpan.FromSeconds(_options.DefaultTimeoutSeconds);

        _ = ExecuteDelegatedTaskAsync(delegation, targetImpl, timeout, cancellationToken);

        // Wait for the result with timeout
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var result = await tcs.Task.WaitAsync(timeoutCts.Token);
            Interlocked.Increment(ref _taskDelegationsCompleted);
            Telemetry.CoordinationDelegationsCompleted.Add(1);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref _taskDelegationsTimedOut);
            Telemetry.CoordinationDelegationsTimedOut.Add(1);
            _pendingDelegations.TryRemove(delegation.Id, out _);

            _logger.LogWarning("Task delegation {DelegationId} timed out after {Timeout}s",
                delegation.Id, timeout.TotalSeconds);

            return new TaskDelegationResult(
                DelegationId: delegation.Id,
                TargetAgentId: delegation.TargetAgentId,
                Outcome: DelegationOutcome.TimedOut,
                Result: null,
                CompletedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    public CoordinationStatus GetStatus()
    {
        return new CoordinationStatus(
            SupportRequestsSent: Interlocked.Read(ref _supportRequestsSent),
            SupportRequestsReceived: Interlocked.Read(ref _supportRequestsReceived),
            SupportRequestsCompleted: Interlocked.Read(ref _supportRequestsCompleted),
            SupportRequestsTimedOut: Interlocked.Read(ref _supportRequestsTimedOut),
            KnowledgeShareEvents: Interlocked.Read(ref _knowledgeShareEvents),
            TaskDelegations: Interlocked.Read(ref _taskDelegations),
            TaskDelegationsCompleted: Interlocked.Read(ref _taskDelegationsCompleted),
            TaskDelegationsTimedOut: Interlocked.Read(ref _taskDelegationsTimedOut),
            ActiveSupportRequests: _pendingSupportRequests.Count,
            ActiveDelegations: _pendingDelegations.Count,
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task ExecuteSupportTaskAsync(
        TaskSupportRequest request,
        IAgent respondingAgent,
        AgentCapabilityProfile profile,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var supportTask = new CoreTask(
                Id: Guid.NewGuid(),
                ObjectiveId: request.TaskId,
                Order: 0,
                Name: $"Support: {request.RequiredCapability}",
                Description: request.Reason,
                RequiredCapability: request.RequiredCapability,
                Inputs: request.Context,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                StartedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: null);

            var context = new CoreExecutionContext(
                CorrelationId: Guid.NewGuid(),
                ObjectiveId: request.TaskId,
                TaskId: supportTask.Id,
                TenantId: "coordination",
                Metadata: new Dictionary<string, string>
                {
                    ["coordinationType"] = "support",
                    ["requestId"] = request.Id.ToString(),
                    ["requestingAgentId"] = request.RequestingAgentId.ToString()
                },
                RequestedAtUtc: DateTimeOffset.UtcNow);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var executionResult = await respondingAgent.ExecuteAsync(supportTask, context, timeoutCts.Token);

            var response = new TaskSupportResponse(
                RequestId: request.Id,
                RespondingAgentId: profile.AgentId,
                RespondingAgentName: profile.AgentName,
                Outcome: executionResult.IsSuccess ? TaskSupportOutcome.Completed : TaskSupportOutcome.Failed,
                Summary: executionResult.Summary,
                Outputs: executionResult.Outputs,
                RespondedAtUtc: DateTimeOffset.UtcNow);

            if (_pendingSupportRequests.TryRemove(request.Id, out var tcs))
                tcs.TrySetResult(response);

            await EmitEventAsync(CoordinationSupportResponseEvent, profile.AgentId, new Dictionary<string, string>
            {
                ["requestId"] = request.Id.ToString(),
                ["respondingAgentId"] = profile.AgentId.ToString(),
                ["outcome"] = response.Outcome.ToString()
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Timeout handled by the caller's WaitAsync
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Support task execution failed for request {RequestId}", request.Id);

            if (_pendingSupportRequests.TryRemove(request.Id, out var tcs))
            {
                tcs.TrySetResult(new TaskSupportResponse(
                    RequestId: request.Id,
                    RespondingAgentId: profile.AgentId,
                    RespondingAgentName: profile.AgentName,
                    Outcome: TaskSupportOutcome.Failed,
                    Summary: ex.Message,
                    Outputs: new Dictionary<string, string>(),
                    RespondedAtUtc: DateTimeOffset.UtcNow));
            }
        }
    }

    private async global::System.Threading.Tasks.Task ExecuteDelegatedTaskAsync(
        TaskDelegation delegation,
        IAgent targetAgent,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var delegatedTask = new CoreTask(
                Id: Guid.NewGuid(),
                ObjectiveId: delegation.OriginalTaskId,
                Order: 0,
                Name: $"Delegated: {delegation.RequiredCapability}",
                Description: $"Task delegated by {delegation.DelegatingAgentName}",
                RequiredCapability: delegation.RequiredCapability,
                Inputs: delegation.TaskInputs,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                StartedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: null);

            var context = new CoreExecutionContext(
                CorrelationId: Guid.NewGuid(),
                ObjectiveId: delegation.OriginalTaskId,
                TaskId: delegatedTask.Id,
                TenantId: "coordination",
                Metadata: new Dictionary<string, string>
                {
                    ["coordinationType"] = "delegation",
                    ["delegationId"] = delegation.Id.ToString(),
                    ["delegatingAgentId"] = delegation.DelegatingAgentId.ToString()
                },
                RequestedAtUtc: DateTimeOffset.UtcNow);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var executionResult = await targetAgent.ExecuteAsync(delegatedTask, context, timeoutCts.Token);

            var result = new TaskDelegationResult(
                DelegationId: delegation.Id,
                TargetAgentId: delegation.TargetAgentId,
                Outcome: executionResult.IsSuccess ? DelegationOutcome.Completed : DelegationOutcome.Failed,
                Result: executionResult,
                CompletedAtUtc: DateTimeOffset.UtcNow);

            if (_pendingDelegations.TryRemove(delegation.Id, out var tcs))
                tcs.TrySetResult(result);

            await EmitEventAsync(CoordinationDelegationResultEvent, delegation.TargetAgentId, new Dictionary<string, string>
            {
                ["delegationId"] = delegation.Id.ToString(),
                ["targetAgentId"] = delegation.TargetAgentId.ToString(),
                ["outcome"] = result.Outcome.ToString()
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Timeout handled by the caller's WaitAsync
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delegated task execution failed for delegation {DelegationId}", delegation.Id);

            if (_pendingDelegations.TryRemove(delegation.Id, out var tcs))
            {
                tcs.TrySetResult(new TaskDelegationResult(
                    DelegationId: delegation.Id,
                    TargetAgentId: delegation.TargetAgentId,
                    Outcome: DelegationOutcome.Failed,
                    Result: null,
                    CompletedAtUtc: DateTimeOffset.UtcNow));
            }
        }
    }

    private async global::System.Threading.Tasks.Task EnsureEventSubscriptionsAsync(CancellationToken cancellationToken)
    {
        if (_subscriptionsInitialized) return;

        await _subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            if (_subscriptionsInitialized) return;

            // Subscribe to knowledge share events for memory persistence
            await _eventBus.SubscribeAsync(CoordinationKnowledgeShareEvent, async (evt, ct) =>
            {
                _logger.LogDebug("Knowledge share event received: {EventId}", evt.Id);
                await global::System.Threading.Tasks.Task.CompletedTask;
            }, cancellationToken);

            _subscriptionsInitialized = true;
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    private async global::System.Threading.Tasks.Task EmitEventAsync(
        string eventType, Guid correlationId,
        Dictionary<string, string> payload, CancellationToken cancellationToken)
    {
        try
        {
            var systemEvent = new SystemEvent(
                Guid.NewGuid(), eventType, nameof(AgentCoordinationService),
                correlationId, payload.AsReadOnly(), DateTimeOffset.UtcNow);
            await _eventBus.PublishAsync(systemEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit {EventType} event", eventType);
        }
    }
}
