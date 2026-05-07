using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.ActionSafety;
using ArchonAI.Core.Models.ProofAnalytics;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Governance;

public sealed class ActionSafetyService : IActionSafetyService
{
    private readonly ConcurrentDictionary<string, ActionSafetyClassification> _classifications = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, GovernedActionRecord> _actions = new();
    private readonly IProofAnalyticsService _proofAnalytics;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ActionSafetyService> _logger;

    public ActionSafetyService(
        IProofAnalyticsService proofAnalytics,
        IEventBus eventBus,
        ILogger<ActionSafetyService> logger)
    {
        _proofAnalytics = proofAnalytics;
        _eventBus = eventBus;
        _logger = logger;

        SeedDefaultClassifications();
    }

    // ── Classifications ──────────────────────────────────────────

    public Task<ActionSafetyClassification> GetClassificationAsync(
        string actionType, CancellationToken ct = default)
    {
        if (_classifications.TryGetValue(actionType, out var classification))
            return Task.FromResult(classification);

        // Unknown actions default to irreversible — safe default
        var fallback = new ActionSafetyClassification(
            Id: Guid.NewGuid(),
            ActionType: actionType,
            Reversibility: ReversibilityLevel.Irreversible,
            RollbackSupported: false,
            RollbackStrategy: RollbackStrategy.None,
            RollbackWindow: null,
            CompensationDescription: null,
            OperatorNotes: "No safety classification registered. Treated as irreversible.",
            ClassifiedBy: "system:default",
            ClassifiedAtUtc: DateTimeOffset.UtcNow);

        return Task.FromResult(fallback);
    }

    public Task<ActionSafetyClassification> SetClassificationAsync(
        ActionSafetyClassification classification, CancellationToken ct = default)
    {
        _classifications[classification.ActionType] = classification;

        _logger.LogInformation(
            "Safety classification set for {ActionType}: {Reversibility}, rollback={RollbackSupported}",
            classification.ActionType, classification.Reversibility, classification.RollbackSupported);

        return Task.FromResult(classification);
    }

    public Task<IReadOnlyList<ActionSafetyClassification>> ListClassificationsAsync(
        CancellationToken ct = default)
    {
        IReadOnlyList<ActionSafetyClassification> result = _classifications.Values
            .OrderBy(c => c.ActionType)
            .ToList();
        return Task.FromResult(result);
    }

    // ── Governed Actions ──────────────────────────────────────────

    public async Task<GovernedActionRecord> RecordActionAsync(
        GovernedActionRecord action, CancellationToken ct = default)
    {
        // Determine rollback eligibility based on classification
        var status = DetermineInitialStatus(action.SafetyClassification);
        var record = action with { Status = status };
        _actions[record.Id] = record;

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(), "action.safety.recorded", "ActionSafetyService",
            record.Id,
            new Dictionary<string, string>
            {
                ["actionId"] = record.Id.ToString(),
                ["tenantId"] = record.TenantId.ToString(),
                ["actionType"] = record.ActionType,
                ["reversibility"] = record.SafetyClassification.Reversibility.ToString(),
                ["rollbackSupported"] = record.SafetyClassification.RollbackSupported.ToString(),
                ["status"] = status.ToString(),
            }.AsReadOnly(),
            record.ExecutedAtUtc), ct);

        _logger.LogInformation(
            "Governed action {ActionId} recorded: type={ActionType} reversibility={Reversibility} status={Status}",
            record.Id, record.ActionType, record.SafetyClassification.Reversibility, status);

        return record;
    }

    public Task<GovernedActionRecord?> GetActionAsync(
        Guid actionId, CancellationToken ct = default)
    {
        _actions.TryGetValue(actionId, out var record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<GovernedActionRecord>> ListActionsAsync(
        Guid tenantId, int limit = 50, CancellationToken ct = default)
    {
        IReadOnlyList<GovernedActionRecord> result = _actions.Values
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.ExecutedAtUtc)
            .Take(limit)
            .ToList();
        return Task.FromResult(result);
    }

    // ── Rollback ──────────────────────────────────────────────────

    public async Task<GovernedActionRecord> AttemptRollbackAsync(
        Guid actionId, string initiatedBy, CancellationToken ct = default)
    {
        if (!_actions.TryGetValue(actionId, out var action))
            throw new KeyNotFoundException($"No governed action found with ID {actionId}.");

        var now = DateTimeOffset.UtcNow;
        var attempt = new RollbackAttempt(
            Id: Guid.NewGuid(),
            ActionId: actionId,
            InitiatedBy: initiatedBy,
            Status: RollbackAttemptStatus.InProgress,
            Detail: null,
            Error: null,
            InitiatedAtUtc: now,
            CompletedAtUtc: null);

        // Block irreversible actions
        if (action.SafetyClassification.Reversibility == ReversibilityLevel.Irreversible)
        {
            var blocked = attempt with
            {
                Status = RollbackAttemptStatus.Blocked,
                Error = "Action is classified as irreversible. Rollback is not supported.",
                CompletedAtUtc = now,
            };
            var updatedBlocked = AppendAttempt(action, blocked, GovernedActionStatus.Irreversible);
            _actions[actionId] = updatedBlocked;

            _logger.LogWarning("Rollback blocked for irreversible action {ActionId}", actionId);
            return updatedBlocked;
        }

        // Check rollback window
        if (action.SafetyClassification.RollbackWindow.HasValue)
        {
            var windowEnd = action.ExecutedAtUtc + action.SafetyClassification.RollbackWindow.Value;
            if (now > windowEnd)
            {
                var expired = attempt with
                {
                    Status = RollbackAttemptStatus.Blocked,
                    Error = $"Rollback window expired at {windowEnd:O}.",
                    CompletedAtUtc = now,
                };
                var updatedExpired = AppendAttempt(action, expired, GovernedActionStatus.RollbackWindowExpired);
                _actions[actionId] = updatedExpired;

                _logger.LogWarning("Rollback window expired for action {ActionId}", actionId);
                return updatedExpired;
            }
        }

        // Block if already rolled back
        if (action.Status is GovernedActionStatus.RolledBack or GovernedActionStatus.CompensationApplied)
        {
            var alreadyDone = attempt with
            {
                Status = RollbackAttemptStatus.Blocked,
                Error = $"Action already in state {action.Status}.",
                CompletedAtUtc = now,
            };
            var updatedDone = AppendAttempt(action, alreadyDone, action.Status);
            _actions[actionId] = updatedDone;
            return updatedDone;
        }

        // Perform rollback
        var safety = action.SafetyClassification;
        GovernedActionStatus newStatus;
        RollbackAttempt completedAttempt;
        string? compensationOutcome = null;

        if (safety.RollbackSupported &&
            safety.RollbackStrategy is RollbackStrategy.Automatic or RollbackStrategy.ManualTrigger)
        {
            // Simulate successful rollback
            completedAttempt = attempt with
            {
                Status = RollbackAttemptStatus.Succeeded,
                Detail = $"Rollback via {safety.RollbackStrategy} completed.",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            };
            newStatus = GovernedActionStatus.RolledBack;
        }
        else if (safety.Reversibility == ReversibilityLevel.Compensatable)
        {
            // Apply compensation
            compensationOutcome = safety.CompensationDescription ?? "Compensation applied.";
            completedAttempt = attempt with
            {
                Status = RollbackAttemptStatus.Succeeded,
                Detail = $"Compensation applied: {compensationOutcome}",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            };
            newStatus = GovernedActionStatus.CompensationApplied;
        }
        else
        {
            completedAttempt = attempt with
            {
                Status = RollbackAttemptStatus.Failed,
                Error = "No automated rollback mechanism available for this classification.",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            };
            newStatus = GovernedActionStatus.RollbackFailed;
        }

        var updated = AppendAttempt(action, completedAttempt, newStatus) with
        {
            CompensationOutcome = compensationOutcome ?? action.CompensationOutcome,
        };
        _actions[actionId] = updated;

        // Record proof event for rollback
        if (action.DecisionId.HasValue)
        {
            var eventType = completedAttempt.Status == RollbackAttemptStatus.Succeeded
                ? ProofEventType.ReversalApplied
                : ProofEventType.ActionExecuted;

            await _proofAnalytics.RecordEventAsync(new ProofEvent(
                Id: Guid.NewGuid(),
                TenantId: action.TenantId,
                DecisionId: action.DecisionId.Value,
                WorkflowId: action.WorkflowId,
                EventType: eventType,
                Actor: initiatedBy,
                Detail: completedAttempt.Detail ?? completedAttempt.Error,
                ExpectedValue: null,
                ActualValue: null,
                Variance: null,
                VariancePercent: null,
                ActionType: action.ActionType,
                IsSuccess: completedAttempt.Status == RollbackAttemptStatus.Succeeded,
                OverrideReason: null,
                EconomicImpact: null,
                ImpactAttribution: null,
                OccurredAtUtc: DateTimeOffset.UtcNow), ct);
        }

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(), "action.rollback.attempted", "ActionSafetyService",
            actionId,
            new Dictionary<string, string>
            {
                ["actionId"] = actionId.ToString(),
                ["tenantId"] = action.TenantId.ToString(),
                ["attemptStatus"] = completedAttempt.Status.ToString(),
                ["newStatus"] = newStatus.ToString(),
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation(
            "Rollback attempt for action {ActionId}: result={Status} newState={NewState}",
            actionId, completedAttempt.Status, newStatus);

        return updated;
    }

    // ── Summary ──────────────────────────────────────────────────

    public Task<RollbackSummary> GetRollbackSummaryAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var actions = _actions.Values.Where(a => a.TenantId == tenantId).ToList();
        var now = DateTimeOffset.UtcNow;

        var reversible = actions.Count(a => a.SafetyClassification.Reversibility == ReversibilityLevel.Reversible);
        var compensatable = actions.Count(a => a.SafetyClassification.Reversibility == ReversibilityLevel.Compensatable);
        var irreversible = actions.Count(a => a.SafetyClassification.Reversibility == ReversibilityLevel.Irreversible);

        var allAttempts = actions.SelectMany(a => a.RollbackHistory).ToList();
        var attempted = allAttempts.Count;
        var succeeded = allAttempts.Count(r => r.Status == RollbackAttemptStatus.Succeeded);
        var failed = allAttempts.Count(r => r.Status is RollbackAttemptStatus.Failed or RollbackAttemptStatus.Blocked);

        var withinWindow = actions.Count(a =>
            a.SafetyClassification.RollbackWindow.HasValue &&
            a.SafetyClassification.RollbackSupported &&
            now <= a.ExecutedAtUtc + a.SafetyClassification.RollbackWindow.Value &&
            a.Status is GovernedActionStatus.Executed or GovernedActionStatus.RollbackEligible);

        var expired = actions.Count(a =>
            a.SafetyClassification.RollbackWindow.HasValue &&
            now > a.ExecutedAtUtc + a.SafetyClassification.RollbackWindow.Value &&
            a.Status is GovernedActionStatus.Executed or GovernedActionStatus.RollbackEligible or GovernedActionStatus.RollbackWindowExpired);

        return Task.FromResult(new RollbackSummary(
            tenantId, actions.Count, reversible, compensatable, irreversible,
            attempted, succeeded, failed, withinWindow, expired));
    }

    // ── Auto-classification ─────────────────────────────────────

    public Task<ActionSafetyClassification> GetOrInferClassificationAsync(
        string actionType, string tenantId, CancellationToken ct = default)
    {
        // Return explicit classification if one exists
        if (_classifications.TryGetValue(actionType, out var existing))
            return Task.FromResult(existing);

        // Infer classification from action type keywords
        var lower = actionType.ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        var (reversibility, rollbackSupported, strategy, window, compensation, notes) = lower switch
        {
            _ when ContainsAny(lower, "delete", "remove", "terminate", "cancel", "drop") =>
                (ReversibilityLevel.Irreversible, false, RollbackStrategy.None,
                 (TimeSpan?)null, (string?)null,
                 "Inferred irreversible: destructive action detected."),

            _ when ContainsAny(lower, "send", "notify", "email", "publish", "broadcast") =>
                (ReversibilityLevel.Irreversible, false, RollbackStrategy.None,
                 (TimeSpan?)null, (string?)null,
                 "Inferred irreversible: communication action cannot be recalled."),

            _ when ContainsAny(lower, "update", "modify", "edit", "change", "patch") =>
                (ReversibilityLevel.Reversible, true, RollbackStrategy.Automatic,
                 (TimeSpan?)TimeSpan.FromHours(4), (string?)null,
                 "Inferred reversible: modification can be undone."),

            _ when ContainsAny(lower, "create", "add", "register", "insert") =>
                (ReversibilityLevel.Reversible, true, RollbackStrategy.Automatic,
                 (TimeSpan?)TimeSpan.FromHours(8), (string?)null,
                 "Inferred reversible: creation can be rolled back via deletion."),

            _ when ContainsAny(lower, "approve", "deny", "review", "reject") =>
                (ReversibilityLevel.Compensatable, false, RollbackStrategy.Compensation,
                 (TimeSpan?)TimeSpan.FromHours(2),
                 "Re-review and update the approval decision.",
                 "Inferred compensatable: approval decisions can be re-reviewed."),

            _ => (ReversibilityLevel.Compensatable, false, RollbackStrategy.Compensation,
                  (TimeSpan?)TimeSpan.FromHours(4),
                  "Manual intervention required for compensation.",
                  "Inferred default: unknown action type classified as compensatable."),
        };

        var inferred = new ActionSafetyClassification(
            Id: Guid.NewGuid(),
            ActionType: actionType,
            Reversibility: reversibility,
            RollbackSupported: rollbackSupported,
            RollbackStrategy: strategy,
            RollbackWindow: window,
            CompensationDescription: compensation,
            OperatorNotes: notes,
            ClassifiedBy: "auto-inference",
            ClassifiedAtUtc: now);

        _logger.LogInformation(
            "Auto-inferred safety classification for {ActionType}: {Reversibility} (tenant={TenantId})",
            actionType, reversibility, tenantId);

        return Task.FromResult(inferred);
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static GovernedActionStatus DetermineInitialStatus(ActionSafetyClassification safety)
    {
        if (safety.Reversibility == ReversibilityLevel.Irreversible)
            return GovernedActionStatus.Irreversible;

        if (safety.RollbackSupported)
            return GovernedActionStatus.RollbackEligible;

        return GovernedActionStatus.Executed;
    }

    private static GovernedActionRecord AppendAttempt(
        GovernedActionRecord action, RollbackAttempt attempt, GovernedActionStatus newStatus)
    {
        var history = new List<RollbackAttempt>(action.RollbackHistory) { attempt };
        return action with
        {
            Status = newStatus,
            RollbackHistory = history,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private void SeedDefaultClassifications()
    {
        var now = DateTimeOffset.UtcNow;

        Register("data.read", ReversibilityLevel.Reversible, true,
            RollbackStrategy.Automatic, TimeSpan.FromHours(24),
            null, "Read-only operations are inherently reversible (no state change).");

        Register("notification.send", ReversibilityLevel.Irreversible, false,
            RollbackStrategy.None, null,
            null, "Sent notifications cannot be unsent.");

        Register("workflow.execute", ReversibilityLevel.Compensatable, false,
            RollbackStrategy.Compensation, TimeSpan.FromHours(4),
            "Cancel in-progress workflow and revert dependent artifacts where possible.",
            "Partial compensation — downstream side effects may not be reversible.");

        Register("decision.execute", ReversibilityLevel.Compensatable, false,
            RollbackStrategy.Compensation, TimeSpan.FromHours(2),
            "Revert decision status to prior state. Financial consequences remain on record.",
            "Decision reversal does not undo downstream executed actions.");

        Register("connector.send", ReversibilityLevel.Irreversible, false,
            RollbackStrategy.None, null,
            null, "Data sent to external systems cannot be recalled by ArchonAI.");

        Register("connector.disconnect", ReversibilityLevel.Reversible, true,
            RollbackStrategy.ManualTrigger, TimeSpan.FromHours(24),
            null, "Reconnection restores prior state.");

        Register("strategy.override", ReversibilityLevel.Reversible, true,
            RollbackStrategy.Automatic, TimeSpan.FromHours(8),
            null, "Revert to prior strategy version.");

        Register("policy.delete", ReversibilityLevel.Compensatable, false,
            RollbackStrategy.Compensation, TimeSpan.FromHours(1),
            "Re-create the policy with identical parameters.",
            "Recreation may not preserve original policy ID or audit trail continuity.");

        Register("workflow.cancel", ReversibilityLevel.Irreversible, false,
            RollbackStrategy.None, null,
            null, "Cancelled workflows cannot be resumed — create a new workflow instead.");

        Register("rbac.role.delete", ReversibilityLevel.Compensatable, false,
            RollbackStrategy.Compensation, TimeSpan.FromHours(2),
            "Re-create role with same permissions. Existing user assignments must be re-applied.",
            "Role deletion audit records are preserved.");

        void Register(string actionType, ReversibilityLevel rev, bool rollbackSupported,
            RollbackStrategy strategy, TimeSpan? window, string? compensation, string? notes)
        {
            _classifications[actionType] = new ActionSafetyClassification(
                Guid.NewGuid(), actionType, rev, rollbackSupported,
                strategy, window, compensation, notes,
                "system:seed", now);
        }
    }
}
