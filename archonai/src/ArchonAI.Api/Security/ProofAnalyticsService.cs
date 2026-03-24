using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.ProofAnalytics;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Api.Security;

public sealed class ProofAnalyticsService : IProofAnalyticsService
{
    private readonly ConcurrentDictionary<Guid, ConcurrentBag<ProofEvent>> _eventsByDecision = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentBag<Guid>> _decisionsByWorkflow = new();
    private readonly IDecisionService _decisions;
    private readonly IOutcomeLearningService _outcomes;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ProofAnalyticsService> _logger;

    public ProofAnalyticsService(
        IDecisionService decisions,
        IOutcomeLearningService outcomes,
        IEventBus eventBus,
        ILogger<ProofAnalyticsService> logger)
    {
        _decisions = decisions;
        _outcomes = outcomes;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<ProofEvent> RecordEventAsync(ProofEvent proofEvent, CancellationToken ct = default)
    {
        var bag = _eventsByDecision.GetOrAdd(proofEvent.DecisionId, _ => new ConcurrentBag<ProofEvent>());
        bag.Add(proofEvent);

        if (proofEvent.WorkflowId.HasValue)
        {
            var wfBag = _decisionsByWorkflow.GetOrAdd(proofEvent.WorkflowId.Value, _ => new ConcurrentBag<Guid>());
            if (!wfBag.Contains(proofEvent.DecisionId))
                wfBag.Add(proofEvent.DecisionId);
        }

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(), "proof.event.recorded", "ProofAnalyticsService",
            proofEvent.DecisionId,
            new Dictionary<string, string>
            {
                ["decisionId"] = proofEvent.DecisionId.ToString(),
                ["tenantId"] = proofEvent.TenantId.ToString(),
                ["eventType"] = proofEvent.EventType.ToString(),
            }.AsReadOnly(),
            proofEvent.OccurredAtUtc), ct);

        _logger.LogInformation(
            "Proof event {EventType} recorded for decision {DecisionId}",
            proofEvent.EventType, proofEvent.DecisionId);

        return proofEvent;
    }

    public async Task<ProofTimeline?> GetTimelineAsync(Guid decisionId, CancellationToken ct = default)
    {
        var decision = await _decisions.GetAsync(decisionId, ct);
        if (decision is null) return null;

        var events = GetEventsForDecision(decisionId);
        var outcome = await _outcomes.GetOutcomeAsync(decisionId, ct);
        var summary = BuildTimelineSummary(events, decision, outcome);

        return new ProofTimeline(
            decisionId, decision.TenantId, decision.Title,
            decision.Domain, events, summary);
    }

    public async Task<IReadOnlyList<ProofTimeline>> GetWorkflowTimelinesAsync(Guid workflowId, CancellationToken ct = default)
    {
        if (!_decisionsByWorkflow.TryGetValue(workflowId, out var decisionIds))
            return Array.Empty<ProofTimeline>();

        var timelines = new List<ProofTimeline>();
        foreach (var did in decisionIds.Distinct())
        {
            var tl = await GetTimelineAsync(did, ct);
            if (tl is not null) timelines.Add(tl);
        }
        return timelines;
    }

    public async Task<PredictedVsActualSummary> GetPredictedVsActualAsync(
        Guid tenantId, string? domain = null, int limit = 50, CancellationToken ct = default)
    {
        var decisions = await _decisions.ListAsync(tenantId, domain, null, limit, ct);
        var entries = new List<PredictedVsActualEntry>();

        foreach (var d in decisions)
        {
            var outcome = await _outcomes.GetOutcomeAsync(d.Id, ct);
            entries.Add(new PredictedVsActualEntry(
                d.Id, d.Title, d.Domain,
                outcome?.ExpectedValue ?? d.ExpectedValue,
                outcome?.ActualValue,
                outcome?.ValueVariance,
                outcome?.VariancePercent,
                outcome?.Direction.ToString() ?? "Pending",
                d.CreatedAtUtc,
                outcome?.OutcomeObservedAtUtc));
        }

        var withOutcomes = entries.Where(e => e.ActualValue.HasValue).ToList();
        var onTarget = withOutcomes.Count(e => e.Direction == "OnTarget");
        var over = withOutcomes.Count(e => e.Direction == "Overperformed");
        var under = withOutcomes.Count(e => e.Direction == "Underperformed");

        var varianceValues = withOutcomes
            .Where(e => e.VariancePercent.HasValue)
            .Select(e => e.VariancePercent!.Value)
            .OrderBy(v => v)
            .ToList();

        var meanVariance = varianceValues.Count > 0 ? varianceValues.Average() : 0;
        var medianVariance = varianceValues.Count > 0
            ? varianceValues[varianceValues.Count / 2] : 0;

        var totalPredicted = withOutcomes.Sum(e => e.PredictedValue ?? 0);
        var totalActual = withOutcomes.Sum(e => e.ActualValue ?? 0);
        var accuracyRate = withOutcomes.Count > 0
            ? (double)(onTarget + over) / withOutcomes.Count : 0;

        return new PredictedVsActualSummary(
            tenantId, domain, entries.Count, withOutcomes.Count,
            onTarget, over, under,
            meanVariance, medianVariance,
            totalPredicted, totalActual, totalActual - totalPredicted,
            accuracyRate, entries);
    }

    public Task<ApprovalConversionSummary> GetApprovalConversionAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var allEvents = GetAllEventsForTenant(tenantId);

        var approvalRequests = allEvents.Where(e => e.EventType == ProofEventType.ApprovalRequested).ToList();
        var granted = allEvents.Where(e => e.EventType == ProofEventType.ApprovalGranted).ToList();
        var denied = allEvents.Where(e => e.EventType == ProofEventType.ApprovalDenied).ToList();
        var executed = allEvents.Where(e => e.EventType == ProofEventType.ActionExecuted).ToList();

        // decisions that got granted AND then executed
        var grantedDecisionIds = granted.Select(e => e.DecisionId).ToHashSet();
        var executedAfterApproval = executed.Count(e => grantedDecisionIds.Contains(e.DecisionId));
        var pendingExecution = grantedDecisionIds.Count - executedAfterApproval;

        var approvalRate = approvalRequests.Count > 0
            ? (double)granted.Count / approvalRequests.Count : 0;
        var executionRate = granted.Count > 0
            ? (double)executedAfterApproval / granted.Count : 0;

        // Mean approval latency
        TimeSpan? meanLatency = null;
        var latencies = new List<TimeSpan>();
        foreach (var req in approvalRequests)
        {
            var grant = granted.FirstOrDefault(g => g.DecisionId == req.DecisionId);
            if (grant is not null)
                latencies.Add(grant.OccurredAtUtc - req.OccurredAtUtc);
        }
        if (latencies.Count > 0)
            meanLatency = TimeSpan.FromTicks((long)latencies.Average(l => l.Ticks));

        // By action type
        var byType = allEvents
            .Where(e => e.ActionType is not null)
            .GroupBy(e => e.ActionType!)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var reqCount = g.Count(e => e.EventType == ProofEventType.ApprovalRequested);
                    var grantCount = g.Count(e => e.EventType == ProofEventType.ApprovalGranted);
                    var denyCount = g.Count(e => e.EventType == ProofEventType.ApprovalDenied);
                    var execCount = g.Count(e => e.EventType == ProofEventType.ActionExecuted);
                    return new ApprovalConversionByType(
                        g.Key, reqCount, grantCount, denyCount, execCount,
                        reqCount > 0 ? (double)grantCount / reqCount : 0,
                        grantCount > 0 ? (double)execCount / grantCount : 0);
                }) as IReadOnlyDictionary<string, ApprovalConversionByType>;

        return Task.FromResult(new ApprovalConversionSummary(
            tenantId, approvalRequests.Count, granted.Count, denied.Count,
            executedAfterApproval, Math.Max(0, pendingExecution),
            approvalRate, executionRate, meanLatency, byType));
    }

    public Task<ExecutionTrendSummary> GetExecutionTrendsAsync(
        Guid tenantId, int bucketCount = 10, CancellationToken ct = default)
    {
        var executions = GetAllEventsForTenant(tenantId)
            .Where(e => e.EventType == ProofEventType.ActionExecuted)
            .OrderBy(e => e.OccurredAtUtc)
            .ToList();

        var total = executions.Count;
        var successes = executions.Count(e => e.IsSuccess == true);
        var failures = executions.Count(e => e.IsSuccess == false);
        var successRate = total > 0 ? (double)successes / total : 0;

        var buckets = new List<ExecutionTrendBucket>();
        if (executions.Count > 0)
        {
            var earliest = executions[0].OccurredAtUtc;
            var latest = executions[^1].OccurredAtUtc;
            var span = latest - earliest;
            var bucketSize = span.Ticks > 0
                ? TimeSpan.FromTicks(span.Ticks / Math.Max(1, bucketCount))
                : TimeSpan.FromHours(1);

            for (int i = 0; i < bucketCount; i++)
            {
                var start = earliest + TimeSpan.FromTicks(bucketSize.Ticks * i);
                var end = earliest + TimeSpan.FromTicks(bucketSize.Ticks * (i + 1));
                var inBucket = executions.Where(e => e.OccurredAtUtc >= start && e.OccurredAtUtc < end).ToList();
                var bSuccesses = inBucket.Count(e => e.IsSuccess == true);
                var bFailures = inBucket.Count(e => e.IsSuccess == false);
                buckets.Add(new ExecutionTrendBucket(
                    start, end, inBucket.Count, bSuccesses, bFailures,
                    inBucket.Count > 0 ? (double)bSuccesses / inBucket.Count : 0));
            }
        }

        return Task.FromResult(new ExecutionTrendSummary(
            tenantId, total, successes, failures, successRate, buckets));
    }

    public Task<OverrideRateSummary> GetOverrideRatesAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var allEvents = GetAllEventsForTenant(tenantId);
        var decisionIds = allEvents.Select(e => e.DecisionId).Distinct().ToList();
        var overrides = allEvents.Where(e => e.EventType == ProofEventType.OverrideApplied).ToList();
        var reversals = allEvents.Where(e => e.EventType == ProofEventType.ReversalApplied).ToList();

        var overrideRate = decisionIds.Count > 0
            ? (double)overrides.Select(e => e.DecisionId).Distinct().Count() / decisionIds.Count : 0;
        var reversalRate = decisionIds.Count > 0
            ? (double)reversals.Select(e => e.DecisionId).Distinct().Count() / decisionIds.Count : 0;

        var reasonDist = overrides
            .Where(o => o.OverrideReason is not null)
            .GroupBy(o => o.OverrideReason!)
            .ToDictionary(g => g.Key, g => g.Count())
            as IReadOnlyDictionary<string, int>;

        return Task.FromResult(new OverrideRateSummary(
            tenantId, decisionIds.Count, overrides.Count, reversals.Count,
            overrideRate, reversalRate, reasonDist));
    }

    public async Task<TrustAnalyticsSummary> GetTrustAnalyticsAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var allEvents = GetAllEventsForTenant(tenantId);
        var actionTypes = allEvents
            .Where(e => e.ActionType is not null)
            .Select(e => e.ActionType!)
            .Distinct()
            .ToList();

        var byType = new List<TrustByActionType>();
        foreach (var actionType in actionTypes)
        {
            var typeEvents = allEvents.Where(e => e.ActionType == actionType).ToList();
            var decisionIds = typeEvents.Select(e => e.DecisionId).Distinct().ToList();
            var withOutcomes = 0;
            var accurateCount = 0;
            var varianceValues = new List<double>();
            var confidences = new List<double>();

            foreach (var did in decisionIds)
            {
                var outcome = await _outcomes.GetOutcomeAsync(did, ct);
                if (outcome is not null && outcome.Direction != OutcomeDirection.Pending)
                {
                    withOutcomes++;
                    if (outcome.Direction is OutcomeDirection.OnTarget or OutcomeDirection.Overperformed)
                        accurateCount++;
                    if (outcome.VariancePercent.HasValue)
                        varianceValues.Add(outcome.VariancePercent.Value);
                    confidences.Add(outcome.ConfidenceAtPrediction);
                }
            }

            var overrideCount = typeEvents.Count(e => e.EventType == ProofEventType.OverrideApplied);
            var accuracyRate = withOutcomes > 0 ? (double)accurateCount / withOutcomes : 0;
            var overrideRate = decisionIds.Count > 0 ? (double)overrideCount / decisionIds.Count : 0;
            var meanConf = confidences.Count > 0 ? confidences.Average() : 0;
            var meanVar = varianceValues.Count > 0 ? varianceValues.Average() : 0;

            byType.Add(new TrustByActionType(
                actionType, decisionIds.Count, withOutcomes,
                accuracyRate, overrideRate, meanConf,
                Math.Abs(meanVar), GradeTrust(accuracyRate, overrideRate)));
        }

        return new TrustAnalyticsSummary(tenantId, byType);
    }

    public async Task<ProofDashboard> GetDashboardAsync(
        Guid tenantId, string? domain = null, CancellationToken ct = default)
    {
        var pva = await GetPredictedVsActualAsync(tenantId, domain, 100, ct);
        var approval = await GetApprovalConversionAsync(tenantId, ct);
        var trends = await GetExecutionTrendsAsync(tenantId, 10, ct);
        var overrides = await GetOverrideRatesAsync(tenantId, ct);
        var trust = await GetTrustAnalyticsAsync(tenantId, ct);

        return new ProofDashboard(
            tenantId, pva, approval, trends, overrides, trust,
            DateTimeOffset.UtcNow);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private IReadOnlyList<ProofEvent> GetEventsForDecision(Guid decisionId)
    {
        if (!_eventsByDecision.TryGetValue(decisionId, out var bag))
            return Array.Empty<ProofEvent>();
        return bag.OrderBy(e => e.OccurredAtUtc).ToList();
    }

    private IReadOnlyList<ProofEvent> GetAllEventsForTenant(Guid tenantId)
    {
        return _eventsByDecision.Values
            .SelectMany(bag => bag)
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.OccurredAtUtc)
            .ToList();
    }

    private static ProofTimelineSummary BuildTimelineSummary(
        IReadOnlyList<ProofEvent> events,
        DecisionRecord decision,
        OutcomeRecord? outcome)
    {
        var hasOutcome = outcome is not null && outcome.Direction != OutcomeDirection.Pending;
        var wasOverridden = events.Any(e => e.EventType == ProofEventType.OverrideApplied);
        var wasReversed = events.Any(e => e.EventType == ProofEventType.ReversalApplied);

        TimeSpan? duration = null;
        if (hasOutcome && outcome!.OutcomeObservedAtUtc.HasValue)
            duration = outcome.OutcomeObservedAtUtc.Value - decision.CreatedAtUtc;

        var finalAssessment = hasOutcome ? outcome!.Assessment.ToString() : null;

        return new ProofTimelineSummary(
            events.Count, hasOutcome, wasOverridden, wasReversed,
            outcome?.ExpectedValue ?? decision.ExpectedValue,
            outcome?.ActualValue,
            outcome?.ValueVariance,
            outcome?.VariancePercent,
            finalAssessment, duration);
    }

    private static string GradeTrust(double accuracyRate, double overrideRate)
    {
        if (accuracyRate >= 0.9 && overrideRate <= 0.05) return "A";
        if (accuracyRate >= 0.75 && overrideRate <= 0.15) return "B";
        if (accuracyRate >= 0.6 && overrideRate <= 0.25) return "C";
        if (accuracyRate >= 0.4) return "D";
        return "F";
    }
}
