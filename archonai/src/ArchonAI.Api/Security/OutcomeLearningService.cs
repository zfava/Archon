using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Decisions;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Api.Security;

public sealed class OutcomeLearningService : IOutcomeLearningService
{
    // Keyed by DecisionId for fast lookup
    private readonly ConcurrentDictionary<Guid, OutcomeRecord> _outcomes = new();
    private readonly IDecisionService _decisionService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<OutcomeLearningService> _logger;

    public OutcomeLearningService(
        IDecisionService decisionService,
        IEventBus eventBus,
        ILogger<OutcomeLearningService> logger)
    {
        _decisionService = decisionService;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<OutcomeRecord> RecordExpectedOutcomeAsync(
        Guid decisionId, Guid tenantId,
        string? expectedSummary, decimal? expectedValue,
        double confidenceAtPrediction, string? expectedTimeframe,
        string recordedBy, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new OutcomeRecord(
            Id: Guid.NewGuid(),
            DecisionId: decisionId,
            TenantId: tenantId,
            ExpectedOutcomeSummary: expectedSummary,
            ExpectedValue: expectedValue,
            ConfidenceAtPrediction: confidenceAtPrediction,
            ExpectedTimeframe: expectedTimeframe,
            ActualOutcomeSummary: null,
            ActualValue: null,
            OutcomeObservedAtUtc: null,
            ValueVariance: null,
            VariancePercent: null,
            Direction: OutcomeDirection.Pending,
            RootCause: null,
            Notes: null,
            Assessment: OutcomeAssessment.Pending,
            RecalibrationSignal: RecalibrationSignal.None,
            RecordedBy: recordedBy,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);

        _outcomes[decisionId] = record;

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(), "outcome.expected.recorded", "OutcomeLearningService",
            decisionId,
            new Dictionary<string, string>
            {
                ["decisionId"] = decisionId.ToString(),
                ["tenantId"] = tenantId.ToString(),
                ["confidence"] = confidenceAtPrediction.ToString("F2"),
            }.AsReadOnly(),
            now), ct);

        _logger.LogInformation(
            "Expected outcome recorded for decision {DecisionId}, confidence={Confidence}",
            decisionId, confidenceAtPrediction);

        return record;
    }

    public async Task<OutcomeRecord> RecordActualOutcomeAsync(
        Guid decisionId, string? actualSummary, decimal? actualValue,
        string? rootCause, string? notes, string recordedBy,
        CancellationToken ct = default)
    {
        if (!_outcomes.TryGetValue(decisionId, out var existing))
            throw new KeyNotFoundException($"No expected outcome recorded for decision {decisionId}.");

        var now = DateTimeOffset.UtcNow;

        // Compute variance
        var (valueVariance, variancePercent) = ComputeVariance(existing.ExpectedValue, actualValue);
        var direction = DetermineDirection(existing.ExpectedValue, actualValue);
        var assessment = DetermineAssessment(direction, variancePercent);
        var signal = DetermineRecalibrationSignal(
            existing.ConfidenceAtPrediction, direction, variancePercent);

        var updated = existing with
        {
            ActualOutcomeSummary = actualSummary,
            ActualValue = actualValue,
            OutcomeObservedAtUtc = now,
            ValueVariance = valueVariance,
            VariancePercent = variancePercent,
            Direction = direction,
            RootCause = rootCause,
            Notes = notes,
            Assessment = assessment,
            RecalibrationSignal = signal,
            RecordedBy = recordedBy,
            UpdatedAtUtc = now,
        };

        _outcomes[decisionId] = updated;

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(), "outcome.actual.recorded", "OutcomeLearningService",
            decisionId,
            new Dictionary<string, string>
            {
                ["decisionId"] = decisionId.ToString(),
                ["tenantId"] = existing.TenantId.ToString(),
                ["direction"] = direction.ToString(),
                ["assessment"] = assessment.ToString(),
                ["signal"] = signal.ToString(),
                ["variancePercent"] = (variancePercent ?? 0).ToString("F1"),
            }.AsReadOnly(),
            now), ct);

        _logger.LogInformation(
            "Actual outcome recorded for decision {DecisionId}: direction={Direction} assessment={Assessment} signal={Signal}",
            decisionId, direction, assessment, signal);

        return updated;
    }

    public Task<OutcomeRecord?> GetOutcomeAsync(Guid decisionId, CancellationToken ct = default)
    {
        _outcomes.TryGetValue(decisionId, out var record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<OutcomeRecord>> ListOutcomesAsync(
        Guid tenantId, int limit = 50, CancellationToken ct = default)
    {
        IReadOnlyList<OutcomeRecord> result = _outcomes.Values
            .Where(o => o.TenantId == tenantId)
            .OrderByDescending(o => o.UpdatedAtUtc)
            .Take(limit)
            .ToList();
        return Task.FromResult(result);
    }

    public async Task<CalibrationSummary> GetCalibrationSummaryAsync(
        Guid tenantId, string? domain = null, CancellationToken ct = default)
    {
        var outcomes = _outcomes.Values
            .Where(o => o.TenantId == tenantId)
            .Where(o => o.Direction != OutcomeDirection.Pending)
            .ToList();

        // If domain filter specified, cross-reference decision records
        if (domain is not null)
        {
            var filtered = new List<OutcomeRecord>();
            foreach (var o in outcomes)
            {
                var decision = await _decisionService.GetAsync(o.DecisionId, ct);
                if (decision is not null && string.Equals(decision.Domain, domain, StringComparison.OrdinalIgnoreCase))
                    filtered.Add(o);
            }
            outcomes = filtered;
        }

        var total = outcomes.Count;
        var onTarget = outcomes.Count(o => o.Direction == OutcomeDirection.OnTarget);
        var over = outcomes.Count(o => o.Direction == OutcomeDirection.Overperformed);
        var under = outcomes.Count(o => o.Direction == OutcomeDirection.Underperformed);

        var meanConfidence = total > 0
            ? outcomes.Average(o => o.ConfidenceAtPrediction) : 0;
        var hitRate = total > 0
            ? (double)(onTarget + over) / total : 0;
        var meanVariance = total > 0
            ? outcomes.Where(o => o.VariancePercent.HasValue).Select(o => o.VariancePercent!.Value).DefaultIfEmpty(0).Average()
            : 0;

        var signalDist = outcomes
            .GroupBy(o => o.RecalibrationSignal.ToString())
            .ToDictionary(g => g.Key, g => g.Count())
            as IReadOnlyDictionary<string, int>;

        return new CalibrationSummary(
            tenantId.ToString(), domain, total,
            onTarget, over, under,
            meanConfidence, hitRate, meanVariance, signalDist);
    }

    // ── Variance Computation ──────────────────────────────────

    internal static (decimal? variance, double? variancePercent) ComputeVariance(
        decimal? expected, decimal? actual)
    {
        if (expected is null || actual is null)
            return (null, null);

        var variance = actual.Value - expected.Value;
        var pct = expected.Value != 0
            ? (double)(variance / expected.Value) * 100
            : (double?)null;

        return (variance, pct);
    }

    internal static OutcomeDirection DetermineDirection(decimal? expected, decimal? actual)
    {
        if (expected is null || actual is null)
            return OutcomeDirection.Pending;

        var variancePct = expected.Value != 0
            ? Math.Abs((double)((actual.Value - expected.Value) / expected.Value)) * 100
            : 0;

        // Within 10% is "on target"
        if (variancePct <= 10)
            return OutcomeDirection.OnTarget;

        return actual.Value > expected.Value
            ? OutcomeDirection.Overperformed
            : OutcomeDirection.Underperformed;
    }

    internal static OutcomeAssessment DetermineAssessment(
        OutcomeDirection direction, double? variancePercent)
    {
        if (direction == OutcomeDirection.Pending)
            return OutcomeAssessment.Pending;

        if (direction == OutcomeDirection.OnTarget)
            return OutcomeAssessment.AsExpected;

        var absVariance = Math.Abs(variancePercent ?? 0);
        if (absVariance > 50)
            return direction == OutcomeDirection.Overperformed
                ? OutcomeAssessment.BetterThanExpected
                : OutcomeAssessment.CompletelyMissed;

        return direction == OutcomeDirection.Overperformed
            ? OutcomeAssessment.BetterThanExpected
            : OutcomeAssessment.WorseThanExpected;
    }

    internal static RecalibrationSignal DetermineRecalibrationSignal(
        double confidenceAtPrediction, OutcomeDirection direction, double? variancePercent)
    {
        if (direction == OutcomeDirection.Pending)
            return RecalibrationSignal.None;

        if (direction == OutcomeDirection.OnTarget)
            return RecalibrationSignal.ConfidenceCalibrated;

        var absVariance = Math.Abs(variancePercent ?? 0);

        // High confidence + bad outcome = confidence was inflated
        if (confidenceAtPrediction >= 0.75 && direction == OutcomeDirection.Underperformed)
            return absVariance > 50
                ? RecalibrationSignal.AssumptionInvalid
                : RecalibrationSignal.ConfidenceInflated;

        // Low confidence + good outcome = confidence was deflated
        if (confidenceAtPrediction < 0.5 && direction == OutcomeDirection.Overperformed)
            return RecalibrationSignal.ConfidenceDeflated;

        // Large value misses regardless of confidence direction
        if (absVariance > 40)
            return RecalibrationSignal.ValueModelDrift;

        return RecalibrationSignal.None;
    }
}
