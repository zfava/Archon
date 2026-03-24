using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.HumanOverride;
using ArchonAI.Workflow;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Runtime.HumanOverride;

public sealed class HumanOverrideService : IHumanOverrideService
{
    private readonly IWorkflowEngine _workflowEngine;
    private readonly IEventBus _eventBus;
    private readonly IMemoryStore _memoryStore;
    private readonly ILogger<HumanOverrideService> _logger;

    private readonly ConcurrentDictionary<Guid, HumanOverrideEntry> _overrideLog = new();
    private readonly ConcurrentDictionary<Guid, string> _snapshots = new();

    public HumanOverrideService(
        IWorkflowEngine workflowEngine,
        IEventBus eventBus,
        IMemoryStore memoryStore,
        ILogger<HumanOverrideService> logger)
    {
        _workflowEngine = workflowEngine;
        _eventBus = eventBus;
        _memoryStore = memoryStore;
        _logger = logger;
    }

    public async Task<OverrideResult> PauseWorkflowAsync(PauseWorkflowRequest request, CancellationToken ct = default)
    {
        var overrideId = Guid.NewGuid();

        try
        {
            var currentState = _workflowEngine.GetState(request.WorkflowId);
            _snapshots[request.WorkflowId] = currentState.ToString();

            var newState = _workflowEngine.Transition(request.WorkflowId, WorkflowTrigger.Pause);

            var entry = new HumanOverrideEntry(
                Id: overrideId,
                WorkflowId: request.WorkflowId,
                Action: OverrideAction.PauseWorkflow,
                Status: OverrideStatus.Applied,
                Reason: request.Reason,
                PreviousValue: currentState.ToString(),
                NewValue: newState.ToString(),
                PerformedBy: request.PerformedBy,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: DateTimeOffset.UtcNow);

            _overrideLog[overrideId] = entry;
            await LogOverrideAsync(entry, ct);

            _logger.LogInformation(
                "Workflow {WorkflowId} paused by {User} — reason: {Reason}",
                request.WorkflowId, request.PerformedBy, request.Reason);

            return new OverrideResult(true, overrideId, "Workflow paused successfully.", newState.ToString());
        }
        catch (InvalidOperationException ex)
        {
            var failEntry = CreateFailedEntry(overrideId, request.WorkflowId, OverrideAction.PauseWorkflow,
                request.Reason, request.PerformedBy, ex.Message);
            _overrideLog[overrideId] = failEntry;
            await LogOverrideAsync(failEntry, ct);

            return new OverrideResult(false, overrideId, $"Cannot pause workflow: {ex.Message}", null);
        }
    }

    public async Task<OverrideResult> ResumeWorkflowAsync(ResumeWorkflowRequest request, CancellationToken ct = default)
    {
        var overrideId = Guid.NewGuid();

        try
        {
            var currentState = _workflowEngine.GetState(request.WorkflowId);
            var newState = _workflowEngine.Transition(request.WorkflowId, WorkflowTrigger.Resume);

            var entry = new HumanOverrideEntry(
                Id: overrideId,
                WorkflowId: request.WorkflowId,
                Action: OverrideAction.ResumeWorkflow,
                Status: OverrideStatus.Applied,
                Reason: request.Reason,
                PreviousValue: currentState.ToString(),
                NewValue: newState.ToString(),
                PerformedBy: request.PerformedBy,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: DateTimeOffset.UtcNow);

            _overrideLog[overrideId] = entry;
            await LogOverrideAsync(entry, ct);

            _logger.LogInformation(
                "Workflow {WorkflowId} resumed by {User} — reason: {Reason}",
                request.WorkflowId, request.PerformedBy, request.Reason);

            return new OverrideResult(true, overrideId, "Workflow resumed successfully.", newState.ToString());
        }
        catch (InvalidOperationException ex)
        {
            var failEntry = CreateFailedEntry(overrideId, request.WorkflowId, OverrideAction.ResumeWorkflow,
                request.Reason, request.PerformedBy, ex.Message);
            _overrideLog[overrideId] = failEntry;
            await LogOverrideAsync(failEntry, ct);

            return new OverrideResult(false, overrideId, $"Cannot resume workflow: {ex.Message}", null);
        }
    }

    public async Task<OverrideResult> CancelActionAsync(CancelActionRequest request, CancellationToken ct = default)
    {
        var overrideId = Guid.NewGuid();

        try
        {
            var currentState = _workflowEngine.GetState(request.WorkflowId);
            _snapshots[request.WorkflowId] = currentState.ToString();

            var newState = _workflowEngine.Transition(request.WorkflowId, WorkflowTrigger.Cancel);

            var taskInfo = request.TaskId.HasValue ? $" (task {request.TaskId.Value})" : "";

            var entry = new HumanOverrideEntry(
                Id: overrideId,
                WorkflowId: request.WorkflowId,
                Action: OverrideAction.CancelAction,
                Status: OverrideStatus.Applied,
                Reason: request.Reason,
                PreviousValue: $"{currentState}{taskInfo}",
                NewValue: newState.ToString(),
                PerformedBy: request.PerformedBy,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: DateTimeOffset.UtcNow);

            _overrideLog[overrideId] = entry;
            await LogOverrideAsync(entry, ct);

            await _eventBus.PublishAsync(new SystemEvent(
                Id: Guid.NewGuid(),
                EventType: "override.action.cancelled",
                Source: "HumanOverrideService",
                CorrelationId: request.WorkflowId,
                Payload: new Dictionary<string, string>
                {
                    ["workflowId"] = request.WorkflowId.ToString(),
                    ["taskId"] = request.TaskId?.ToString() ?? "",
                    ["reason"] = request.Reason,
                    ["performedBy"] = request.PerformedBy
                },
                OccurredAtUtc: DateTimeOffset.UtcNow), ct);

            _logger.LogWarning(
                "Action cancelled on workflow {WorkflowId}{TaskInfo} by {User} — reason: {Reason}",
                request.WorkflowId, taskInfo, request.PerformedBy, request.Reason);

            return new OverrideResult(true, overrideId, $"Action cancelled successfully{taskInfo}.", newState.ToString());
        }
        catch (InvalidOperationException ex)
        {
            var failEntry = CreateFailedEntry(overrideId, request.WorkflowId, OverrideAction.CancelAction,
                request.Reason, request.PerformedBy, ex.Message);
            _overrideLog[overrideId] = failEntry;
            await LogOverrideAsync(failEntry, ct);

            return new OverrideResult(false, overrideId, $"Cannot cancel action: {ex.Message}", null);
        }
    }

    public async Task<OverrideResult> ModifyStrategyAsync(ModifyStrategyRequest request, CancellationToken ct = default)
    {
        var overrideId = Guid.NewGuid();

        try
        {
            var currentState = _workflowEngine.GetState(request.WorkflowId);

            if (currentState is not (WorkflowState.Paused or WorkflowState.Scheduled or WorkflowState.Planning))
            {
                return new OverrideResult(false, overrideId,
                    $"Cannot modify strategy while workflow is in {currentState} state. Pause the workflow first.", currentState.ToString());
            }

            _snapshots[request.WorkflowId] = request.PreviousStrategy;

            var entry = new HumanOverrideEntry(
                Id: overrideId,
                WorkflowId: request.WorkflowId,
                Action: OverrideAction.ModifyStrategy,
                Status: OverrideStatus.Applied,
                Reason: request.Reason,
                PreviousValue: request.PreviousStrategy,
                NewValue: request.NewStrategy,
                PerformedBy: request.PerformedBy,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: DateTimeOffset.UtcNow);

            _overrideLog[overrideId] = entry;
            await LogOverrideAsync(entry, ct);

            await _eventBus.PublishAsync(new SystemEvent(
                Id: Guid.NewGuid(),
                EventType: "override.strategy.modified",
                Source: "HumanOverrideService",
                CorrelationId: request.WorkflowId,
                Payload: new Dictionary<string, string>
                {
                    ["workflowId"] = request.WorkflowId.ToString(),
                    ["previousStrategy"] = request.PreviousStrategy,
                    ["newStrategy"] = request.NewStrategy,
                    ["reason"] = request.Reason,
                    ["performedBy"] = request.PerformedBy
                },
                OccurredAtUtc: DateTimeOffset.UtcNow), ct);

            _logger.LogInformation(
                "Strategy modified on workflow {WorkflowId}: {Previous} → {New} by {User}",
                request.WorkflowId, request.PreviousStrategy, request.NewStrategy, request.PerformedBy);

            return new OverrideResult(true, overrideId,
                $"Strategy changed from '{request.PreviousStrategy}' to '{request.NewStrategy}'.", currentState.ToString());
        }
        catch (Exception ex)
        {
            var failEntry = CreateFailedEntry(overrideId, request.WorkflowId, OverrideAction.ModifyStrategy,
                request.Reason, request.PerformedBy, ex.Message);
            _overrideLog[overrideId] = failEntry;
            await LogOverrideAsync(failEntry, ct);

            return new OverrideResult(false, overrideId, $"Failed to modify strategy: {ex.Message}", null);
        }
    }

    public async Task<OverrideResult> RollbackAsync(RollbackRequest request, CancellationToken ct = default)
    {
        var rollbackId = Guid.NewGuid();

        if (!_overrideLog.TryGetValue(request.OverrideId, out var originalOverride))
        {
            return new OverrideResult(false, rollbackId, "Original override not found.", null);
        }

        if (originalOverride.Status == OverrideStatus.RolledBack)
        {
            return new OverrideResult(false, rollbackId, "Override has already been rolled back.", null);
        }

        try
        {
            string? restoredState = null;

            switch (originalOverride.Action)
            {
                case OverrideAction.PauseWorkflow:
                    _workflowEngine.Transition(request.WorkflowId, WorkflowTrigger.Resume);
                    restoredState = _workflowEngine.GetState(request.WorkflowId).ToString();
                    break;

                case OverrideAction.ModifyStrategy:
                    restoredState = originalOverride.PreviousValue;
                    await _eventBus.PublishAsync(new SystemEvent(
                        Id: Guid.NewGuid(),
                        EventType: "override.strategy.rolledback",
                        Source: "HumanOverrideService",
                        CorrelationId: request.WorkflowId,
                        Payload: new Dictionary<string, string>
                        {
                            ["workflowId"] = request.WorkflowId.ToString(),
                            ["restoredStrategy"] = originalOverride.PreviousValue ?? "",
                            ["reason"] = request.Reason,
                            ["performedBy"] = request.PerformedBy
                        },
                        OccurredAtUtc: DateTimeOffset.UtcNow), ct);
                    break;

                case OverrideAction.CancelAction:
                    return new OverrideResult(false, rollbackId,
                        "Cannot rollback a cancelled action. Create a new workflow instead.", null);

                default:
                    return new OverrideResult(false, rollbackId,
                        $"Rollback not supported for action type '{originalOverride.Action}'.", null);
            }

            // Mark original as rolled back
            var rolledBackOriginal = originalOverride with { Status = OverrideStatus.RolledBack };
            _overrideLog[request.OverrideId] = rolledBackOriginal;

            var rollbackEntry = new HumanOverrideEntry(
                Id: rollbackId,
                WorkflowId: request.WorkflowId,
                Action: OverrideAction.Rollback,
                Status: OverrideStatus.Applied,
                Reason: request.Reason,
                PreviousValue: originalOverride.NewValue,
                NewValue: originalOverride.PreviousValue,
                PerformedBy: request.PerformedBy,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: DateTimeOffset.UtcNow);

            _overrideLog[rollbackId] = rollbackEntry;
            await LogOverrideAsync(rollbackEntry, ct);

            _logger.LogInformation(
                "Rollback applied for override {OverrideId} on workflow {WorkflowId} by {User}",
                request.OverrideId, request.WorkflowId, request.PerformedBy);

            return new OverrideResult(true, rollbackId, "Rollback completed successfully.", restoredState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed for override {OverrideId}", request.OverrideId);
            return new OverrideResult(false, rollbackId, $"Rollback failed: {ex.Message}", null);
        }
    }

    public Task<OverrideLog> GetOverrideLogAsync(Guid? workflowId = null, int limit = 100, CancellationToken ct = default)
    {
        var entries = _overrideLog.Values
            .Where(e => workflowId == null || e.WorkflowId == workflowId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(limit)
            .ToList();

        return global::System.Threading.Tasks.Task.FromResult(new OverrideLog(entries, entries.Count, DateTimeOffset.UtcNow));
    }

    private async global::System.Threading.Tasks.Task LogOverrideAsync(HumanOverrideEntry entry, CancellationToken ct)
    {
        var record = new MemoryRecord(
            Id: Guid.NewGuid(),
            MemoryType: "human-override",
            Scope: $"override:workflow:{entry.WorkflowId}",
            Content: $"[{entry.Action}] {entry.Reason} — by {entry.PerformedBy} (status: {entry.Status})",
            Metadata: new Dictionary<string, string>
            {
                ["overrideId"] = entry.Id.ToString(),
                ["workflowId"] = entry.WorkflowId.ToString(),
                ["action"] = entry.Action.ToString(),
                ["status"] = entry.Status.ToString(),
                ["previousValue"] = entry.PreviousValue ?? "",
                ["newValue"] = entry.NewValue ?? "",
                ["performedBy"] = entry.PerformedBy
            },
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: null);

        await _memoryStore.SaveAsync(record, ct);

        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: $"override.{entry.Action.ToString().ToLowerInvariant()}",
            Source: "HumanOverrideService",
            CorrelationId: entry.WorkflowId,
            Payload: new Dictionary<string, string>
            {
                ["overrideId"] = entry.Id.ToString(),
                ["action"] = entry.Action.ToString(),
                ["status"] = entry.Status.ToString(),
                ["reason"] = entry.Reason,
                ["performedBy"] = entry.PerformedBy
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), ct);
    }

    private static HumanOverrideEntry CreateFailedEntry(
        Guid overrideId, Guid workflowId, OverrideAction action,
        string reason, string performedBy, string errorMessage)
    {
        return new HumanOverrideEntry(
            Id: overrideId,
            WorkflowId: workflowId,
            Action: action,
            Status: OverrideStatus.Failed,
            Reason: $"{reason} [FAILED: {errorMessage}]",
            PreviousValue: null,
            NewValue: null,
            PerformedBy: performedBy,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: DateTimeOffset.UtcNow);
    }
}
