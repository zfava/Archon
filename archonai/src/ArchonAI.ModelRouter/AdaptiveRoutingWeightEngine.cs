using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.ModelRouter;

/// <summary>
/// Periodically analyzes model performance scores and adjusts routing weights
/// so that higher-performing models receive more traffic. Generates per-task-type
/// weights when task-type outcome data is available.
/// </summary>
public sealed class AdaptiveRoutingWeightEngine
{
    private readonly IModelPerformanceTracker _tracker;
    private readonly IEventBus _eventBus;
    private readonly ModelRouterOptions _options;
    private readonly ILogger<AdaptiveRoutingWeightEngine> _logger;

    private const double MinWeight = 0.05;
    private const double MaxWeight = 1.0;
    private const double PromotionStep = 0.10;
    private const double DemotionStep = 0.15;
    private const double RecoveryStep = 0.05;

    public AdaptiveRoutingWeightEngine(
        IModelPerformanceTracker tracker,
        IEventBus eventBus,
        IOptions<ModelRouterOptions> options,
        ILogger<AdaptiveRoutingWeightEngine> logger)
    {
        _tracker = tracker;
        _eventBus = eventBus;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Analyze all model performance scores and recompute routing weights.
    /// </summary>
    public async global::System.Threading.Tasks.Task<RoutingWeightReport> AdjustWeightsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var allScores = _tracker.GetAllScores();
        var eligible = allScores
            .Where(s => s.SampleCount >= _options.MinSamplesForAdaptive)
            .ToList();

        if (eligible.Count == 0)
        {
            _logger.LogInformation("No models with sufficient samples for weight adjustment");
            return EmptyReport();
        }

        var adjustments = new List<WeightAdjustmentRecord>();

        // ── Global weight adjustments ────────────────────────────
        var globalWeights = AdjustGlobalWeights(eligible, adjustments);

        // ── Task-type weight adjustments ─────────────────────────
        var taskTypeWeights = AdjustTaskTypeWeights(eligible, adjustments);

        // Determine top/bottom
        string topModel = globalWeights.Count > 0
            ? globalWeights.OrderByDescending(w => w.Weight).First().Model
            : "unknown";
        string bottomModel = globalWeights.Count > 0
            ? globalWeights.OrderBy(w => w.Weight).First().Model
            : "unknown";

        var report = new RoutingWeightReport(
            ReportId: Guid.NewGuid(),
            ModelsEvaluated: eligible.Count,
            GlobalWeights: globalWeights,
            TaskTypeWeights: taskTypeWeights,
            Adjustments: adjustments,
            TopModel: topModel,
            BottomModel: bottomModel,
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        // Publish event
        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: "model-router.weights.adjusted",
            Source: nameof(AdaptiveRoutingWeightEngine),
            CorrelationId: report.ReportId,
            Payload: new Dictionary<string, string>
            {
                ["reportId"] = report.ReportId.ToString(),
                ["modelsEvaluated"] = eligible.Count.ToString(),
                ["adjustments"] = adjustments.Count.ToString(),
                ["topModel"] = topModel,
                ["bottomModel"] = bottomModel
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        _logger.LogInformation(
            "Routing weights adjusted: {Models} models, {Adjustments} adjustments, top={Top}, bottom={Bottom}",
            eligible.Count, adjustments.Count, topModel, bottomModel);

        return report;
    }

    // ══════════════════════════════════════════════════════════════
    //  Global weights
    // ══════════════════════════════════════════════════════════════

    private List<ModelRoutingWeight> AdjustGlobalWeights(
        IReadOnlyList<ModelPerformanceScore> scores,
        List<WeightAdjustmentRecord> adjustments)
    {
        var results = new List<ModelRoutingWeight>();

        // Compute relative performance rankings
        double maxComposite = scores.Max(s => s.CompositeScore);
        double minComposite = scores.Min(s => s.CompositeScore);
        double range = maxComposite - minComposite;

        foreach (var score in scores)
        {
            var currentWeight = _tracker.GetRoutingWeight(score.Provider, score.Model);
            double oldWeight = currentWeight?.Weight ?? 0.5;
            double newWeight = oldWeight;
            string reason;

            // Relative position in [0, 1]
            double relativePosition = range > 0.001
                ? (score.CompositeScore - minComposite) / range
                : 0.5;

            if (score.SuccessRate < 0.3)
            {
                // Severe failure — aggressive demotion
                newWeight = Math.Max(MinWeight, oldWeight - DemotionStep * 2);
                reason = $"severe-failure:success={score.SuccessRate:F2}";
            }
            else if (score.SuccessRate < 0.6)
            {
                // Poor success rate — demote
                newWeight = Math.Max(MinWeight, oldWeight - DemotionStep);
                reason = $"poor-success:success={score.SuccessRate:F2}";
            }
            else if (score.SuccessRate >= 0.9 && relativePosition >= 0.7)
            {
                // Top performer — promote
                newWeight = Math.Min(MaxWeight, oldWeight + PromotionStep);
                reason = $"top-performer:success={score.SuccessRate:F2},rank={relativePosition:F2}";
            }
            else if (score.SuccessRate >= 0.7 && relativePosition < 0.3)
            {
                // Decent success but relatively worse — slight demotion
                newWeight = Math.Max(MinWeight, oldWeight - RecoveryStep);
                reason = $"underperforming-relative:success={score.SuccessRate:F2},rank={relativePosition:F2}";
            }
            else if (oldWeight < 0.5 && score.SuccessRate >= 0.8)
            {
                // Previously demoted but recovering — slow recovery
                newWeight = Math.Min(MaxWeight, oldWeight + RecoveryStep);
                reason = $"recovery:success={score.SuccessRate:F2},oldWeight={oldWeight:F2}";
            }
            else
            {
                // Stable — no change
                reason = $"stable:success={score.SuccessRate:F2},rank={relativePosition:F2}";
            }

            newWeight = Math.Round(Math.Clamp(newWeight, MinWeight, MaxWeight), 4);

            if (Math.Abs(newWeight - oldWeight) > 0.001)
            {
                _tracker.SetRoutingWeight(score.Provider, score.Model, newWeight, reason);
                adjustments.Add(new WeightAdjustmentRecord(
                    Provider: score.Provider,
                    Model: score.Model,
                    TaskType: null,
                    OldWeight: oldWeight,
                    NewWeight: newWeight,
                    Reason: reason,
                    AdjustedAtUtc: DateTimeOffset.UtcNow));
            }

            results.Add(new ModelRoutingWeight(
                Provider: score.Provider,
                Model: score.Model,
                Weight: newWeight,
                PreviousWeight: oldWeight,
                AdjustmentReason: reason,
                AdjustedAtUtc: DateTimeOffset.UtcNow));
        }

        return results;
    }

    // ══════════════════════════════════════════════════════════════
    //  Task-type weights
    // ══════════════════════════════════════════════════════════════

    private List<TaskTypeModelWeight> AdjustTaskTypeWeights(
        IReadOnlyList<ModelPerformanceScore> scores,
        List<WeightAdjustmentRecord> adjustments)
    {
        var results = new List<TaskTypeModelWeight>();

        foreach (var (taskType, _) in _options.TaskTypeModelMap)
        {
            var existingWeights = _tracker.GetTaskTypeWeights(taskType);

            // For each eligible model, compute a task-type-specific weight
            // using composite score weighted toward the strategy that matters for the task
            foreach (var score in scores)
            {
                var existing = existingWeights
                    .FirstOrDefault(w => w.Provider == score.Provider && w.Model == score.Model);
                double oldWeight = existing?.Weight ?? 0.5;

                // Task-type affinity: if the configured model for this task-type matches,
                // give a bonus (it was chosen for a reason)
                bool isConfiguredDefault = _options.TaskTypeModelMap.TryGetValue(taskType, out var configured)
                    && configured.Equals(score.Model, StringComparison.OrdinalIgnoreCase);
                double affinityBonus = isConfiguredDefault ? 0.1 : 0;

                // Weight based on composite + affinity
                double taskWeight = Math.Clamp(
                    (score.CompositeScore * 0.8) + (score.SuccessRate * 0.2) + affinityBonus,
                    MinWeight, MaxWeight);
                taskWeight = Math.Round(taskWeight, 4);

                if (Math.Abs(taskWeight - oldWeight) > 0.01)
                {
                    _tracker.SetTaskTypeWeight(taskType, score.Provider, score.Model, taskWeight);
                    adjustments.Add(new WeightAdjustmentRecord(
                        Provider: score.Provider,
                        Model: score.Model,
                        TaskType: taskType,
                        OldWeight: oldWeight,
                        NewWeight: taskWeight,
                        Reason: $"task-type-reweight:{taskType}:composite={score.CompositeScore:F3}",
                        AdjustedAtUtc: DateTimeOffset.UtcNow));
                }

                results.Add(new TaskTypeModelWeight(
                    TaskType: taskType,
                    Provider: score.Provider,
                    Model: score.Model,
                    Weight: taskWeight,
                    SuccessRate: score.SuccessRate,
                    AverageLatencyMs: score.AverageLatencyMs,
                    SampleCount: score.SampleCount,
                    AdjustedAtUtc: DateTimeOffset.UtcNow));
            }
        }

        return results;
    }

    private static RoutingWeightReport EmptyReport() => new(
        ReportId: Guid.NewGuid(),
        ModelsEvaluated: 0,
        GlobalWeights: [],
        TaskTypeWeights: [],
        Adjustments: [],
        TopModel: "unknown",
        BottomModel: "unknown",
        GeneratedAtUtc: DateTimeOffset.UtcNow);
}
