using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Evaluation;
using ArchonAI.Core.Models.Knowledge;
using ArchonAI.Core.Models.Learning;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Learning;

/// <summary>
/// Analyzes outcome evaluation history to improve future strategies.
/// Builds performance profiles for strategies and agent types, then generates
/// actionable recommendations for goal generation, strategy selection, and
/// agent assignment. Persists learned insights in the KnowledgeGraph.
/// </summary>
public sealed class StrategyLearningEngine : IStrategyLearningEngine
{
    private readonly IOutcomeEvaluator _outcomeEvaluator;
    private readonly IPlanningFeedbackStore _feedbackStore;
    private readonly IKnowledgeGraphStore _knowledgeStore;
    private readonly IEventBus _eventBus;
    private readonly ILogger<StrategyLearningEngine> _logger;

    // Known strategies and departments to scan
    private static readonly string[] KnownStrategies =
        ["balanced", "safe-mode", "throughput-optimized", "cost-optimized"];

    private static readonly string[] KnownDepartments =
        ["sales", "marketing", "operations", "finance", "logistics", "support"];

    public StrategyLearningEngine(
        IOutcomeEvaluator outcomeEvaluator,
        IPlanningFeedbackStore feedbackStore,
        IKnowledgeGraphStore knowledgeStore,
        IEventBus eventBus,
        ILogger<StrategyLearningEngine> logger)
    {
        _outcomeEvaluator = outcomeEvaluator;
        _feedbackStore = feedbackStore;
        _knowledgeStore = knowledgeStore;
        _eventBus = eventBus;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Full learning cycle
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<StrategyLearningReport> AnalyzeAndLearnAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Collect all evaluations across strategies
        var allEvaluations = new List<OutcomeEvaluationResult>();
        foreach (var strategy in KnownStrategies)
        {
            var evals = await _outcomeEvaluator.GetEvaluationsForStrategyAsync(strategy, cancellationToken);
            allEvaluations.AddRange(evals);
        }

        if (allEvaluations.Count == 0)
        {
            _logger.LogInformation("No outcome evaluations found — skipping learning cycle");
            return EmptyReport();
        }

        // Build profiles
        var strategyProfiles = BuildStrategyProfiles(allEvaluations);
        var agentTypeProfiles = BuildAgentTypeProfiles(allEvaluations);

        // Generate recommendations
        var strategyRecommendations = GenerateStrategyRecommendations(strategyProfiles);
        var goalAdjustments = GenerateGoalAdjustments(allEvaluations, strategyProfiles);
        var agentAdjustments = GenerateAgentAdjustments(agentTypeProfiles);

        // Determine best/worst
        string bestStrategy = strategyProfiles.Count > 0
            ? strategyProfiles.OrderByDescending(p => p.SuccessRate).First().Strategy
            : "balanced";
        string worstStrategy = strategyProfiles.Count > 0
            ? strategyProfiles.OrderBy(p => p.SuccessRate).First().Strategy
            : "unknown";

        var report = new StrategyLearningReport(
            ReportId: Guid.NewGuid(),
            EvaluationsAnalyzed: allEvaluations.Count,
            StrategyProfiles: strategyProfiles,
            AgentTypeProfiles: agentTypeProfiles,
            StrategyRecommendations: strategyRecommendations,
            GoalAdjustments: goalAdjustments,
            AgentAdjustments: agentAdjustments,
            BestOverallStrategy: bestStrategy,
            WorstOverallStrategy: worstStrategy,
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        // Persist insights
        await StoreReportInKnowledgeGraphAsync(report, cancellationToken);

        // Write planner feedback for each recommendation
        foreach (var rec in strategyRecommendations)
        {
            await _feedbackStore.AddAsync(new PlanningFeedback(
                Strategy: rec.Target,
                Capability: "strategy-learning",
                WasSuccessful: rec.Confidence >= 0.7,
                Rationale: $"[{rec.RecommendationType}] {rec.Recommendation} (confidence={rec.Confidence:F2})",
                RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
        }

        // Publish event
        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: "learning.strategy.report.generated",
            Source: nameof(StrategyLearningEngine),
            CorrelationId: report.ReportId,
            Payload: new Dictionary<string, string>
            {
                ["reportId"] = report.ReportId.ToString(),
                ["evaluationsAnalyzed"] = allEvaluations.Count.ToString(),
                ["strategyProfiles"] = strategyProfiles.Count.ToString(),
                ["agentTypeProfiles"] = agentTypeProfiles.Count.ToString(),
                ["recommendations"] = strategyRecommendations.Count.ToString(),
                ["goalAdjustments"] = goalAdjustments.Count.ToString(),
                ["agentAdjustments"] = agentAdjustments.Count.ToString(),
                ["bestStrategy"] = bestStrategy,
                ["worstStrategy"] = worstStrategy
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        _logger.LogInformation(
            "Strategy learning report generated: {Evals} evaluations, {Strategies} strategy profiles, " +
            "{AgentTypes} agent profiles, {Recs} recommendations, best={Best}, worst={Worst}",
            allEvaluations.Count, strategyProfiles.Count, agentTypeProfiles.Count,
            strategyRecommendations.Count, bestStrategy, worstStrategy);

        return report;
    }

    // ══════════════════════════════════════════════════════════════
    //  Profile queries
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<IReadOnlyList<StrategyPerformanceProfile>> GetStrategyProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var allEvaluations = new List<OutcomeEvaluationResult>();
        foreach (var strategy in KnownStrategies)
        {
            var evals = await _outcomeEvaluator.GetEvaluationsForStrategyAsync(strategy, cancellationToken);
            allEvaluations.AddRange(evals);
        }

        return BuildStrategyProfiles(allEvaluations);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<AgentTypePerformanceProfile>> GetAgentTypeProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var allEvaluations = new List<OutcomeEvaluationResult>();
        foreach (var strategy in KnownStrategies)
        {
            var evals = await _outcomeEvaluator.GetEvaluationsForStrategyAsync(strategy, cancellationToken);
            allEvaluations.AddRange(evals);
        }

        return BuildAgentTypeProfiles(allEvaluations);
    }

    public async global::System.Threading.Tasks.Task<string> RecommendStrategyAsync(
        string department,
        string priority,
        CancellationToken cancellationToken = default)
    {
        var profiles = await GetStrategyProfilesAsync(cancellationToken);

        if (profiles.Count == 0)
            return "balanced";

        // Find strategies with data for this department
        var candidates = profiles
            .Where(p => p.TotalExecutions >= 3)
            .Select(p =>
            {
                double baseScore = p.SuccessRate;

                // Boost score if we have department-specific data
                if (p.SuccessRateByDepartment.TryGetValue(department, out double deptRate))
                    baseScore = (baseScore * 0.4) + (deptRate * 0.6);

                // Boost score if we have priority-specific data
                if (p.SuccessRateByPriority.TryGetValue(priority, out double priorityRate))
                    baseScore = (baseScore * 0.7) + (priorityRate * 0.3);

                return (Profile: p, Score: baseScore);
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        if (candidates.Count == 0)
            return "balanced";

        return candidates[0].Profile.Strategy;
    }

    // ══════════════════════════════════════════════════════════════
    //  Profile builders
    // ══════════════════════════════════════════════════════════════

    private static IReadOnlyList<StrategyPerformanceProfile> BuildStrategyProfiles(
        IReadOnlyList<OutcomeEvaluationResult> evaluations)
    {
        return evaluations
            .GroupBy(e => e.Strategy, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var evals = group.ToList();
                int successes = evals.Count(e => e.Comparison.ActualSuccess);
                int failures = evals.Count - successes;
                double successRate = evals.Count > 0 ? successes / (double)evals.Count : 0;

                // Department breakdown — use KnowledgeGraph-stored goalId context
                // Since evaluations may lack department info directly, use GoalId grouping
                var deptRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var priorityRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                return new StrategyPerformanceProfile(
                    Strategy: group.Key,
                    TotalExecutions: evals.Count,
                    Successes: successes,
                    Failures: failures,
                    SuccessRate: Math.Round(successRate, 4),
                    AverageOverallScore: Math.Round(evals.Average(e => e.SuccessMetrics.OverallScore), 4),
                    AverageDurationAccuracy: Math.Round(evals.Average(e => e.SuccessMetrics.DurationAccuracy), 4),
                    AverageCostAccuracy: Math.Round(evals.Average(e => e.SuccessMetrics.CostAccuracy), 4),
                    AverageRiskPredictionAccuracy: Math.Round(evals.Average(e => e.SuccessMetrics.RiskPredictionAccuracy), 4),
                    SuccessRateByDepartment: deptRates,
                    SuccessRateByPriority: priorityRates,
                    LastUpdatedAtUtc: evals.Max(e => e.EvaluatedAtUtc));
            })
            .OrderByDescending(p => p.SuccessRate)
            .ToList();
    }

    private static IReadOnlyList<AgentTypePerformanceProfile> BuildAgentTypeProfiles(
        IReadOnlyList<OutcomeEvaluationResult> evaluations)
    {
        var allNodeEvals = evaluations
            .SelectMany(e => e.NodeEvaluations)
            .ToList();

        if (allNodeEvals.Count == 0)
            return [];

        return allNodeEvals
            .GroupBy(n => n.AgentType, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var nodes = group.ToList();
                int successes = nodes.Count(n => n.ActualSuccess);
                int failures = nodes.Count - successes;
                double successRate = nodes.Count > 0 ? successes / (double)nodes.Count : 0;

                int unexpectedFailures = nodes.Count(n =>
                    !n.ActualSuccess && n.PredictedSuccessProbability >= 0.8);
                int criticalPathFailures = nodes.Count(n =>
                    n.WasOnCriticalPath && !n.ActualSuccess);

                double avgDurationDev = nodes
                    .Where(n => n.PredictedDurationHours > 0)
                    .Select(n => ((n.ActualDurationHours - n.PredictedDurationHours) / n.PredictedDurationHours) * 100)
                    .DefaultIfEmpty(0)
                    .Average();

                double avgCostDev = nodes
                    .Where(n => n.PredictedCost > 0)
                    .Select(n => (double)((n.ActualCost - n.PredictedCost) / n.PredictedCost) * 100)
                    .DefaultIfEmpty(0)
                    .Average();

                return new AgentTypePerformanceProfile(
                    AgentType: group.Key,
                    TotalAssignments: nodes.Count,
                    Successes: successes,
                    Failures: failures,
                    SuccessRate: Math.Round(successRate, 4),
                    AverageDurationDeviationPercent: Math.Round(avgDurationDev, 2),
                    AverageCostDeviationPercent: Math.Round(avgCostDev, 2),
                    UnexpectedFailures: unexpectedFailures,
                    CriticalPathFailures: criticalPathFailures,
                    LastUpdatedAtUtc: DateTimeOffset.UtcNow);
            })
            .OrderByDescending(p => p.TotalAssignments)
            .ToList();
    }

    // ══════════════════════════════════════════════════════════════
    //  Recommendation generators
    // ══════════════════════════════════════════════════════════════

    private static IReadOnlyList<StrategyLearningRecommendation> GenerateStrategyRecommendations(
        IReadOnlyList<StrategyPerformanceProfile> profiles)
    {
        var recommendations = new List<StrategyLearningRecommendation>();

        foreach (var profile in profiles)
        {
            if (profile.TotalExecutions < 3) continue;

            // Low success rate
            if (profile.SuccessRate < 0.6)
            {
                recommendations.Add(new StrategyLearningRecommendation(
                    RecommendationType: "strategy_demotion",
                    Target: profile.Strategy,
                    Recommendation: $"Strategy '{profile.Strategy}' has a {profile.SuccessRate:P0} success rate " +
                                    $"across {profile.TotalExecutions} executions. Reduce usage priority or " +
                                    "investigate root cause of failures.",
                    Confidence: Math.Min(1.0, profile.TotalExecutions / 10.0),
                    Evidence: $"{profile.Failures} failures out of {profile.TotalExecutions} executions"));
            }

            // High success rate — promote
            if (profile.SuccessRate >= 0.9 && profile.TotalExecutions >= 5)
            {
                recommendations.Add(new StrategyLearningRecommendation(
                    RecommendationType: "strategy_promotion",
                    Target: profile.Strategy,
                    Recommendation: $"Strategy '{profile.Strategy}' is highly reliable ({profile.SuccessRate:P0} success). " +
                                    "Consider increasing usage for similar goal profiles.",
                    Confidence: Math.Min(1.0, profile.TotalExecutions / 10.0),
                    Evidence: $"{profile.Successes} successes out of {profile.TotalExecutions} executions"));
            }

            // Poor duration accuracy — needs calibration
            if (profile.AverageDurationAccuracy < 0.6)
            {
                recommendations.Add(new StrategyLearningRecommendation(
                    RecommendationType: "calibration_needed",
                    Target: profile.Strategy,
                    Recommendation: $"Duration predictions for '{profile.Strategy}' have low accuracy " +
                                    $"({profile.AverageDurationAccuracy:P0}). Recalibrate base duration estimates.",
                    Confidence: 0.8,
                    Evidence: $"Average duration accuracy: {profile.AverageDurationAccuracy:F3}"));
            }

            // Poor cost accuracy
            if (profile.AverageCostAccuracy < 0.6)
            {
                recommendations.Add(new StrategyLearningRecommendation(
                    RecommendationType: "calibration_needed",
                    Target: profile.Strategy,
                    Recommendation: $"Cost predictions for '{profile.Strategy}' have low accuracy " +
                                    $"({profile.AverageCostAccuracy:P0}). Revise cost-per-hour baselines.",
                    Confidence: 0.8,
                    Evidence: $"Average cost accuracy: {profile.AverageCostAccuracy:F3}"));
            }

            // Risk prediction inaccuracy
            if (profile.AverageRiskPredictionAccuracy < 0.5 && profile.TotalExecutions >= 5)
            {
                recommendations.Add(new StrategyLearningRecommendation(
                    RecommendationType: "risk_model_retune",
                    Target: profile.Strategy,
                    Recommendation: $"Risk predictions for '{profile.Strategy}' are inaccurate " +
                                    $"({profile.AverageRiskPredictionAccuracy:P0}). " +
                                    "Retune risk thresholds and scoring weights.",
                    Confidence: 0.9,
                    Evidence: $"Average risk prediction accuracy: {profile.AverageRiskPredictionAccuracy:F3}"));
            }
        }

        // Cross-strategy: suggest default strategy swap if current best isn't "balanced"
        if (profiles.Count >= 2)
        {
            var best = profiles.OrderByDescending(p => p.SuccessRate).First();
            var balanced = profiles.FirstOrDefault(p =>
                p.Strategy.Equals("balanced", StringComparison.OrdinalIgnoreCase));

            if (balanced is not null && best.Strategy != balanced.Strategy
                && best.SuccessRate > balanced.SuccessRate + 0.1
                && best.TotalExecutions >= 5)
            {
                recommendations.Add(new StrategyLearningRecommendation(
                    RecommendationType: "default_strategy_change",
                    Target: best.Strategy,
                    Recommendation: $"Strategy '{best.Strategy}' outperforms 'balanced' by " +
                                    $"{(best.SuccessRate - balanced.SuccessRate):P0}. " +
                                    "Consider making it the default strategy.",
                    Confidence: Math.Min(1.0, best.TotalExecutions / 10.0),
                    Evidence: $"'{best.Strategy}': {best.SuccessRate:P0} vs 'balanced': {balanced.SuccessRate:P0}"));
            }
        }

        return recommendations;
    }

    private static IReadOnlyList<GoalGenerationAdjustment> GenerateGoalAdjustments(
        IReadOnlyList<OutcomeEvaluationResult> evaluations,
        IReadOnlyList<StrategyPerformanceProfile> profiles)
    {
        var adjustments = new List<GoalGenerationAdjustment>();

        // Identify strategies that consistently fail — suggest avoiding auto-generated goals
        // that would use these strategies
        foreach (var profile in profiles.Where(p => p.TotalExecutions >= 5 && p.SuccessRate < 0.5))
        {
            adjustments.Add(new GoalGenerationAdjustment(
                AdjustmentType: "strategy_avoidance",
                Department: "all",
                CurrentBehaviour: $"Goals may be assigned '{profile.Strategy}' strategy",
                RecommendedBehaviour: $"Avoid '{profile.Strategy}' for auto-generated goals; prefer higher-performing alternatives",
                Reason: $"Strategy '{profile.Strategy}' has {profile.SuccessRate:P0} success rate across {profile.TotalExecutions} executions",
                Confidence: Math.Min(1.0, profile.TotalExecutions / 10.0)));
        }

        // Identify goals that consistently fail by overall assessment
        var poorEvaluations = evaluations.Where(e => e.OverallAssessment == "poor").ToList();
        if (poorEvaluations.Count >= 3)
        {
            var failedStrategies = poorEvaluations
                .GroupBy(e => e.Strategy, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .First();

            adjustments.Add(new GoalGenerationAdjustment(
                AdjustmentType: "goal_complexity_reduction",
                Department: "operations",
                CurrentBehaviour: "Goals generated with standard complexity",
                RecommendedBehaviour: "Break complex goals into smaller sub-goals to improve completion rates",
                Reason: $"{poorEvaluations.Count} evaluations scored 'poor', primarily with strategy '{failedStrategies.Key}'",
                Confidence: 0.7));
        }

        // Check if high-priority goals have lower success than medium-priority
        var highPriorityEvals = evaluations.Where(e =>
            e.SuccessMetrics.OverallScore < 0.5).ToList();
        var mediumPriorityEvals = evaluations.Where(e =>
            e.SuccessMetrics.OverallScore >= 0.5).ToList();

        if (highPriorityEvals.Count > 0 && mediumPriorityEvals.Count > 0)
        {
            double failRate = highPriorityEvals.Count / (double)evaluations.Count;
            if (failRate > 0.4)
            {
                adjustments.Add(new GoalGenerationAdjustment(
                    AdjustmentType: "priority_calibration",
                    Department: "all",
                    CurrentBehaviour: "Goals assigned priority based on signal severity",
                    RecommendedBehaviour: "Consider resource availability when assigning priority; " +
                                         "high-priority goals may need pre-validation",
                    Reason: $"{failRate:P0} of evaluations have poor outcomes — " +
                            "goals may be over-prioritized relative to system capacity",
                    Confidence: 0.6));
            }
        }

        // Deadline pressure analysis
        var overtimeEvals = evaluations.Where(e =>
            e.Comparison.DurationDeviationPercent > 50).ToList();
        if (overtimeEvals.Count >= 3)
        {
            adjustments.Add(new GoalGenerationAdjustment(
                AdjustmentType: "deadline_relaxation",
                Department: "all",
                CurrentBehaviour: "Goal deadlines set based on signal urgency",
                RecommendedBehaviour: "Increase deadline buffers by 30-50% — " +
                                     $"{overtimeEvals.Count} goals significantly exceeded their estimated duration",
                Reason: $"{overtimeEvals.Count} evaluations show >50% duration overrun",
                Confidence: 0.75));
        }

        return adjustments;
    }

    private static IReadOnlyList<AgentAssignmentAdjustment> GenerateAgentAdjustments(
        IReadOnlyList<AgentTypePerformanceProfile> agentProfiles)
    {
        var adjustments = new List<AgentAssignmentAdjustment>();

        foreach (var profile in agentProfiles)
        {
            if (profile.TotalAssignments < 3) continue;

            // Unreliable agent type
            if (profile.SuccessRate < 0.7)
            {
                adjustments.Add(new AgentAssignmentAdjustment(
                    AgentType: profile.AgentType,
                    RecommendedAction: "reduce_assignments",
                    Reason: $"Agent type '{profile.AgentType}' has {profile.SuccessRate:P0} success rate. " +
                            "Reduce assignment frequency or add retry/fallback logic.",
                    Confidence: Math.Min(1.0, profile.TotalAssignments / 10.0),
                    Evidence: new Dictionary<string, string>
                    {
                        ["totalAssignments"] = profile.TotalAssignments.ToString(),
                        ["failures"] = profile.Failures.ToString(),
                        ["successRate"] = profile.SuccessRate.ToString("F4")
                    }));
            }

            // High unexpected failure rate
            if (profile.UnexpectedFailures > 0 &&
                profile.UnexpectedFailures / (double)profile.TotalAssignments > 0.2)
            {
                adjustments.Add(new AgentAssignmentAdjustment(
                    AgentType: profile.AgentType,
                    RecommendedAction: "improve_prediction",
                    Reason: $"Agent type '{profile.AgentType}' has {profile.UnexpectedFailures} unexpected failures " +
                            $"({profile.UnexpectedFailures / (double)profile.TotalAssignments:P0} of assignments). " +
                            "Success probability model is too optimistic for this agent type.",
                    Confidence: 0.85,
                    Evidence: new Dictionary<string, string>
                    {
                        ["unexpectedFailures"] = profile.UnexpectedFailures.ToString(),
                        ["totalAssignments"] = profile.TotalAssignments.ToString()
                    }));
            }

            // Critical path failures
            if (profile.CriticalPathFailures > 0)
            {
                adjustments.Add(new AgentAssignmentAdjustment(
                    AgentType: profile.AgentType,
                    RecommendedAction: "avoid_critical_path",
                    Reason: $"Agent type '{profile.AgentType}' has {profile.CriticalPathFailures} critical-path failures. " +
                            "Avoid placing this agent type on critical path nodes.",
                    Confidence: 0.9,
                    Evidence: new Dictionary<string, string>
                    {
                        ["criticalPathFailures"] = profile.CriticalPathFailures.ToString(),
                        ["totalAssignments"] = profile.TotalAssignments.ToString()
                    }));
            }

            // Consistently slow — duration overrun
            if (Math.Abs(profile.AverageDurationDeviationPercent) > 40)
            {
                string direction = profile.AverageDurationDeviationPercent > 0 ? "slower" : "faster";
                adjustments.Add(new AgentAssignmentAdjustment(
                    AgentType: profile.AgentType,
                    RecommendedAction: "recalibrate_duration",
                    Reason: $"Agent type '{profile.AgentType}' runs {Math.Abs(profile.AverageDurationDeviationPercent):F0}% " +
                            $"{direction} than predicted. Update duration baselines.",
                    Confidence: 0.8,
                    Evidence: new Dictionary<string, string>
                    {
                        ["averageDurationDeviationPercent"] = profile.AverageDurationDeviationPercent.ToString("F2")
                    }));
            }

            // Consistently over budget
            if (profile.AverageCostDeviationPercent > 30)
            {
                adjustments.Add(new AgentAssignmentAdjustment(
                    AgentType: profile.AgentType,
                    RecommendedAction: "recalibrate_cost",
                    Reason: $"Agent type '{profile.AgentType}' costs {profile.AverageCostDeviationPercent:F0}% more than predicted. " +
                            "Revise cost-per-hour estimates.",
                    Confidence: 0.8,
                    Evidence: new Dictionary<string, string>
                    {
                        ["averageCostDeviationPercent"] = profile.AverageCostDeviationPercent.ToString("F2")
                    }));
            }

            // Highly reliable — recommend for critical work
            if (profile.SuccessRate >= 0.95 && profile.TotalAssignments >= 10)
            {
                adjustments.Add(new AgentAssignmentAdjustment(
                    AgentType: profile.AgentType,
                    RecommendedAction: "prefer_for_critical_path",
                    Reason: $"Agent type '{profile.AgentType}' is highly reliable ({profile.SuccessRate:P0} success rate " +
                            $"across {profile.TotalAssignments} assignments). Prefer for critical-path nodes.",
                    Confidence: Math.Min(1.0, profile.TotalAssignments / 20.0),
                    Evidence: new Dictionary<string, string>
                    {
                        ["successRate"] = profile.SuccessRate.ToString("F4"),
                        ["totalAssignments"] = profile.TotalAssignments.ToString()
                    }));
            }
        }

        return adjustments;
    }

    // ══════════════════════════════════════════════════════════════
    //  KnowledgeGraph persistence
    // ══════════════════════════════════════════════════════════════

    private async global::System.Threading.Tasks.Task StoreReportInKnowledgeGraphAsync(
        StrategyLearningReport report,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        string reportNodeId = $"learning_report:{report.ReportId}";

        // Store the report summary node
        await _knowledgeStore.UpsertNodeAsync(new KnowledgeNode(
            NodeId: reportNodeId,
            NodeType: "strategy_learning_report",
            DisplayName: $"Learning Report ({report.EvaluationsAnalyzed} evaluations)",
            Properties: new Dictionary<string, string>
            {
                ["reportId"] = report.ReportId.ToString(),
                ["evaluationsAnalyzed"] = report.EvaluationsAnalyzed.ToString(),
                ["strategyProfiles"] = report.StrategyProfiles.Count.ToString(),
                ["agentTypeProfiles"] = report.AgentTypeProfiles.Count.ToString(),
                ["recommendations"] = report.StrategyRecommendations.Count.ToString(),
                ["goalAdjustments"] = report.GoalAdjustments.Count.ToString(),
                ["agentAdjustments"] = report.AgentAdjustments.Count.ToString(),
                ["bestStrategy"] = report.BestOverallStrategy,
                ["worstStrategy"] = report.WorstOverallStrategy,
                ["generatedAtUtc"] = report.GeneratedAtUtc.ToString("O")
            },
            UpdatedAtUtc: now), cancellationToken);

        // Store strategy performance nodes and link to report
        foreach (var profile in report.StrategyProfiles)
        {
            string profileNodeId = $"strategy_profile:{profile.Strategy}";

            await _knowledgeStore.UpsertNodeAsync(new KnowledgeNode(
                NodeId: profileNodeId,
                NodeType: "strategy_performance_profile",
                DisplayName: $"Strategy: {profile.Strategy} ({profile.SuccessRate:P0})",
                Properties: new Dictionary<string, string>
                {
                    ["strategy"] = profile.Strategy,
                    ["totalExecutions"] = profile.TotalExecutions.ToString(),
                    ["successes"] = profile.Successes.ToString(),
                    ["failures"] = profile.Failures.ToString(),
                    ["successRate"] = profile.SuccessRate.ToString("F4"),
                    ["averageOverallScore"] = profile.AverageOverallScore.ToString("F4"),
                    ["avgDurationAccuracy"] = profile.AverageDurationAccuracy.ToString("F4"),
                    ["avgCostAccuracy"] = profile.AverageCostAccuracy.ToString("F4"),
                    ["avgRiskPredictionAccuracy"] = profile.AverageRiskPredictionAccuracy.ToString("F4")
                },
                UpdatedAtUtc: now), cancellationToken);

            await _knowledgeStore.UpsertRelationshipAsync(new KnowledgeRelationship(
                RelationshipId: $"report:{report.ReportId}->strategy:{profile.Strategy}",
                FromNodeId: reportNodeId,
                RelationshipType: "analyzed_strategy",
                ToNodeId: profileNodeId,
                Properties: new Dictionary<string, string>
                {
                    ["successRate"] = profile.SuccessRate.ToString("F4")
                },
                UpdatedAtUtc: now), cancellationToken);
        }

        // Store agent type performance nodes
        foreach (var profile in report.AgentTypeProfiles)
        {
            string agentProfileNodeId = $"agent_type_profile:{profile.AgentType}";

            await _knowledgeStore.UpsertNodeAsync(new KnowledgeNode(
                NodeId: agentProfileNodeId,
                NodeType: "agent_type_performance_profile",
                DisplayName: $"Agent: {profile.AgentType} ({profile.SuccessRate:P0})",
                Properties: new Dictionary<string, string>
                {
                    ["agentType"] = profile.AgentType,
                    ["totalAssignments"] = profile.TotalAssignments.ToString(),
                    ["successRate"] = profile.SuccessRate.ToString("F4"),
                    ["unexpectedFailures"] = profile.UnexpectedFailures.ToString(),
                    ["criticalPathFailures"] = profile.CriticalPathFailures.ToString(),
                    ["avgDurationDeviation"] = profile.AverageDurationDeviationPercent.ToString("F2"),
                    ["avgCostDeviation"] = profile.AverageCostDeviationPercent.ToString("F2")
                },
                UpdatedAtUtc: now), cancellationToken);

            await _knowledgeStore.UpsertRelationshipAsync(new KnowledgeRelationship(
                RelationshipId: $"report:{report.ReportId}->agent_type:{profile.AgentType}",
                FromNodeId: reportNodeId,
                RelationshipType: "analyzed_agent_type",
                ToNodeId: agentProfileNodeId,
                Properties: new Dictionary<string, string>
                {
                    ["successRate"] = profile.SuccessRate.ToString("F4")
                },
                UpdatedAtUtc: now), cancellationToken);
        }

        // Store recommendations
        for (int i = 0; i < report.StrategyRecommendations.Count; i++)
        {
            var rec = report.StrategyRecommendations[i];
            string recNodeId = $"learning_rec:{report.ReportId}:{i}";

            await _knowledgeStore.UpsertNodeAsync(new KnowledgeNode(
                NodeId: recNodeId,
                NodeType: "learning_recommendation",
                DisplayName: $"[{rec.RecommendationType}] {rec.Target}",
                Properties: new Dictionary<string, string>
                {
                    ["type"] = rec.RecommendationType,
                    ["target"] = rec.Target,
                    ["recommendation"] = rec.Recommendation,
                    ["confidence"] = rec.Confidence.ToString("F2"),
                    ["evidence"] = rec.Evidence
                },
                UpdatedAtUtc: now), cancellationToken);

            await _knowledgeStore.UpsertRelationshipAsync(new KnowledgeRelationship(
                RelationshipId: $"report:{report.ReportId}->rec:{i}",
                FromNodeId: reportNodeId,
                RelationshipType: "has_recommendation",
                ToNodeId: recNodeId,
                Properties: new Dictionary<string, string>
                {
                    ["type"] = rec.RecommendationType,
                    ["confidence"] = rec.Confidence.ToString("F2")
                },
                UpdatedAtUtc: now), cancellationToken);
        }
    }

    private static StrategyLearningReport EmptyReport() => new(
        ReportId: Guid.NewGuid(),
        EvaluationsAnalyzed: 0,
        StrategyProfiles: [],
        AgentTypeProfiles: [],
        StrategyRecommendations: [],
        GoalAdjustments: [],
        AgentAdjustments: [],
        BestOverallStrategy: "balanced",
        WorstOverallStrategy: "unknown",
        GeneratedAtUtc: DateTimeOffset.UtcNow);
}
