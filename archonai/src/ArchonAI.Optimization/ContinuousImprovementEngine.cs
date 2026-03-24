using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Optimization;
using ArchonAI.Core.Models.Trace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Optimization;

/// <summary>
/// Continuous improvement loop that analyzes performance metrics, detects
/// inefficiencies across agents/models/tasks/costs, tracks trends over time,
/// and generates prioritized system improvement recommendations.
/// </summary>
public sealed class ContinuousImprovementEngine : IContinuousImprovementEngine
{
    private readonly IPerformanceAnalyzer _performanceAnalyzer;
    private readonly IModelPerformanceTracker _modelTracker;
    private readonly IPlanningFeedbackStore _feedbackStore;
    private readonly IEventBus _eventBus;
    private readonly ITraceStore _traceStore;
    private readonly OptimizationOptions _options;
    private readonly ILogger<ContinuousImprovementEngine> _logger;

    // Trend tracking: metric key -> time series of values
    private readonly ConcurrentDictionary<string, List<PerformanceTrendPoint>> _trendData = new(StringComparer.OrdinalIgnoreCase);

    // Cycle history
    private readonly List<ContinuousImprovementReport> _cycleHistory = [];
    private readonly object _historyLock = new();
    private int _cycleNumber;
    private double _previousHealthScore = 1.0;

    public ContinuousImprovementEngine(
        IPerformanceAnalyzer performanceAnalyzer,
        IModelPerformanceTracker modelTracker,
        IPlanningFeedbackStore feedbackStore,
        IEventBus eventBus,
        ITraceStore traceStore,
        IOptions<OptimizationOptions> options,
        ILogger<ContinuousImprovementEngine> logger)
    {
        _performanceAnalyzer = performanceAnalyzer;
        _modelTracker = modelTracker;
        _feedbackStore = feedbackStore;
        _eventBus = eventBus;
        _traceStore = traceStore;
        _options = options.Value;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Full improvement cycle
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<ContinuousImprovementReport> RunCycleAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int cycle = Interlocked.Increment(ref _cycleNumber);

        // Step 1: Analyze current performance
        var perfReport = await _performanceAnalyzer.AnalyzeAsync(cancellationToken);

        // Step 2: Record trend data points
        RecordTrendPoints(perfReport);

        // Step 3: Detect inefficiencies
        var inefficiencies = await DetectInefficienciesFromReport(perfReport, cancellationToken);

        // Step 4: Generate recommendations
        var recommendations = await RecommendImprovementsAsync(inefficiencies, cancellationToken);

        // Step 5: Apply high-priority improvements if auto-apply is enabled
        var appliedActions = new List<ImprovementAction>();
        if (_options.AutoApplyImprovements)
        {
            var urgentRecs = recommendations
                .Where(r => r.Priority is ImprovementPriority.Urgent or ImprovementPriority.High)
                .ToList();

            foreach (var rec in urgentRecs)
            {
                var action = new ImprovementAction(
                    Id: Guid.NewGuid(),
                    Category: rec.Category,
                    Target: rec.Target,
                    Action: rec.ProposedAction,
                    Parameters: rec.Parameters,
                    Applied: true,
                    CreatedAtUtc: DateTimeOffset.UtcNow,
                    AppliedAtUtc: DateTimeOffset.UtcNow);

                appliedActions.Add(action);

                await _feedbackStore.AddAsync(new PlanningFeedback(
                    Strategy: rec.Category,
                    Capability: rec.ProposedAction,
                    WasSuccessful: true,
                    Rationale: $"[continuous-improvement:cycle-{cycle}] {rec.Title}: {rec.Description}",
                    RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
            }

            if (appliedActions.Count > 0)
            {
                await _performanceAnalyzer.ApplyImprovementsAsync(appliedActions, cancellationToken);
            }
        }

        // Step 6: Compute trends
        var trends = await GetTrendsAsync(cancellationToken);

        // Step 7: Build report
        double healthTrend = perfReport.OverallHealthScore - _previousHealthScore;

        var report = new ContinuousImprovementReport(
            ReportId: Guid.NewGuid(),
            CycleNumber: cycle,
            OverallHealthScore: perfReport.OverallHealthScore,
            PreviousHealthScore: _previousHealthScore,
            HealthTrend: Math.Round(healthTrend, 4),
            Inefficiencies: inefficiencies,
            Recommendations: recommendations,
            Trends: trends,
            ActionsApplied: appliedActions,
            TotalInefficienciesDetected: inefficiencies.Count,
            CriticalInefficiencies: inefficiencies.Count(i => i.Severity == InefficiencySeverity.Critical),
            RecommendationsGenerated: recommendations.Count,
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        _previousHealthScore = perfReport.OverallHealthScore;

        // Store history
        lock (_historyLock)
        {
            _cycleHistory.Add(report);
            // Keep last 50 cycles
            if (_cycleHistory.Count > 50)
                _cycleHistory.RemoveAt(0);
        }

        // Publish event
        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: "optimization.continuous-improvement.cycle-completed",
            Source: nameof(ContinuousImprovementEngine),
            CorrelationId: report.ReportId,
            Payload: new Dictionary<string, string>
            {
                ["reportId"] = report.ReportId.ToString(),
                ["cycle"] = cycle.ToString(),
                ["healthScore"] = perfReport.OverallHealthScore.ToString("F4"),
                ["healthTrend"] = healthTrend.ToString("F4"),
                ["inefficiencies"] = inefficiencies.Count.ToString(),
                ["critical"] = report.CriticalInefficiencies.ToString(),
                ["recommendations"] = recommendations.Count.ToString(),
                ["applied"] = appliedActions.Count.ToString()
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        await _traceStore.RecordAsync(new TraceEntry(
            Id: Guid.NewGuid(),
            Scope: "optimization",
            Category: "continuous-improvement-cycle",
            Message: $"Cycle {cycle}: health={perfReport.OverallHealthScore:F3} (trend={healthTrend:+0.000;-0.000}), " +
                     $"{inefficiencies.Count} inefficiencies ({report.CriticalInefficiencies} critical), " +
                     $"{recommendations.Count} recommendations, {appliedActions.Count} applied",
            Metadata: new Dictionary<string, string>
            {
                ["cycle"] = cycle.ToString(),
                ["healthScore"] = perfReport.OverallHealthScore.ToString("F4")
            },
            RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        _logger.LogInformation(
            "Continuous improvement cycle {Cycle}: health={Health:F3} (trend={Trend:+0.000;-0.000}), " +
            "{Inefficiencies} inefficiencies, {Recs} recommendations, {Applied} applied",
            cycle, perfReport.OverallHealthScore, healthTrend,
            inefficiencies.Count, recommendations.Count, appliedActions.Count);

        return report;
    }

    // ══════════════════════════════════════════════════════════════
    //  Inefficiency detection
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<IReadOnlyList<DetectedInefficiency>> DetectInefficienciesAsync(
        CancellationToken cancellationToken = default)
    {
        var report = await _performanceAnalyzer.AnalyzeAsync(cancellationToken);
        return await DetectInefficienciesFromReport(report, cancellationToken);
    }

    private global::System.Threading.Tasks.Task<IReadOnlyList<DetectedInefficiency>> DetectInefficienciesFromReport(
        PerformanceReport report,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inefficiencies = new List<DetectedInefficiency>();

        // ── Agent bottlenecks ────────────────────────────────────
        foreach (var agent in report.AgentEfficiency)
        {
            int total = agent.TasksCompleted + agent.TasksFailed;
            if (total < _options.MinSamplesForAnalysis) continue;

            if (agent.SuccessRate < _options.AgentSuccessRateThreshold)
            {
                double deviation = ((_options.AgentSuccessRateThreshold - agent.SuccessRate) / _options.AgentSuccessRateThreshold) * 100;
                inefficiencies.Add(new DetectedInefficiency(
                    InefficiencyId: Guid.NewGuid(),
                    Type: InefficiencyType.AgentBottleneck,
                    Severity: agent.SuccessRate < 0.3 ? InefficiencySeverity.Critical
                        : agent.SuccessRate < 0.5 ? InefficiencySeverity.High
                        : InefficiencySeverity.Medium,
                    Target: agent.AgentName,
                    Description: $"Agent '{agent.AgentName}' has {agent.SuccessRate:P0} success rate ({agent.TasksFailed} failures out of {total})",
                    CurrentValue: agent.SuccessRate,
                    ThresholdValue: _options.AgentSuccessRateThreshold,
                    DeviationPercent: Math.Round(deviation, 2),
                    Evidence: new Dictionary<string, string>
                    {
                        ["agentId"] = agent.AgentId.ToString(),
                        ["completed"] = agent.TasksCompleted.ToString(),
                        ["failed"] = agent.TasksFailed.ToString(),
                        ["efficiencyScore"] = agent.EfficiencyScore.ToString("F4"),
                        ["avgExecutionTimeMs"] = agent.AverageExecutionTimeMs.ToString("F1")
                    },
                    DetectedAtUtc: DateTimeOffset.UtcNow));
            }

            // Latency regression: agents taking >5x average
            double avgLatencyAll = report.AgentEfficiency
                .Where(a => (a.TasksCompleted + a.TasksFailed) >= _options.MinSamplesForAnalysis)
                .Select(a => a.AverageExecutionTimeMs)
                .DefaultIfEmpty(5000)
                .Average();

            if (agent.AverageExecutionTimeMs > avgLatencyAll * 3 && agent.AverageExecutionTimeMs > 10_000)
            {
                inefficiencies.Add(new DetectedInefficiency(
                    InefficiencyId: Guid.NewGuid(),
                    Type: InefficiencyType.LatencyRegression,
                    Severity: agent.AverageExecutionTimeMs > avgLatencyAll * 5
                        ? InefficiencySeverity.High : InefficiencySeverity.Medium,
                    Target: agent.AgentName,
                    Description: $"Agent '{agent.AgentName}' avg latency {agent.AverageExecutionTimeMs:F0}ms is {agent.AverageExecutionTimeMs / avgLatencyAll:F1}x the system average",
                    CurrentValue: agent.AverageExecutionTimeMs,
                    ThresholdValue: avgLatencyAll * 3,
                    DeviationPercent: Math.Round(((agent.AverageExecutionTimeMs - avgLatencyAll) / avgLatencyAll) * 100, 2),
                    Evidence: new Dictionary<string, string>
                    {
                        ["agentId"] = agent.AgentId.ToString(),
                        ["systemAvgLatencyMs"] = avgLatencyAll.ToString("F1")
                    },
                    DetectedAtUtc: DateTimeOffset.UtcNow));
            }
        }

        // ── Model degradation ────────────────────────────────────
        var modelScores = _modelTracker.GetAllScores();
        foreach (var model in modelScores)
        {
            if (model.SampleCount < _options.MinSamplesForAnalysis) continue;

            if (model.SuccessRate < _options.ModelSuccessRateThreshold)
            {
                double deviation = ((_options.ModelSuccessRateThreshold - model.SuccessRate) / _options.ModelSuccessRateThreshold) * 100;
                inefficiencies.Add(new DetectedInefficiency(
                    InefficiencyId: Guid.NewGuid(),
                    Type: InefficiencyType.ModelDegradation,
                    Severity: model.SuccessRate < 0.4 ? InefficiencySeverity.Critical
                        : model.SuccessRate < 0.6 ? InefficiencySeverity.High
                        : InefficiencySeverity.Medium,
                    Target: $"{model.Provider}/{model.Model}",
                    Description: $"Model '{model.Provider}/{model.Model}' has {model.SuccessRate:P0} success rate with {model.AccuracyRate:P0} accuracy",
                    CurrentValue: model.SuccessRate,
                    ThresholdValue: _options.ModelSuccessRateThreshold,
                    DeviationPercent: Math.Round(deviation, 2),
                    Evidence: new Dictionary<string, string>
                    {
                        ["provider"] = model.Provider,
                        ["model"] = model.Model,
                        ["sampleCount"] = model.SampleCount.ToString(),
                        ["accuracyRate"] = model.AccuracyRate.ToString("F4"),
                        ["compositeScore"] = model.CompositeScore.ToString("F4"),
                        ["avgLatencyMs"] = model.AverageLatencyMs.ToString("F1"),
                        ["avgCost"] = model.AverageCostPerRequest.ToString("F6")
                    },
                    DetectedAtUtc: DateTimeOffset.UtcNow));
            }

            // Cost overrun: model costing >3x average
            double avgCostAll = modelScores
                .Where(m => m.SampleCount >= _options.MinSamplesForAnalysis)
                .Select(m => m.AverageCostPerRequest)
                .DefaultIfEmpty(0.01)
                .Average();

            if (model.AverageCostPerRequest > avgCostAll * 3 && model.AverageCostPerRequest > 0.01)
            {
                inefficiencies.Add(new DetectedInefficiency(
                    InefficiencyId: Guid.NewGuid(),
                    Type: InefficiencyType.CostOverrun,
                    Severity: model.AverageCostPerRequest > avgCostAll * 5
                        ? InefficiencySeverity.High : InefficiencySeverity.Medium,
                    Target: $"{model.Provider}/{model.Model}",
                    Description: $"Model '{model.Provider}/{model.Model}' costs ${model.AverageCostPerRequest:F4}/request, " +
                                 $"{model.AverageCostPerRequest / avgCostAll:F1}x the average",
                    CurrentValue: model.AverageCostPerRequest,
                    ThresholdValue: avgCostAll * 3,
                    DeviationPercent: Math.Round(((model.AverageCostPerRequest - avgCostAll) / avgCostAll) * 100, 2),
                    Evidence: new Dictionary<string, string>
                    {
                        ["provider"] = model.Provider,
                        ["model"] = model.Model,
                        ["systemAvgCost"] = avgCostAll.ToString("F6")
                    },
                    DetectedAtUtc: DateTimeOffset.UtcNow));
            }
        }

        // ── Task failure spikes ──────────────────────────────────
        foreach (var task in report.TaskCompletion)
        {
            if (task.TotalTasks < _options.MinSamplesForAnalysis) continue;

            if (task.CompletionRate < _options.TaskCompletionRateThreshold)
            {
                double deviation = ((_options.TaskCompletionRateThreshold - task.CompletionRate) / _options.TaskCompletionRateThreshold) * 100;
                inefficiencies.Add(new DetectedInefficiency(
                    InefficiencyId: Guid.NewGuid(),
                    Type: InefficiencyType.TaskFailureSpike,
                    Severity: task.CompletionRate < 0.4 ? InefficiencySeverity.Critical
                        : task.CompletionRate < 0.55 ? InefficiencySeverity.High
                        : InefficiencySeverity.Medium,
                    Target: task.TaskType,
                    Description: $"Task type '{task.TaskType}' has {task.CompletionRate:P0} completion rate ({task.Failed} failures out of {task.TotalTasks})",
                    CurrentValue: task.CompletionRate,
                    ThresholdValue: _options.TaskCompletionRateThreshold,
                    DeviationPercent: Math.Round(deviation, 2),
                    Evidence: new Dictionary<string, string>
                    {
                        ["totalTasks"] = task.TotalTasks.ToString(),
                        ["completed"] = task.Completed.ToString(),
                        ["failed"] = task.Failed.ToString(),
                        ["avgExecutionTimeMs"] = task.AverageExecutionTimeMs.ToString("F1"),
                        ["avgCost"] = task.AverageCost.ToString("F4")
                    },
                    DetectedAtUtc: DateTimeOffset.UtcNow));
            }
        }

        // ── Throughput decline (cross-cycle trend) ───────────────
        if (_trendData.TryGetValue("overall_health", out var healthTrend) && healthTrend.Count >= 3)
        {
            var recent = healthTrend.TakeLast(3).ToList();
            bool declining = recent.Count >= 3
                && recent[^1].Value < recent[0].Value
                && recent[^1].Value < recent[^2].Value;

            if (declining && recent[^1].Value < 0.6)
            {
                inefficiencies.Add(new DetectedInefficiency(
                    InefficiencyId: Guid.NewGuid(),
                    Type: InefficiencyType.ThroughputDecline,
                    Severity: recent[^1].Value < 0.4 ? InefficiencySeverity.Critical : InefficiencySeverity.High,
                    Target: "system",
                    Description: $"System health declining over last 3 cycles: {recent[0].Value:F3} → {recent[^1].Value:F3}",
                    CurrentValue: recent[^1].Value,
                    ThresholdValue: 0.6,
                    DeviationPercent: Math.Round(((0.6 - recent[^1].Value) / 0.6) * 100, 2),
                    Evidence: new Dictionary<string, string>
                    {
                        ["cycle_minus_2"] = recent[0].Value.ToString("F4"),
                        ["cycle_minus_1"] = recent[^2].Value.ToString("F4"),
                        ["current"] = recent[^1].Value.ToString("F4")
                    },
                    DetectedAtUtc: DateTimeOffset.UtcNow));
            }
        }

        // ── Cascading failures: multiple agents + tasks failing ──
        int failingAgents = report.AgentEfficiency
            .Count(a => a.SuccessRate < _options.AgentSuccessRateThreshold
                && (a.TasksCompleted + a.TasksFailed) >= _options.MinSamplesForAnalysis);
        int failingTasks = report.TaskCompletion
            .Count(t => t.CompletionRate < _options.TaskCompletionRateThreshold
                && t.TotalTasks >= _options.MinSamplesForAnalysis);

        if (failingAgents >= 3 && failingTasks >= 2)
        {
            inefficiencies.Add(new DetectedInefficiency(
                InefficiencyId: Guid.NewGuid(),
                Type: InefficiencyType.CascadingFailure,
                Severity: InefficiencySeverity.Critical,
                Target: "system",
                Description: $"Cascading failure pattern: {failingAgents} agents and {failingTasks} task types simultaneously underperforming",
                CurrentValue: report.OverallHealthScore,
                ThresholdValue: 0.6,
                DeviationPercent: Math.Round(((0.6 - report.OverallHealthScore) / 0.6) * 100, 2),
                Evidence: new Dictionary<string, string>
                {
                    ["failingAgents"] = failingAgents.ToString(),
                    ["failingTasks"] = failingTasks.ToString(),
                    ["overallHealthScore"] = report.OverallHealthScore.ToString("F4")
                },
                DetectedAtUtc: DateTimeOffset.UtcNow));
        }

        IReadOnlyList<DetectedInefficiency> result = inefficiencies
            .OrderByDescending(i => i.Severity)
            .ThenByDescending(i => i.DeviationPercent)
            .ToList();

        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    // ══════════════════════════════════════════════════════════════
    //  Recommendation generation
    // ══════════════════════════════════════════════════════════════

    public global::System.Threading.Tasks.Task<IReadOnlyList<SystemImprovementRecommendation>> RecommendImprovementsAsync(
        IReadOnlyList<DetectedInefficiency> inefficiencies,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var recommendations = new List<SystemImprovementRecommendation>();

        // Group by target to consolidate recommendations
        var byTarget = inefficiencies
            .GroupBy(i => i.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in byTarget)
        {
            var issues = group.ToList();
            var worstSeverity = issues.Max(i => i.Severity);
            var relatedIds = issues.Select(i => i.InefficiencyId).ToList();

            // Determine recommendation based on issue types present
            var issueTypes = issues.Select(i => i.Type).Distinct().ToHashSet();

            if (issueTypes.Contains(InefficiencyType.CascadingFailure))
            {
                recommendations.Add(CreateRecommendation(
                    priority: ImprovementPriority.Urgent,
                    category: "system-stability",
                    target: group.Key,
                    title: "Address cascading failure pattern",
                    description: "Multiple agents and task types are failing simultaneously, indicating a systemic issue. " +
                                 "Check shared dependencies, resource exhaustion, or upstream service degradation.",
                    action: "system-diagnostic",
                    impact: 0.9,
                    confidence: 0.85,
                    relatedIds: relatedIds,
                    parameters: issues[0].Evidence));
                continue;
            }

            if (issueTypes.Contains(InefficiencyType.AgentBottleneck))
            {
                var agentIssue = issues.First(i => i.Type == InefficiencyType.AgentBottleneck);
                bool severe = agentIssue.CurrentValue < 0.3;

                recommendations.Add(CreateRecommendation(
                    priority: severe ? ImprovementPriority.High : ImprovementPriority.Medium,
                    category: "agent-efficiency",
                    target: group.Key,
                    title: severe
                        ? $"Replace or retrain agent '{group.Key}'"
                        : $"Optimize agent '{group.Key}' configuration",
                    description: $"Agent has {agentIssue.CurrentValue:P0} success rate, {agentIssue.DeviationPercent:F0}% below threshold. " +
                                 (severe ? "Consider replacing with a more capable agent or retraining."
                                     : "Adjust task routing to reduce load, or tune agent parameters."),
                    action: severe ? "replace-agent" : "optimize-agent-config",
                    impact: Math.Min(1.0, agentIssue.DeviationPercent / 100.0),
                    confidence: Math.Min(1.0, int.Parse(agentIssue.Evidence.GetValueOrDefault("completed", "0"))
                        + int.Parse(agentIssue.Evidence.GetValueOrDefault("failed", "0"))) / 20.0,
                    relatedIds: relatedIds,
                    parameters: agentIssue.Evidence));
            }

            if (issueTypes.Contains(InefficiencyType.ModelDegradation))
            {
                var modelIssue = issues.First(i => i.Type == InefficiencyType.ModelDegradation);

                recommendations.Add(CreateRecommendation(
                    priority: modelIssue.Severity == InefficiencySeverity.Critical
                        ? ImprovementPriority.High : ImprovementPriority.Medium,
                    category: "model-routing",
                    target: group.Key,
                    title: $"Deprioritize model '{group.Key}'",
                    description: $"Model has {modelIssue.CurrentValue:P0} success rate with " +
                                 $"{modelIssue.Evidence.GetValueOrDefault("accuracyRate", "N/A")} accuracy. " +
                                 "Reduce routing weight and redirect traffic to better-performing models.",
                    action: "deprioritize-model",
                    impact: Math.Min(1.0, modelIssue.DeviationPercent / 100.0),
                    confidence: 0.8,
                    relatedIds: relatedIds,
                    parameters: modelIssue.Evidence));
            }

            if (issueTypes.Contains(InefficiencyType.CostOverrun))
            {
                var costIssue = issues.First(i => i.Type == InefficiencyType.CostOverrun);

                recommendations.Add(CreateRecommendation(
                    priority: ImprovementPriority.Medium,
                    category: "cost-optimization",
                    target: group.Key,
                    title: $"Reduce cost for '{group.Key}'",
                    description: $"Cost is ${costIssue.CurrentValue:F4}/request, {costIssue.DeviationPercent:F0}% above system average. " +
                                 "Route lower-priority requests to cheaper alternatives.",
                    action: "route-to-cheaper-model",
                    impact: Math.Min(1.0, costIssue.DeviationPercent / 200.0),
                    confidence: 0.75,
                    relatedIds: relatedIds,
                    parameters: costIssue.Evidence));
            }

            if (issueTypes.Contains(InefficiencyType.TaskFailureSpike))
            {
                var taskIssue = issues.First(i => i.Type == InefficiencyType.TaskFailureSpike);

                recommendations.Add(CreateRecommendation(
                    priority: taskIssue.Severity == InefficiencySeverity.Critical
                        ? ImprovementPriority.High : ImprovementPriority.Medium,
                    category: "task-design",
                    target: group.Key,
                    title: $"Restructure task type '{group.Key}'",
                    description: $"Task type has {taskIssue.CurrentValue:P0} completion rate. " +
                                 "Break into smaller subtasks, add retry logic, or reassign to more capable agents.",
                    action: "restructure-task",
                    impact: Math.Min(1.0, taskIssue.DeviationPercent / 100.0),
                    confidence: 0.7,
                    relatedIds: relatedIds,
                    parameters: taskIssue.Evidence));
            }

            if (issueTypes.Contains(InefficiencyType.LatencyRegression))
            {
                var latencyIssue = issues.First(i => i.Type == InefficiencyType.LatencyRegression);

                recommendations.Add(CreateRecommendation(
                    priority: ImprovementPriority.Medium,
                    category: "latency-optimization",
                    target: group.Key,
                    title: $"Reduce latency for '{group.Key}'",
                    description: $"Average latency is {latencyIssue.CurrentValue:F0}ms, significantly above system average. " +
                                 "Check for resource contention, optimize processing, or add caching.",
                    action: "optimize-latency",
                    impact: Math.Min(1.0, latencyIssue.DeviationPercent / 200.0),
                    confidence: 0.7,
                    relatedIds: relatedIds,
                    parameters: latencyIssue.Evidence));
            }

            if (issueTypes.Contains(InefficiencyType.ThroughputDecline))
            {
                var throughputIssue = issues.First(i => i.Type == InefficiencyType.ThroughputDecline);

                recommendations.Add(CreateRecommendation(
                    priority: ImprovementPriority.High,
                    category: "system-health",
                    target: group.Key,
                    title: "Address declining system health trend",
                    description: $"System health has declined over consecutive cycles to {throughputIssue.CurrentValue:F3}. " +
                                 "Review recent changes, scale resources, or roll back problematic deployments.",
                    action: "system-health-review",
                    impact: 0.8,
                    confidence: 0.85,
                    relatedIds: relatedIds,
                    parameters: throughputIssue.Evidence));
            }
        }

        IReadOnlyList<SystemImprovementRecommendation> result = recommendations
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.EstimatedImpact)
            .ToList();

        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    // ══════════════════════════════════════════════════════════════
    //  Trend tracking
    // ══════════════════════════════════════════════════════════════

    public global::System.Threading.Tasks.Task<IReadOnlyList<PerformanceTrend>> GetTrendsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var trends = new List<PerformanceTrend>();
        var now = DateTimeOffset.UtcNow;

        foreach (var (metricKey, dataPoints) in _trendData)
        {
            if (dataPoints.Count < 2) continue;

            var parts = metricKey.Split("::", 2);
            string metricName = parts[0];
            string target = parts.Length > 1 ? parts[1] : "system";

            var latest = dataPoints[^1];
            var previous = dataPoints[^2];
            double change = previous.Value > 0.0001
                ? ((latest.Value - previous.Value) / previous.Value) * 100
                : 0;

            string direction = change > 1 ? "improving" : change < -1 ? "declining" : "stable";

            trends.Add(new PerformanceTrend(
                MetricName: metricName,
                Target: target,
                DataPoints: dataPoints.ToList(),
                CurrentValue: Math.Round(latest.Value, 4),
                PreviousValue: Math.Round(previous.Value, 4),
                ChangePercent: Math.Round(change, 2),
                Direction: direction,
                AnalyzedAtUtc: now));
        }

        IReadOnlyList<PerformanceTrend> result = trends
            .OrderByDescending(t => Math.Abs(t.ChangePercent))
            .ToList();

        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public IReadOnlyList<ContinuousImprovementReport> GetCycleHistory()
    {
        lock (_historyLock)
        {
            return _cycleHistory.ToList();
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Internals
    // ══════════════════════════════════════════════════════════════

    private void RecordTrendPoints(PerformanceReport report)
    {
        var now = DateTimeOffset.UtcNow;

        RecordTrendPoint("overall_health::system", report.OverallHealthScore, now);

        foreach (var agent in report.AgentEfficiency)
        {
            RecordTrendPoint($"agent_success_rate::{agent.AgentName}", agent.SuccessRate, now);
            RecordTrendPoint($"agent_efficiency::{agent.AgentName}", agent.EfficiencyScore, now);
            RecordTrendPoint($"agent_latency::{agent.AgentName}", agent.AverageExecutionTimeMs, now);
        }

        foreach (var model in report.ModelAccuracy)
        {
            RecordTrendPoint($"model_success_rate::{model.Provider}/{model.Model}", model.SuccessRate, now);
            RecordTrendPoint($"model_composite::{model.Provider}/{model.Model}", model.CompositeScore, now);
        }

        foreach (var task in report.TaskCompletion)
        {
            RecordTrendPoint($"task_completion_rate::{task.TaskType}", task.CompletionRate, now);
        }
    }

    private void RecordTrendPoint(string key, double value, DateTimeOffset timestamp)
    {
        var points = _trendData.GetOrAdd(key, _ => new List<PerformanceTrendPoint>());
        lock (points)
        {
            points.Add(new PerformanceTrendPoint(value, timestamp));
            // Keep last 100 data points
            if (points.Count > 100)
                points.RemoveAt(0);
        }
    }

    private static SystemImprovementRecommendation CreateRecommendation(
        ImprovementPriority priority,
        string category,
        string target,
        string title,
        string description,
        string action,
        double impact,
        double confidence,
        IReadOnlyList<Guid> relatedIds,
        IReadOnlyDictionary<string, string> parameters)
    {
        return new SystemImprovementRecommendation(
            RecommendationId: Guid.NewGuid(),
            Priority: priority,
            Category: category,
            Target: target,
            Title: title,
            Description: description,
            ProposedAction: action,
            EstimatedImpact: Math.Round(Math.Clamp(impact, 0, 1), 4),
            Confidence: Math.Round(Math.Clamp(confidence, 0, 1), 4),
            RelatedInefficiencies: relatedIds,
            Parameters: parameters,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }
}
