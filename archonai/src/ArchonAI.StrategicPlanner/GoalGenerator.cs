using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.OrganizationState;
using ArchonAI.Core.Models.Optimization;
using ArchonAI.Core.Models.Perception;
using ArchonAI.Core.Models.Planning;
using Microsoft.Extensions.Logging;

namespace ArchonAI.StrategicPlanner;

public sealed class GoalGenerator : IGoalGenerator
{
    private readonly IOrganizationStateEngine _stateEngine;
    private readonly IBusinessPerceptionEngine _perceptionEngine;
    private readonly IPerformanceAnalyzer _performanceAnalyzer;
    private readonly IPlanner _planner;
    private readonly IEventBus _eventBus;
    private readonly ILogger<GoalGenerator> _logger;

    private readonly ConcurrentDictionary<Guid, OperationalGoal> _goals = new();
    private long _totalRuns;

    public GoalGenerator(
        IOrganizationStateEngine stateEngine,
        IBusinessPerceptionEngine perceptionEngine,
        IPerformanceAnalyzer performanceAnalyzer,
        IPlanner planner,
        IEventBus eventBus,
        ILogger<GoalGenerator> logger)
    {
        _stateEngine = stateEngine;
        _perceptionEngine = perceptionEngine;
        _performanceAnalyzer = performanceAnalyzer;
        _planner = planner;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task<GoalGenerationResult> GenerateGoalsAsync(
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalRuns);

        var state = await _stateEngine.GetCurrentStateAsync(cancellationToken);
        var perceptionDashboard = await _perceptionEngine.GetDashboardAsync(cancellationToken);
        var perfReport = await _performanceAnalyzer.AnalyzeAsync(cancellationToken);

        var goals = new List<OperationalGoal>();
        int anomaliesDetected = 0;
        int trendsEvaluated = 0;

        // 1. Goals from state anomalies (department health)
        foreach (var dept in state.Departments)
        {
            if (dept.HealthScore < 0.6)
            {
                anomaliesDetected++;
                goals.Add(CreateGoal(
                    title: $"Restore {dept.DisplayName} department health",
                    description: $"Department '{dept.DisplayName}' health score is {dept.HealthScore:F2}, below threshold. " +
                                 $"Pending tasks: {dept.PendingTasks}.",
                    priority: dept.HealthScore < 0.3 ? GoalPriority.Critical : GoalPriority.High,
                    source: GoalSource.StateAnomaly,
                    expectedImpact: $"Restore health score from {dept.HealthScore:F2} to >0.8",
                    department: dept.DepartmentId,
                    deadlineHours: dept.HealthScore < 0.3 ? 4 : 24,
                    context: new Dictionary<string, string>
                    {
                        ["currentHealthScore"] = dept.HealthScore.ToString("F3"),
                        ["pendingTasks"] = dept.PendingTasks.ToString(),
                        ["departmentType"] = dept.Type.ToString()
                    }));
            }

            if (dept.PendingTasks > 50)
            {
                anomaliesDetected++;
                goals.Add(CreateGoal(
                    title: $"Reduce backlog in {dept.DisplayName}",
                    description: $"Department '{dept.DisplayName}' has {dept.PendingTasks} pending tasks exceeding threshold.",
                    priority: dept.PendingTasks > 100 ? GoalPriority.High : GoalPriority.Medium,
                    source: GoalSource.StateAnomaly,
                    expectedImpact: $"Reduce pending tasks from {dept.PendingTasks} to <20",
                    department: dept.DepartmentId,
                    deadlineHours: 48,
                    context: new Dictionary<string, string>
                    {
                        ["pendingTasks"] = dept.PendingTasks.ToString(),
                        ["departmentType"] = dept.Type.ToString()
                    }));
            }
        }

        // 2. Goals from resource utilization
        foreach (var resource in state.Resources)
        {
            double utilPercent = resource.Allocated > 0
                ? resource.Utilized / resource.Allocated * 100.0
                : 0;

            if (utilPercent > 90)
            {
                anomaliesDetected++;
                goals.Add(CreateGoal(
                    title: $"Address {resource.Kind} capacity in {resource.DepartmentId}",
                    description: $"Resource '{resource.ResourceId}' utilization at {utilPercent:F1}% — nearing capacity.",
                    priority: utilPercent > 95 ? GoalPriority.Critical : GoalPriority.High,
                    source: GoalSource.StateAnomaly,
                    expectedImpact: $"Reduce utilization from {utilPercent:F1}% to <80%",
                    department: resource.DepartmentId,
                    deadlineHours: 24,
                    context: new Dictionary<string, string>
                    {
                        ["resourceKind"] = resource.Kind.ToString(),
                        ["utilized"] = resource.Utilized.ToString("F2"),
                        ["allocated"] = resource.Allocated.ToString("F2"),
                        ["utilPercent"] = utilPercent.ToString("F1")
                    }));
            }
        }

        // 3. Goals from customer health
        var atRiskCustomers = await _stateEngine.GetCustomersByHealthAsync(CustomerHealthStatus.AtRisk, cancellationToken);
        if (atRiskCustomers.Count > 0)
        {
            double totalLtv = atRiskCustomers.Sum(c => c.LifetimeValue);
            goals.Add(CreateGoal(
                title: "Retain at-risk customers",
                description: $"{atRiskCustomers.Count} customers are at risk with combined LTV of {totalLtv:C0}.",
                priority: atRiskCustomers.Count > 5 ? GoalPriority.High : GoalPriority.Medium,
                source: GoalSource.BusinessSignal,
                expectedImpact: $"Prevent churn of {atRiskCustomers.Count} customers worth {totalLtv:C0}",
                department: "sales",
                deadlineHours: 72,
                context: new Dictionary<string, string>
                {
                    ["atRiskCount"] = atRiskCustomers.Count.ToString(),
                    ["totalLtv"] = totalLtv.ToString("F2")
                }));
        }

        // 4. Goals from perception signals (recent high-severity observations)
        int signalsAnalyzed = 0;
        foreach (var obs in perceptionDashboard.RecentObservations)
        {
            signalsAnalyzed++;
            if (obs.Severity < ObservationSeverity.High) continue;

            if (obs.Category == ObservationCategory.Revenue)
            {
                goals.Add(CreateGoal(
                    title: "Increase revenue from flagged opportunity",
                    description: $"High-severity revenue signal from {obs.SourceSystem}: {obs.Summary}",
                    priority: GoalPriority.High,
                    source: GoalSource.BusinessSignal,
                    expectedImpact: "Capitalize on revenue opportunity identified by perception engine",
                    department: MapSourceToDepartment(obs.SourceSystem),
                    deadlineHours: 48,
                    context: new Dictionary<string, string>
                    {
                        ["observationId"] = obs.ObservationId.ToString(),
                        ["category"] = obs.Category.ToString()
                    }));
            }
            else if (obs.Category == ObservationCategory.CampaignPerformance)
            {
                goals.Add(CreateGoal(
                    title: "Improve marketing conversion",
                    description: $"Campaign performance signal from {obs.SourceSystem}: {obs.Summary}",
                    priority: GoalPriority.Medium,
                    source: GoalSource.BusinessSignal,
                    expectedImpact: "Optimize underperforming campaigns to improve conversion rates",
                    department: "marketing",
                    deadlineHours: 72,
                    context: new Dictionary<string, string>
                    {
                        ["observationId"] = obs.ObservationId.ToString(),
                        ["category"] = obs.Category.ToString()
                    }));
            }
            else if (obs.Category == ObservationCategory.SupplyChain)
            {
                goals.Add(CreateGoal(
                    title: "Optimize logistics routing",
                    description: $"Supply chain signal from {obs.SourceSystem}: {obs.Summary}",
                    priority: GoalPriority.Medium,
                    source: GoalSource.BusinessSignal,
                    expectedImpact: "Reduce delivery times and optimize technician/transport routes",
                    department: "logistics",
                    deadlineHours: 48,
                    context: new Dictionary<string, string>
                    {
                        ["observationId"] = obs.ObservationId.ToString(),
                        ["category"] = obs.Category.ToString()
                    }));
            }
        }

        // 5. Goals from performance trends
        foreach (var rec in perfReport.Recommendations)
        {
            trendsEvaluated++;
            if (rec.ExpectedImpact < 0.3) continue;

            goals.Add(CreateGoal(
                title: $"Performance: {rec.Action}",
                description: $"[{rec.Category}] {rec.Reason} — target: {rec.Target}",
                priority: rec.ExpectedImpact > 0.7 ? GoalPriority.High : GoalPriority.Medium,
                source: GoalSource.PerformanceTrend,
                expectedImpact: $"Expected {rec.ExpectedImpact:P0} improvement in {rec.Category}",
                department: "operations",
                deadlineHours: 96,
                context: new Dictionary<string, string>
                {
                    ["category"] = rec.Category,
                    ["target"] = rec.Target,
                    ["expectedImpact"] = rec.ExpectedImpact.ToString("F3")
                }));
        }

        // 6. Goals from task failure rates
        foreach (var task in perfReport.TaskCompletion)
        {
            trendsEvaluated++;
            if (task.CompletionRate >= 0.9 || task.TotalTasks < 10) continue;

            goals.Add(CreateGoal(
                title: $"Improve {task.TaskType} completion rate",
                description: $"Task type '{task.TaskType}' has {task.CompletionRate:P0} completion rate " +
                             $"({task.Failed}/{task.TotalTasks} failed).",
                priority: task.CompletionRate < 0.7 ? GoalPriority.High : GoalPriority.Medium,
                source: GoalSource.PerformanceTrend,
                expectedImpact: $"Raise completion rate from {task.CompletionRate:P0} to >90%",
                department: "operations",
                deadlineHours: 72,
                context: new Dictionary<string, string>
                {
                    ["taskType"] = task.TaskType,
                    ["completionRate"] = task.CompletionRate.ToString("F3"),
                    ["totalTasks"] = task.TotalTasks.ToString(),
                    ["failedTasks"] = task.Failed.ToString()
                }));
        }

        // Deduplicate by title
        var deduplicated = goals
            .GroupBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Priority).First())
            .ToList();

        // Store and publish
        foreach (var goal in deduplicated)
        {
            _goals[goal.GoalId] = goal;
        }

        foreach (var goal in deduplicated)
        {
            await PublishGoalEventAsync(goal, cancellationToken);
        }

        _logger.LogInformation(
            "Goal generation run: {Goals} goals from {Signals} signals, {Anomalies} anomalies, {Trends} trends",
            deduplicated.Count, signalsAnalyzed, anomaliesDetected, trendsEvaluated);

        return new GoalGenerationResult(
            GeneratedGoals: deduplicated,
            SignalsAnalyzed: signalsAnalyzed,
            AnomaliesDetected: anomaliesDetected,
            TrendsEvaluated: trendsEvaluated,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    public global::System.Threading.Tasks.Task<OperationalGoal?> GetGoalAsync(
        Guid goalId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _goals.TryGetValue(goalId, out var goal);
        return global::System.Threading.Tasks.Task.FromResult(goal);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<OperationalGoal>> GetGoalsByStatusAsync(
        GoalStatus status,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<OperationalGoal> result = _goals.Values
            .Where(g => g.Status == status)
            .OrderByDescending(g => g.Priority)
            .ThenBy(g => g.Deadline)
            .ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public async global::System.Threading.Tasks.Task ApproveGoalAsync(
        Guid goalId,
        CancellationToken cancellationToken = default)
    {
        if (!_goals.TryGetValue(goalId, out var goal))
            return;

        var approved = goal with { Status = GoalStatus.Approved };
        _goals[goalId] = approved;

        await PublishGoalToPlannerAsync(approved, cancellationToken);

        _logger.LogInformation("Goal {GoalId} approved and published to Planner: {Title}", goalId, goal.Title);
    }

    public global::System.Threading.Tasks.Task CancelGoalAsync(
        Guid goalId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (_goals.TryGetValue(goalId, out var goal))
        {
            _goals[goalId] = goal with
            {
                Status = GoalStatus.Cancelled,
                Context = new Dictionary<string, string>(goal.Context, StringComparer.OrdinalIgnoreCase)
                {
                    ["cancellationReason"] = reason
                }
            };
            _logger.LogInformation("Goal {GoalId} cancelled: {Reason}", goalId, reason);
        }

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<GoalDashboard> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var allGoals = _goals.Values.ToList();

        var dashboard = new GoalDashboard(
            TotalGoals: allGoals.Count,
            ProposedGoals: allGoals.Count(g => g.Status == GoalStatus.Proposed),
            InProgressGoals: allGoals.Count(g => g.Status == GoalStatus.InProgress),
            CompletedGoals: allGoals.Count(g => g.Status == GoalStatus.Completed),
            GoalsByPriority: allGoals.GroupBy(g => g.Priority).ToDictionary(g => g.Key, g => g.Count()),
            GoalsBySource: allGoals.GroupBy(g => g.Source).ToDictionary(g => g.Key, g => g.Count()),
            RecentGoals: allGoals.OrderByDescending(g => g.CreatedAtUtc).Take(20).ToList(),
            TotalGenerationRuns: Interlocked.Read(ref _totalRuns),
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        return global::System.Threading.Tasks.Task.FromResult(dashboard);
    }

    // ══════════════════════════════════════════════════════════════
    //  Internals
    // ══════════════════════════════════════════════════════════════

    private static OperationalGoal CreateGoal(
        string title,
        string description,
        GoalPriority priority,
        GoalSource source,
        string expectedImpact,
        string department,
        int deadlineHours,
        Dictionary<string, string> context)
    {
        return new OperationalGoal(
            GoalId: Guid.NewGuid(),
            Title: title,
            Description: description,
            Priority: priority,
            Source: source,
            Status: GoalStatus.Proposed,
            ExpectedImpact: expectedImpact,
            Department: department,
            Deadline: DateTimeOffset.UtcNow.AddHours(deadlineHours),
            Context: context,
            CreatedAtUtc: DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task PublishGoalEventAsync(
        OperationalGoal goal,
        CancellationToken cancellationToken)
    {
        try
        {
            var systemEvent = new SystemEvent(
                Id: Guid.NewGuid(),
                EventType: "strategic.goal.generated",
                Source: "goal-generator",
                CorrelationId: goal.GoalId,
                Payload: new Dictionary<string, string>
                {
                    ["goalId"] = goal.GoalId.ToString(),
                    ["title"] = goal.Title,
                    ["priority"] = goal.Priority.ToString(),
                    ["source"] = goal.Source.ToString(),
                    ["department"] = goal.Department,
                    ["expectedImpact"] = goal.ExpectedImpact,
                    ["deadline"] = goal.Deadline.ToString("O")
                },
                OccurredAtUtc: DateTimeOffset.UtcNow);

            await _eventBus.PublishAsync(systemEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish goal event for {GoalId}", goal.GoalId);
        }
    }

    private async global::System.Threading.Tasks.Task PublishGoalToPlannerAsync(
        OperationalGoal goal,
        CancellationToken cancellationToken)
    {
        try
        {
            var objective = new Objective(
                Id: goal.GoalId,
                Title: goal.Title,
                Description: goal.Description,
                Constraints: new Dictionary<string, string>(goal.Context, StringComparer.OrdinalIgnoreCase)
                {
                    ["objectiveType"] = "auto-generated-goal",
                    ["goalSource"] = goal.Source.ToString(),
                    ["priority"] = goal.Priority.ToString(),
                    ["department"] = goal.Department,
                    ["expectedImpact"] = goal.ExpectedImpact
                },
                CreatedAtUtc: DateTimeOffset.UtcNow,
                DueAtUtc: goal.Deadline);

            await _planner.CreatePlanAsync(objective, cancellationToken);

            _goals[goal.GoalId] = goal with { Status = GoalStatus.InProgress };

            _logger.LogInformation("Goal {GoalId} published to Planner: {Title}", goal.GoalId, goal.Title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish goal {GoalId} to Planner", goal.GoalId);
        }
    }

    private static string MapSourceToDepartment(SourceSystem source) => source switch
    {
        SourceSystem.CRM => "sales",
        SourceSystem.ERP => "operations",
        SourceSystem.Finance => "finance",
        SourceSystem.Marketing => "marketing",
        SourceSystem.Logistics => "logistics",
        _ => "operations"
    };
}
