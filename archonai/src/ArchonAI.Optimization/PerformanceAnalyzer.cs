using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Optimization;
using ArchonAI.Core.Models.Models.Routing;
using ArchonAI.Core.Models.Trace;
using Microsoft.Extensions.Options;

namespace ArchonAI.Optimization;

public sealed class PerformanceAnalyzer : IPerformanceAnalyzer
{
    private readonly ConcurrentDictionary<Guid, AgentMetrics> _agentMetrics = new();
    private readonly ConcurrentDictionary<string, TaskTypeMetrics> _taskMetrics = new(StringComparer.OrdinalIgnoreCase);
    private readonly IModelPerformanceTracker _modelPerformanceTracker;
    private readonly IPlanningFeedbackStore _feedbackStore;
    private readonly ITraceStore _traceStore;
    private readonly OptimizationOptions _options;

    public PerformanceAnalyzer(
        IModelPerformanceTracker modelPerformanceTracker,
        IPlanningFeedbackStore feedbackStore,
        ITraceStore traceStore,
        IOptions<OptimizationOptions> options)
    {
        _modelPerformanceTracker = modelPerformanceTracker;
        _feedbackStore = feedbackStore;
        _traceStore = traceStore;
        _options = options.Value;
    }

    public void RecordAgentExecution(Guid agentId, string agentName, bool success, double executionTimeMs, decimal cost)
    {
        _agentMetrics.AddOrUpdate(
            agentId,
            _ => new AgentMetrics(agentId, agentName, success, executionTimeMs, cost),
            (_, existing) =>
            {
                existing.Record(success, executionTimeMs, cost);
                return existing;
            });
    }

    public void RecordTaskCompletion(string taskType, bool success, double executionTimeMs, decimal cost)
    {
        _taskMetrics.AddOrUpdate(
            taskType,
            _ => new TaskTypeMetrics(taskType, success, executionTimeMs, cost),
            (_, existing) =>
            {
                existing.Record(success, executionTimeMs, cost);
                return existing;
            });
    }

    public async global::System.Threading.Tasks.Task<PerformanceReport> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var agentRecords = _agentMetrics.Values
            .Select(m => m.ToRecord())
            .OrderByDescending(r => r.TasksCompleted + r.TasksFailed)
            .ToList();

        IReadOnlyList<ModelPerformanceScore> modelScores = _modelPerformanceTracker.GetAllScores();
        var modelRecords = modelScores
            .Select(s => new ModelAccuracyRecord(
                Provider: s.Provider,
                Model: s.Model,
                TotalRequests: s.SampleCount,
                SuccessRate: s.SuccessRate,
                AverageLatencyMs: s.AverageLatencyMs,
                AverageCost: s.AverageCostPerRequest,
                AccuracyRate: s.AccuracyRate,
                CompositeScore: s.CompositeScore))
            .OrderByDescending(r => r.TotalRequests)
            .ToList();

        var taskRecords = _taskMetrics.Values
            .Select(m => m.ToRecord())
            .OrderByDescending(r => r.TotalTasks)
            .ToList();

        var recommendations = GenerateRecommendations(agentRecords, modelRecords, taskRecords);

        double healthScore = ComputeOverallHealth(agentRecords, modelRecords, taskRecords);

        var report = new PerformanceReport(
            AgentEfficiency: agentRecords,
            ModelAccuracy: modelRecords,
            TaskCompletion: taskRecords,
            Recommendations: recommendations,
            OverallHealthScore: healthScore,
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        await TraceReportAsync(report);
        return report;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<ImprovementAction>> GenerateImprovementsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var report = await AnalyzeAsync(cancellationToken);
        var actions = new List<ImprovementAction>();
        var now = DateTimeOffset.UtcNow;

        // Agent improvements: reassign tasks from underperforming agents
        foreach (var agent in report.AgentEfficiency.Where(a => a.SuccessRate < _options.AgentSuccessRateThreshold && (a.TasksCompleted + a.TasksFailed) >= _options.MinSamplesForAnalysis))
        {
            actions.Add(new ImprovementAction(
                Id: Guid.NewGuid(),
                Category: "agent-efficiency",
                Target: agent.AgentId.ToString(),
                Action: "reduce-task-load",
                Parameters: new Dictionary<string, string>
                {
                    ["agentName"] = agent.AgentName,
                    ["currentSuccessRate"] = agent.SuccessRate.ToString("F3"),
                    ["threshold"] = _options.AgentSuccessRateThreshold.ToString("F3")
                },
                Applied: false,
                CreatedAtUtc: now,
                AppliedAtUtc: null));
        }

        // Model improvements: flag underperforming models for routing adjustment
        foreach (var model in report.ModelAccuracy.Where(m => m.SuccessRate < _options.ModelSuccessRateThreshold && m.TotalRequests >= _options.MinSamplesForAnalysis))
        {
            actions.Add(new ImprovementAction(
                Id: Guid.NewGuid(),
                Category: "model-accuracy",
                Target: $"{model.Provider}::{model.Model}",
                Action: "deprioritize-model",
                Parameters: new Dictionary<string, string>
                {
                    ["provider"] = model.Provider,
                    ["model"] = model.Model,
                    ["currentSuccessRate"] = model.SuccessRate.ToString("F3"),
                    ["currentAccuracy"] = model.AccuracyRate.ToString("F3")
                },
                Applied: false,
                CreatedAtUtc: now,
                AppliedAtUtc: null));
        }

        // Task type improvements: flag chronically failing task types
        foreach (var task in report.TaskCompletion.Where(t => t.CompletionRate < _options.TaskCompletionRateThreshold && t.TotalTasks >= _options.MinSamplesForAnalysis))
        {
            actions.Add(new ImprovementAction(
                Id: Guid.NewGuid(),
                Category: "task-completion",
                Target: task.TaskType,
                Action: "review-task-design",
                Parameters: new Dictionary<string, string>
                {
                    ["taskType"] = task.TaskType,
                    ["completionRate"] = task.CompletionRate.ToString("F3"),
                    ["averageLatencyMs"] = task.AverageExecutionTimeMs.ToString("F1")
                },
                Applied: false,
                CreatedAtUtc: now,
                AppliedAtUtc: null));
        }

        return actions;
    }

    public async global::System.Threading.Tasks.Task ApplyImprovementsAsync(
        IReadOnlyList<ImprovementAction> actions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var action in actions.Where(a => !a.Applied))
        {
            // Record each improvement as planning feedback so the system learns
            await _feedbackStore.AddAsync(
                new Core.Models.PlanningFeedback(
                    Strategy: action.Category,
                    Capability: action.Action,
                    WasSuccessful: true,
                    Rationale: $"Performance analyzer applied improvement: {action.Action} on {action.Target}",
                    RecordedAtUtc: DateTimeOffset.UtcNow),
                cancellationToken);

            await _traceStore.RecordAsync(new TraceEntry(
                Id: Guid.NewGuid(),
                Scope: "optimization",
                Category: "improvement-applied",
                Message: $"Applied {action.Action} on {action.Target} ({action.Category})",
                Metadata: action.Parameters,
                RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
        }
    }

    private List<PerformanceRecommendation> GenerateRecommendations(
        IReadOnlyList<AgentEfficiencyRecord> agents,
        IReadOnlyList<ModelAccuracyRecord> models,
        IReadOnlyList<TaskCompletionRecord> tasks)
    {
        var recommendations = new List<PerformanceRecommendation>();
        var now = DateTimeOffset.UtcNow;

        // Agent recommendations
        foreach (var agent in agents.Where(a => a.EfficiencyScore < 0.5 && (a.TasksCompleted + a.TasksFailed) >= _options.MinSamplesForAnalysis))
        {
            recommendations.Add(new PerformanceRecommendation(
                Category: "agent",
                Target: agent.AgentName,
                Action: agent.SuccessRate < 0.3 ? "consider-replacement" : "optimize-configuration",
                Reason: $"Agent efficiency score {agent.EfficiencyScore:F2} below threshold. Success rate: {agent.SuccessRate:P0}.",
                ExpectedImpact: 1.0 - agent.EfficiencyScore,
                GeneratedAtUtc: now));
        }

        // Model recommendations
        foreach (var model in models.Where(m => m.CompositeScore < 0.4 && m.TotalRequests >= _options.MinSamplesForAnalysis))
        {
            recommendations.Add(new PerformanceRecommendation(
                Category: "model",
                Target: $"{model.Provider}/{model.Model}",
                Action: "route-to-better-model",
                Reason: $"Model composite score {model.CompositeScore:F3} is low. Accuracy: {model.AccuracyRate:P0}.",
                ExpectedImpact: 0.4 - model.CompositeScore,
                GeneratedAtUtc: now));
        }

        // Task type recommendations
        foreach (var task in tasks.Where(t => t.CompletionRate < _options.TaskCompletionRateThreshold && t.TotalTasks >= _options.MinSamplesForAnalysis))
        {
            recommendations.Add(new PerformanceRecommendation(
                Category: "task",
                Target: task.TaskType,
                Action: "restructure-workflow",
                Reason: $"Task type '{task.TaskType}' completion rate {task.CompletionRate:P0} below {_options.TaskCompletionRateThreshold:P0} threshold.",
                ExpectedImpact: _options.TaskCompletionRateThreshold - task.CompletionRate,
                GeneratedAtUtc: now));
        }

        return recommendations.OrderByDescending(r => r.ExpectedImpact).ToList();
    }

    private double ComputeOverallHealth(
        IReadOnlyList<AgentEfficiencyRecord> agents,
        IReadOnlyList<ModelAccuracyRecord> models,
        IReadOnlyList<TaskCompletionRecord> tasks)
    {
        double agentHealth = agents.Count > 0
            ? agents.Average(a => a.EfficiencyScore)
            : 1.0;

        double modelHealth = models.Count > 0
            ? models.Average(m => m.CompositeScore)
            : 1.0;

        double taskHealth = tasks.Count > 0
            ? tasks.Average(t => t.CompletionRate)
            : 1.0;

        // Weighted: tasks matter most, then agents, then models
        return Math.Clamp((taskHealth * 0.40) + (agentHealth * 0.35) + (modelHealth * 0.25), 0, 1);
    }

    private async global::System.Threading.Tasks.Task TraceReportAsync(PerformanceReport report)
    {
        try
        {
            await _traceStore.RecordAsync(new TraceEntry(
                Id: Guid.NewGuid(),
                Scope: "optimization",
                Category: "performance-analysis",
                Message: $"Performance report generated: health={report.OverallHealthScore:F3}, agents={report.AgentEfficiency.Count}, models={report.ModelAccuracy.Count}, tasks={report.TaskCompletion.Count}, recommendations={report.Recommendations.Count}",
                Metadata: new Dictionary<string, string>
                {
                    ["healthScore"] = report.OverallHealthScore.ToString("F3"),
                    ["agentCount"] = report.AgentEfficiency.Count.ToString(),
                    ["modelCount"] = report.ModelAccuracy.Count.ToString(),
                    ["taskTypeCount"] = report.TaskCompletion.Count.ToString(),
                    ["recommendationCount"] = report.Recommendations.Count.ToString()
                },
                RecordedAtUtc: DateTimeOffset.UtcNow));
        }
        catch
        {
            // Tracing failures must not break analysis
        }
    }

    private sealed class AgentMetrics
    {
        private readonly object _lock = new();
        private readonly Guid _agentId;
        private readonly string _agentName;
        private int _completed;
        private int _failed;
        private double _totalExecutionTimeMs;
        private decimal _totalCost;
        private DateTimeOffset _lastActive;

        public AgentMetrics(Guid agentId, string agentName, bool success, double executionTimeMs, decimal cost)
        {
            _agentId = agentId;
            _agentName = agentName;
            _lastActive = DateTimeOffset.UtcNow;
            Record(success, executionTimeMs, cost);
        }

        public void Record(bool success, double executionTimeMs, decimal cost)
        {
            lock (_lock)
            {
                if (success) _completed++;
                else _failed++;
                _totalExecutionTimeMs += executionTimeMs;
                _totalCost += cost;
                _lastActive = DateTimeOffset.UtcNow;
            }
        }

        public AgentEfficiencyRecord ToRecord()
        {
            lock (_lock)
            {
                int total = _completed + _failed;
                double successRate = total > 0 ? (double)_completed / total : 0;
                double avgTime = total > 0 ? _totalExecutionTimeMs / total : 0;
                decimal avgCost = total > 0 ? _totalCost / total : 0;

                // Efficiency: weighted combination of success rate and speed
                double speedScore = Math.Max(0, 1.0 - (avgTime / 30_000.0)); // 30s cap
                double efficiency = (successRate * 0.7) + (speedScore * 0.3);

                return new AgentEfficiencyRecord(
                    AgentId: _agentId,
                    AgentName: _agentName,
                    TasksCompleted: _completed,
                    TasksFailed: _failed,
                    SuccessRate: successRate,
                    AverageExecutionTimeMs: avgTime,
                    AverageCost: avgCost,
                    EfficiencyScore: efficiency,
                    LastActiveUtc: _lastActive);
            }
        }
    }

    private sealed class TaskTypeMetrics
    {
        private readonly object _lock = new();
        private readonly string _taskType;
        private int _completed;
        private int _failed;
        private double _totalExecutionTimeMs;
        private decimal _totalCost;

        public TaskTypeMetrics(string taskType, bool success, double executionTimeMs, decimal cost)
        {
            _taskType = taskType;
            Record(success, executionTimeMs, cost);
        }

        public void Record(bool success, double executionTimeMs, decimal cost)
        {
            lock (_lock)
            {
                if (success) _completed++;
                else _failed++;
                _totalExecutionTimeMs += executionTimeMs;
                _totalCost += cost;
            }
        }

        public TaskCompletionRecord ToRecord()
        {
            lock (_lock)
            {
                int total = _completed + _failed;
                return new TaskCompletionRecord(
                    TaskType: _taskType,
                    TotalTasks: total,
                    Completed: _completed,
                    Failed: _failed,
                    CompletionRate: total > 0 ? (double)_completed / total : 0,
                    AverageExecutionTimeMs: total > 0 ? _totalExecutionTimeMs / total : 0,
                    AverageCost: total > 0 ? _totalCost / total : 0);
            }
        }
    }
}
