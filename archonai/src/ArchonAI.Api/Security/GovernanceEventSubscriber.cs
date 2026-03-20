using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Inspection;
using ArchonAI.Core.Models.ProofAnalytics;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Api.Security;

/// <summary>
/// Subscribes to domain events on the IEventBus and dispatches inspection data
/// recording and proof analytics auto-emission. This keeps producers (PolicyEngine,
/// HeroWorkflowService, GatedActionExecutor) non-blocking while ensuring events
/// are reliably dispatched, observable, and deterministically testable.
/// </summary>
public sealed class GovernanceEventSubscriber : IHostedService
{
    private readonly IEventBus _eventBus;
    private readonly InspectionService _inspectionService;
    private readonly IProofAnalyticsService _proofAnalytics;
    private readonly ILogger<GovernanceEventSubscriber> _logger;

    public GovernanceEventSubscriber(
        IEventBus eventBus,
        InspectionService inspectionService,
        IProofAnalyticsService proofAnalytics,
        ILogger<GovernanceEventSubscriber> logger)
    {
        _eventBus = eventBus;
        _inspectionService = inspectionService;
        _proofAnalytics = proofAnalytics;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // ── Inspection event subscriptions ──
        await _eventBus.SubscribeAsync(
            "inspection.policy-evaluation-recorded",
            HandlePolicyEvaluationRecordedAsync,
            cancellationToken);

        await _eventBus.SubscribeAsync(
            "inspection.memory-reference-recorded",
            HandleMemoryReferenceRecordedAsync,
            cancellationToken);

        // ── Proof analytics auto-emission subscriptions ──
        await _eventBus.SubscribeAsync(
            "decision.created",
            HandleDecisionCreatedAsync,
            cancellationToken);

        await _eventBus.SubscribeAsync(
            "decision.status-updated",
            HandleDecisionStatusUpdatedAsync,
            cancellationToken);

        await _eventBus.SubscribeAsync(
            "hero_workflow.step-completed",
            HandleWorkflowStepCompletedAsync,
            cancellationToken);

        await _eventBus.SubscribeAsync(
            "hero_workflow.completed",
            HandleWorkflowCompletedAsync,
            cancellationToken);

        await _eventBus.SubscribeAsync(
            "hero_workflow.failed",
            HandleWorkflowFailedAsync,
            cancellationToken);

        await _eventBus.SubscribeAsync(
            "gated-action.executed",
            HandleActionExecutedAsync,
            cancellationToken);

        _logger.LogInformation(
            "GovernanceEventSubscriber started — subscribed to 8 event types for inspection and proof auto-emission");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ══════════════════════════════════════════════════════════════
    //  Inspection handlers
    // ══════════════════════════════════════════════════════════════

    private Task HandlePolicyEvaluationRecordedAsync(SystemEvent evt, CancellationToken ct)
    {
        try
        {
            var subjectType = evt.Payload.GetValueOrDefault("subjectType") ?? "unknown";
            var subjectId = evt.Payload.GetValueOrDefault("subjectId") ?? "";
            var tenantIdStr = evt.Payload.GetValueOrDefault("tenantId") ?? "";
            var isAllowed = bool.TryParse(evt.Payload.GetValueOrDefault("isAllowed"), out var a) && a;
            var riskScore = double.TryParse(evt.Payload.GetValueOrDefault("riskScore"), out var r) ? r : 0;
            var confidenceScore = double.TryParse(evt.Payload.GetValueOrDefault("confidenceScore"), out var c) ? c : 0;
            var reason = evt.Payload.GetValueOrDefault("reason") ?? "";

            if (!Guid.TryParse(tenantIdStr, out var tenantId))
                return Task.CompletedTask;

            var evalResult = new PolicyEvaluationResult(
                EvaluationId: evt.Id,
                TenantId: tenantId,
                SubjectType: subjectType,
                SubjectId: subjectId,
                IsAllowed: isAllowed,
                RiskScore: riskScore,
                ConfidenceScore: confidenceScore,
                RequiresApproval: bool.TryParse(evt.Payload.GetValueOrDefault("requiresApproval"), out var ra) && ra,
                ApprovalState: evt.Payload.GetValueOrDefault("approvalState") ?? "not-required",
                ManualOverrideState: evt.Payload.GetValueOrDefault("manualOverrideState") ?? "none",
                ApprovalCheckpoint: evt.Payload.GetValueOrDefault("approvalCheckpoint") ?? "none",
                GuardrailViolations: (evt.Payload.GetValueOrDefault("violations") ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries),
                RulesEvaluated: Array.Empty<PolicyRuleResult>(),
                Reason: reason,
                EvaluatedAtUtc: evt.OccurredAtUtc);

            _inspectionService.RecordPolicyEvaluation(subjectType, subjectId, evalResult);

            _logger.LogDebug(
                "Inspection: recorded policy evaluation for {SubjectType}/{SubjectId}",
                subjectType, subjectId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "GovernanceEventSubscriber: failed to handle policy evaluation event {EventId}",
                evt.Id);
        }
        return Task.CompletedTask;
    }

    private Task HandleMemoryReferenceRecordedAsync(SystemEvent evt, CancellationToken ct)
    {
        try
        {
            var subjectType = evt.Payload.GetValueOrDefault("subjectType") ?? "unknown";
            var subjectId = evt.Payload.GetValueOrDefault("subjectId") ?? "";
            var memoryRecordId = evt.Payload.GetValueOrDefault("memoryRecordId") ?? "";
            var scope = evt.Payload.GetValueOrDefault("scope") ?? "unknown";
            var relevance = double.TryParse(evt.Payload.GetValueOrDefault("relevanceScore"), out var rel) ? rel : 0;

            var reference = new MemoryContextReference(
                MemoryId: Guid.TryParse(memoryRecordId, out var mrid) ? mrid : Guid.NewGuid(),
                MemoryType: scope,
                Source: evt.Payload.GetValueOrDefault("category") ?? "general",
                ContentSummary: evt.Payload.GetValueOrDefault("summary") ?? "Memory reference",
                RelevanceScore: relevance,
                UsageContext: subjectType,
                RetrievedAtUtc: evt.OccurredAtUtc);

            _inspectionService.RecordMemoryReference(subjectType, subjectId, reference);

            _logger.LogDebug(
                "Inspection: recorded memory reference for {SubjectType}/{SubjectId}",
                subjectType, subjectId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "GovernanceEventSubscriber: failed to handle memory reference event {EventId}",
                evt.Id);
        }
        return Task.CompletedTask;
    }

    // ══════════════════════════════════════════════════════════════
    //  Proof analytics auto-emission handlers
    // ══════════════════════════════════════════════════════════════

    private async Task HandleDecisionCreatedAsync(SystemEvent evt, CancellationToken ct)
    {
        try
        {
            if (!Guid.TryParse(evt.Payload.GetValueOrDefault("decisionId"), out var decisionId))
                return;
            if (!Guid.TryParse(evt.Payload.GetValueOrDefault("tenantId"), out var tenantId))
                return;

            await _proofAnalytics.RecordEventAsync(new ProofEvent(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                DecisionId: decisionId,
                WorkflowId: null,
                EventType: ProofEventType.DecisionCreated,
                Actor: evt.Source,
                Detail: $"Decision created: {evt.Payload.GetValueOrDefault("title")}",
                ExpectedValue: null,
                ActualValue: null,
                Variance: null,
                VariancePercent: null,
                ActionType: "decision.create",
                IsSuccess: true,
                OverrideReason: null,
                EconomicImpact: null,
                ImpactAttribution: null,
                OccurredAtUtc: evt.OccurredAtUtc), ct);

            _logger.LogDebug("Proof auto-emit: DecisionCreated for {DecisionId}", decisionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "GovernanceEventSubscriber: failed to auto-emit proof for decision.created {EventId}",
                evt.Id);
        }
    }

    private async Task HandleDecisionStatusUpdatedAsync(SystemEvent evt, CancellationToken ct)
    {
        try
        {
            if (!Guid.TryParse(evt.Payload.GetValueOrDefault("decisionId"), out var decisionId))
                return;

            var newStatus = evt.Payload.GetValueOrDefault("newStatus") ?? "Unknown";
            var actor = evt.Payload.GetValueOrDefault("actor") ?? evt.Source;

            var eventType = newStatus switch
            {
                "Executing" => ProofEventType.ActionExecuted,
                "Completed" => ProofEventType.ActualOutcomeRecorded,
                "Approved" => ProofEventType.ApprovalGranted,
                "Rejected" => ProofEventType.ApprovalDenied,
                _ => (ProofEventType?)null,
            };

            if (eventType is null) return;

            // Derive tenantId from the correlation ID (decision store sets it)
            var tenantId = Guid.TryParse(evt.Payload.GetValueOrDefault("tenantId"), out var tid)
                ? tid : evt.CorrelationId;

            await _proofAnalytics.RecordEventAsync(new ProofEvent(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                DecisionId: decisionId,
                WorkflowId: null,
                EventType: eventType.Value,
                Actor: actor,
                Detail: $"Decision status changed to {newStatus}",
                ExpectedValue: null,
                ActualValue: null,
                Variance: null,
                VariancePercent: null,
                ActionType: $"decision.{newStatus.ToLowerInvariant()}",
                IsSuccess: newStatus is "Completed" or "Executing" or "Approved",
                OverrideReason: null,
                EconomicImpact: null,
                ImpactAttribution: null,
                OccurredAtUtc: evt.OccurredAtUtc), ct);

            _logger.LogDebug("Proof auto-emit: {EventType} for decision {DecisionId}", eventType, decisionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "GovernanceEventSubscriber: failed to auto-emit proof for decision.status-updated {EventId}",
                evt.Id);
        }
    }

    private async Task HandleWorkflowStepCompletedAsync(SystemEvent evt, CancellationToken ct)
    {
        try
        {
            if (!Guid.TryParse(evt.Payload.GetValueOrDefault("workflowId"), out var workflowId))
                return;
            if (!Guid.TryParse(evt.Payload.GetValueOrDefault("tenantId"), out var tenantId))
                return;

            var decisionId = Guid.TryParse(evt.Payload.GetValueOrDefault("decisionId"), out var did)
                ? did : workflowId;

            await _proofAnalytics.RecordEventAsync(new ProofEvent(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                DecisionId: decisionId,
                WorkflowId: workflowId,
                EventType: ProofEventType.ActionExecuted,
                Actor: evt.Payload.GetValueOrDefault("actor") ?? "system",
                Detail: $"Workflow step completed: {evt.Payload.GetValueOrDefault("stepId")}",
                ExpectedValue: null,
                ActualValue: null,
                Variance: null,
                VariancePercent: null,
                ActionType: $"workflow.step.{evt.Payload.GetValueOrDefault("stepId")}",
                IsSuccess: true,
                OverrideReason: null,
                EconomicImpact: null,
                ImpactAttribution: null,
                OccurredAtUtc: evt.OccurredAtUtc), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "GovernanceEventSubscriber: failed to auto-emit proof for workflow step {EventId}",
                evt.Id);
        }
    }

    private async Task HandleWorkflowCompletedAsync(SystemEvent evt, CancellationToken ct)
    {
        try
        {
            if (!Guid.TryParse(evt.Payload.GetValueOrDefault("workflowId"), out var workflowId))
                return;
            if (!Guid.TryParse(evt.Payload.GetValueOrDefault("tenantId"), out var tenantId))
                return;

            var decisionId = Guid.TryParse(evt.Payload.GetValueOrDefault("decisionId"), out var did)
                ? did : workflowId;

            await _proofAnalytics.RecordEventAsync(new ProofEvent(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                DecisionId: decisionId,
                WorkflowId: workflowId,
                EventType: ProofEventType.ActualOutcomeRecorded,
                Actor: evt.Payload.GetValueOrDefault("actor") ?? "system",
                Detail: "Workflow completed successfully",
                ExpectedValue: null,
                ActualValue: null,
                Variance: null,
                VariancePercent: null,
                ActionType: "workflow.completed",
                IsSuccess: true,
                OverrideReason: null,
                EconomicImpact: null,
                ImpactAttribution: null,
                OccurredAtUtc: evt.OccurredAtUtc), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "GovernanceEventSubscriber: failed to auto-emit proof for workflow.completed {EventId}",
                evt.Id);
        }
    }

    private async Task HandleWorkflowFailedAsync(SystemEvent evt, CancellationToken ct)
    {
        try
        {
            if (!Guid.TryParse(evt.Payload.GetValueOrDefault("workflowId"), out var workflowId))
                return;
            if (!Guid.TryParse(evt.Payload.GetValueOrDefault("tenantId"), out var tenantId))
                return;

            var decisionId = Guid.TryParse(evt.Payload.GetValueOrDefault("decisionId"), out var did)
                ? did : workflowId;

            await _proofAnalytics.RecordEventAsync(new ProofEvent(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                DecisionId: decisionId,
                WorkflowId: workflowId,
                EventType: ProofEventType.ActionExecuted,
                Actor: evt.Payload.GetValueOrDefault("actor") ?? "system",
                Detail: $"Workflow failed: {evt.Payload.GetValueOrDefault("reason")}",
                ExpectedValue: null,
                ActualValue: null,
                Variance: null,
                VariancePercent: null,
                ActionType: "workflow.failed",
                IsSuccess: false,
                OverrideReason: null,
                EconomicImpact: null,
                ImpactAttribution: null,
                OccurredAtUtc: evt.OccurredAtUtc), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "GovernanceEventSubscriber: failed to auto-emit proof for workflow.failed {EventId}",
                evt.Id);
        }
    }

    private async Task HandleActionExecutedAsync(SystemEvent evt, CancellationToken ct)
    {
        try
        {
            if (!Guid.TryParse(evt.Payload.GetValueOrDefault("tenantId"), out var tenantId))
                return;

            var decisionId = Guid.TryParse(evt.Payload.GetValueOrDefault("decisionId"), out var did)
                ? did : evt.CorrelationId;
            var success = bool.TryParse(evt.Payload.GetValueOrDefault("success"), out var s) && s;
            var actionType = evt.Payload.GetValueOrDefault("actionType") ?? "gated-action";

            await _proofAnalytics.RecordEventAsync(new ProofEvent(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                DecisionId: decisionId,
                WorkflowId: null,
                EventType: success ? ProofEventType.ActionExecuted : ProofEventType.ReversalApplied,
                Actor: evt.Payload.GetValueOrDefault("actor") ?? "system",
                Detail: evt.Payload.GetValueOrDefault("detail") ?? "Gated action executed",
                ExpectedValue: null,
                ActualValue: null,
                Variance: null,
                VariancePercent: null,
                ActionType: actionType,
                IsSuccess: success,
                OverrideReason: null,
                EconomicImpact: null,
                ImpactAttribution: null,
                OccurredAtUtc: evt.OccurredAtUtc), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "GovernanceEventSubscriber: failed to auto-emit proof for gated-action {EventId}",
                evt.Id);
        }
    }
}
