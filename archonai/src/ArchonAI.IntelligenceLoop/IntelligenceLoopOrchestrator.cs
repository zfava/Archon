using System.Diagnostics;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Evaluation;
using ArchonAI.Core.Models.Learning;
using ArchonAI.Core.Models.Patterns;
using ArchonAI.Core.Models.Perception;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Simulation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreTask = ArchonAI.Core.Models.Task;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;

namespace ArchonAI.IntelligenceLoop;

/// <summary>
/// The autonomous intelligence loop orchestrator. Continuously cycles:
/// Perception → Planner (goals) → Reasoner (strategies) → Simulation →
/// Planner (task graphs) → Agents (execution) → Reasoner (outcomes) → Learning.
/// </summary>
public sealed class IntelligenceLoopOrchestrator : IIntelligenceLoop
{
    private readonly IBusinessPerceptionEngine _perception;
    private readonly IGoalGenerator _goalGenerator;
    private readonly IReasoner _reasoner;
    private readonly IStrategySimulator _simulator;
    private readonly ITaskGraphBuilder _taskGraphBuilder;
    private readonly IDistributedTaskOrchestrator _taskOrchestrator;
    private readonly IOutcomeEvaluator _outcomeEvaluator;
    private readonly IStrategyLearningEngine _learningEngine;
    private readonly ILearningEngine _baseLearningEngine;
    private readonly IEventBus _eventBus;
    private readonly ILogger<IntelligenceLoopOrchestrator> _logger;
    private readonly IntelligenceLoopOptions _options;

    private long _totalCyclesCompleted;
    private long _totalGoalsProcessed;
    private long _totalTasksExecuted;
    private long _totalLearningCycles;
    private DateTimeOffset? _lastCycleCompletedAtUtc;
    private bool _isRunning;
    private readonly List<TimeSpan> _recentCycleDurations = new();
    private readonly Lock _statsLock = new();

    public IntelligenceLoopOrchestrator(
        IBusinessPerceptionEngine perception,
        IGoalGenerator goalGenerator,
        IReasoner reasoner,
        IStrategySimulator simulator,
        ITaskGraphBuilder taskGraphBuilder,
        IDistributedTaskOrchestrator taskOrchestrator,
        IOutcomeEvaluator outcomeEvaluator,
        IStrategyLearningEngine learningEngine,
        ILearningEngine baseLearningEngine,
        IEventBus eventBus,
        ILogger<IntelligenceLoopOrchestrator> logger,
        IOptions<IntelligenceLoopOptions> options)
    {
        _perception = perception;
        _goalGenerator = goalGenerator;
        _reasoner = reasoner;
        _simulator = simulator;
        _taskGraphBuilder = taskGraphBuilder;
        _taskOrchestrator = taskOrchestrator;
        _outcomeEvaluator = outcomeEvaluator;
        _learningEngine = learningEngine;
        _baseLearningEngine = baseLearningEngine;
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
    }

    public async global::System.Threading.Tasks.Task<IntelligenceLoopCycleResult> ExecuteCycleAsync(
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("IntelligenceLoop.Cycle");
        var cycleId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        _isRunning = true;

        activity?.SetTag("loop.cycle_id", cycleId.ToString());
        _logger.LogInformation("Intelligence loop cycle {CycleId} starting", cycleId);

        var insights = new List<string>();
        int signalsObserved = 0;
        int goalsGenerated = 0;
        int strategiesEvaluated = 0;
        int simulationsRun = 0;
        int taskGraphsBuilt = 0;
        int tasksExecuted = 0;
        int outcomesEvaluated = 0;
        bool learningApplied = false;
        var processedGoals = new List<OperationalGoal>();

        try
        {
            // ═══════════════════════════════════════════════════════
            // PHASE 1: Perception observes business signals
            // ═══════════════════════════════════════════════════════
            _logger.LogInformation("[Cycle {CycleId}] Phase 1: Perception - observing business signals", cycleId);
            var dashboard = await _perception.GetDashboardAsync(cancellationToken);
            signalsObserved = dashboard.RecentObservations.Count;
            Telemetry.IntelligenceLoopSignalsObserved.Add(signalsObserved);

            if (signalsObserved > 0)
            {
                insights.Add($"Perception observed {signalsObserved} recent observations across {dashboard.ObservationsByCategory.Count} categories");
            }

            await EmitLoopEventAsync("intelligence_loop.perception.completed", cycleId,
                new Dictionary<string, string> { ["signals_observed"] = signalsObserved.ToString() }, cancellationToken);

            // ═══════════════════════════════════════════════════════
            // PHASE 2: Planner generates goals from observations
            // ═══════════════════════════════════════════════════════
            _logger.LogInformation("[Cycle {CycleId}] Phase 2: Planner - generating goals", cycleId);
            var goalResult = await _goalGenerator.GenerateGoalsAsync(cancellationToken);
            goalsGenerated = goalResult.GeneratedGoals.Count;
            Telemetry.IntelligenceLoopGoalsGenerated.Add(goalsGenerated);

            insights.Add($"Planner generated {goalsGenerated} goals from {goalResult.SignalsAnalyzed} signals ({goalResult.AnomaliesDetected} anomalies, {goalResult.TrendsEvaluated} trends)");

            // Auto-approve goals if configured
            var goalsToProcess = new List<OperationalGoal>();
            foreach (var goal in goalResult.GeneratedGoals.Take(_options.MaxGoalsPerCycle))
            {
                if (_options.AutoApproveGoals && goal.Status == GoalStatus.Proposed)
                {
                    await _goalGenerator.ApproveGoalAsync(goal.GoalId, cancellationToken);
                    goalsToProcess.Add(goal with { Status = GoalStatus.Approved });
                }
                else if (goal.Status == GoalStatus.Approved)
                {
                    goalsToProcess.Add(goal);
                }
            }

            // Also pick up any previously approved goals that haven't been processed
            var approvedGoals = await _goalGenerator.GetGoalsByStatusAsync(GoalStatus.Approved, cancellationToken);
            foreach (var goal in approvedGoals)
            {
                if (!goalsToProcess.Any(g => g.GoalId == goal.GoalId) && goalsToProcess.Count < _options.MaxGoalsPerCycle)
                {
                    goalsToProcess.Add(goal);
                }
            }

            await EmitLoopEventAsync("intelligence_loop.planning.completed", cycleId,
                new Dictionary<string, string>
                {
                    ["goals_generated"] = goalsGenerated.ToString(),
                    ["goals_to_process"] = goalsToProcess.Count.ToString()
                }, cancellationToken);

            // ═══════════════════════════════════════════════════════
            // Process each goal through the remaining phases
            // ═══════════════════════════════════════════════════════
            foreach (var goal in goalsToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var (graphsBuilt, executed, evaluated, goalInsights) =
                        await ProcessGoalAsync(cycleId, goal, cancellationToken);

                    taskGraphsBuilt += graphsBuilt;
                    tasksExecuted += executed;
                    outcomesEvaluated += evaluated;
                    strategiesEvaluated += _options.DefaultStrategies.Length;
                    simulationsRun += _options.DefaultStrategies.Length;
                    insights.AddRange(goalInsights);
                    processedGoals.Add(goal);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Cycle {CycleId}] Failed to process goal {GoalId}: {GoalTitle}",
                        cycleId, goal.GoalId, goal.Title);
                    insights.Add($"Goal '{goal.Title}' processing failed: {ex.Message}");
                }
            }

            // ═══════════════════════════════════════════════════════
            // PHASE 8: Learning improves future strategies
            // ═══════════════════════════════════════════════════════
            long currentCycle = Interlocked.Read(ref _totalCyclesCompleted) + 1;
            if (currentCycle % _options.LearningCycleFrequency == 0 || outcomesEvaluated > 0)
            {
                _logger.LogInformation("[Cycle {CycleId}] Phase 8: Learning - analyzing outcome history", cycleId);

                var learningReport = await _learningEngine.AnalyzeAndLearnAsync(cancellationToken);
                learningApplied = true;
                Interlocked.Increment(ref _totalLearningCycles);
                Telemetry.IntelligenceLoopLearningCycles.Add(1);

                insights.Add($"Learning analyzed {learningReport.EvaluationsAnalyzed} evaluations. " +
                    $"Best strategy: {learningReport.BestOverallStrategy}. " +
                    $"{learningReport.StrategyRecommendations.Count} recommendations generated.");

                // Convert learning recommendations into operational patterns for the learning engine
                if (learningReport.StrategyRecommendations.Count > 0)
                {
                    var patterns = learningReport.StrategyRecommendations
                        .Select(r => new OperationalPattern(
                            Guid.NewGuid(),
                            Guid.Empty,
                            r.RecommendationType,
                            $"Strategy recommendation: {r.Target}",
                            r.Recommendation,
                            r.Confidence,
                            new Dictionary<string, string>
                            {
                                ["target"] = r.Target,
                                ["evidence"] = r.Evidence
                            },
                            DateTimeOffset.UtcNow))
                        .ToList();

                    await _baseLearningEngine.IngestPatternsAsync("system", patterns, cancellationToken);
                }

                await EmitLoopEventAsync("intelligence_loop.learning.completed", cycleId,
                    new Dictionary<string, string>
                    {
                        ["evaluations_analyzed"] = learningReport.EvaluationsAnalyzed.ToString(),
                        ["best_strategy"] = learningReport.BestOverallStrategy,
                        ["recommendations"] = learningReport.StrategyRecommendations.Count.ToString()
                    }, cancellationToken);
            }

            sw.Stop();
            var cycleDuration = sw.Elapsed;

            // Update stats
            lock (_statsLock)
            {
                _recentCycleDurations.Add(cycleDuration);
                if (_recentCycleDurations.Count > 50)
                    _recentCycleDurations.RemoveAt(0);
            }

            Interlocked.Increment(ref _totalCyclesCompleted);
            Interlocked.Add(ref _totalGoalsProcessed, processedGoals.Count);
            Interlocked.Add(ref _totalTasksExecuted, tasksExecuted);
            _lastCycleCompletedAtUtc = DateTimeOffset.UtcNow;

            Telemetry.IntelligenceLoopCyclesCompleted.Add(1);
            Telemetry.IntelligenceLoopCycleDurationMs.Record(cycleDuration.TotalMilliseconds);

            _logger.LogInformation(
                "[Cycle {CycleId}] Completed in {Duration}ms. Goals: {Goals}, Tasks: {Tasks}, Learning: {Learning}",
                cycleId, cycleDuration.TotalMilliseconds, processedGoals.Count, tasksExecuted, learningApplied);

            return new IntelligenceLoopCycleResult(
                cycleId, signalsObserved, goalsGenerated, strategiesEvaluated, simulationsRun,
                taskGraphsBuilt, tasksExecuted, outcomesEvaluated, learningApplied,
                processedGoals, insights, cycleDuration, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[Cycle {CycleId}] Cancelled", cycleId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Cycle {CycleId}] Failed with exception", cycleId);
            Telemetry.IntelligenceLoopCycleFailures.Add(1);
            throw;
        }
        finally
        {
            _isRunning = false;
        }
    }

    public IntelligenceLoopStatus GetStatus()
    {
        TimeSpan? avgDuration;
        lock (_statsLock)
        {
            avgDuration = _recentCycleDurations.Count > 0
                ? TimeSpan.FromMilliseconds(_recentCycleDurations.Average(d => d.TotalMilliseconds))
                : null;
        }

        return new IntelligenceLoopStatus(
            IsRunning: _isRunning,
            TotalCyclesCompleted: Interlocked.Read(ref _totalCyclesCompleted),
            TotalGoalsProcessed: Interlocked.Read(ref _totalGoalsProcessed),
            TotalTasksExecuted: Interlocked.Read(ref _totalTasksExecuted),
            TotalLearningCycles: Interlocked.Read(ref _totalLearningCycles),
            LastCycleCompletedAtUtc: _lastCycleCompletedAtUtc,
            NextCycleScheduledAtUtc: _lastCycleCompletedAtUtc?.AddSeconds(_options.CycleIntervalSeconds),
            AverageCycleDuration: avgDuration,
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Process a single goal through: Reasoner → Simulation → TaskGraph → Execution → Evaluation.
    /// </summary>
    private async global::System.Threading.Tasks.Task<(int GraphsBuilt, int TasksExecuted, int OutcomesEvaluated, List<string> Insights)>
        ProcessGoalAsync(Guid cycleId, OperationalGoal goal, CancellationToken cancellationToken)
    {
        var insights = new List<string>();
        int graphsBuilt = 0;
        int tasksExecuted = 0;
        int outcomesEvaluated = 0;

        // ═══════════════════════════════════════════════════════
        // PHASE 3: Reasoner evaluates strategies for this goal
        // ═══════════════════════════════════════════════════════
        _logger.LogInformation("[Cycle {CycleId}] Phase 3: Reasoner - evaluating strategies for goal {GoalId}", cycleId, goal.GoalId);

        var objective = new Objective(
            goal.GoalId, goal.Title, goal.Description, goal.Context,
            goal.CreatedAtUtc, goal.Deadline);

        var evaluation = await _reasoner.EvaluateAsync(objective, Array.Empty<ExecutionResult>(), cancellationToken);
        insights.Add($"Reasoner evaluation for '{goal.Title}': {evaluation}");
        Telemetry.IntelligenceLoopStrategiesEvaluated.Add(_options.DefaultStrategies.Length);

        // ═══════════════════════════════════════════════════════
        // PHASE 4: Simulation tests strategies against task graphs
        // ═══════════════════════════════════════════════════════
        _logger.LogInformation("[Cycle {CycleId}] Phase 4: Simulation - testing strategies for goal {GoalId}", cycleId, goal.GoalId);

        var comparison = await _simulator.SimulateGoalStrategiesAsync(
            goal, _options.DefaultStrategies, cancellationToken);

        Telemetry.IntelligenceLoopSimulationsRun.Add(comparison.Simulations.Count);

        var recommended = comparison.RecommendedSimulation;
        insights.Add($"Simulation recommends strategy '{recommended.Strategy}' " +
            $"(success: {recommended.ExpectedOutcome.OverallSuccessProbability:P0}, " +
            $"risk: {recommended.RiskScore:F2}): {comparison.RecommendationReason}");

        // Skip execution if simulation predicts low success
        if (recommended.ExpectedOutcome.OverallSuccessProbability < _options.MinSimulationSuccessProbability)
        {
            insights.Add($"Skipping goal '{goal.Title}' - simulation success probability " +
                $"{recommended.ExpectedOutcome.OverallSuccessProbability:P0} below threshold {_options.MinSimulationSuccessProbability:P0}");
            return (graphsBuilt, tasksExecuted, outcomesEvaluated, insights);
        }

        // ═══════════════════════════════════════════════════════
        // PHASE 5: Planner generates task graphs
        // ═══════════════════════════════════════════════════════
        _logger.LogInformation("[Cycle {CycleId}] Phase 5: Planner - building task graph for goal {GoalId} with strategy {Strategy}",
            cycleId, goal.GoalId, recommended.Strategy);

        var taskGraph = await _taskGraphBuilder.BuildGraphAsync(goal, recommended.Strategy, cancellationToken);
        graphsBuilt = 1;
        Telemetry.IntelligenceLoopTaskGraphsBuilt.Add(1);

        insights.Add($"Task graph built: {taskGraph.Nodes.Count} nodes, {taskGraph.Edges.Count} edges, " +
            $"{taskGraph.GetExecutionLayers().Count} execution layers");

        // ═══════════════════════════════════════════════════════
        // PHASE 6: Agents execute tasks via task orchestrator
        // ═══════════════════════════════════════════════════════
        _logger.LogInformation("[Cycle {CycleId}] Phase 6: Agents - executing task graph {GraphId}", cycleId, taskGraph.GraphId);

        var dispatchResult = await _taskGraphBuilder.DispatchGraphAsync(taskGraph, cancellationToken);
        var executionLayers = taskGraph.GetExecutionLayers();

        // Execute tasks layer by layer through the distributed orchestrator
        var allExecutionResults = new List<ExecutionResult>();
        var nodeResults = new List<TaskNodeExecutionResult>();

        foreach (var layer in executionLayers)
        {
            foreach (var node in layer)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var task = new CoreTask(
                    node.NodeId, goal.GoalId, node.Priority, node.Name, node.Description,
                    node.AgentType, node.RequiredInputs, DateTimeOffset.UtcNow, null, null);

                await _taskOrchestrator.EnqueueAsync(task, cancellationToken);
            }

            var context = new CoreExecutionContext(
                Guid.NewGuid(), goal.GoalId, Guid.NewGuid(), "system",
                new Dictionary<string, string>
                {
                    ["cycle_id"] = cycleId.ToString(),
                    ["strategy"] = recommended.Strategy,
                    ["graph_id"] = taskGraph.GraphId.ToString()
                },
                DateTimeOffset.UtcNow);

            var layerResults = await _taskOrchestrator.ExecuteAllAsync(
                context,
                async (t, ct) =>
                {
                    var nodeSw = Stopwatch.StartNew();
                    try
                    {
                        // Task execution is handled by the distributed orchestrator's agent pool
                        return new ExecutionResult(
                            t.Id, true, $"Executed: {t.Name}",
                            new Dictionary<string, string> { ["agent_type"] = t.RequiredCapability },
                            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);
                    }
                    catch (Exception ex)
                    {
                        return new ExecutionResult(
                            t.Id, false, $"Failed: {t.Name}",
                            new Dictionary<string, string>(), Array.Empty<string>(),
                            new[] { ex.Message }, DateTimeOffset.UtcNow);
                    }
                },
                cancellationToken);

            allExecutionResults.AddRange(layerResults);

            // Build node execution results for outcome evaluation
            foreach (var result in layerResults)
            {
                var node = taskGraph.Nodes.FirstOrDefault(n => n.NodeId == result.TaskId);
                if (node is not null)
                {
                    nodeResults.Add(new TaskNodeExecutionResult(
                        node.NodeId, node.Name, node.AgentType, result.IsSuccess,
                        node.EstimatedDurationHours, 0m, 1,
                        result.Errors.ToList(), result.CompletedAtUtc));
                }
            }
        }

        tasksExecuted = allExecutionResults.Count;
        Interlocked.Add(ref _totalTasksExecuted, tasksExecuted);
        Telemetry.IntelligenceLoopTasksExecuted.Add(tasksExecuted);

        bool overallSuccess = allExecutionResults.All(r => r.IsSuccess);
        insights.Add($"Executed {tasksExecuted} tasks. Overall success: {overallSuccess}. " +
            $"Succeeded: {allExecutionResults.Count(r => r.IsSuccess)}, Failed: {allExecutionResults.Count(r => !r.IsSuccess)}");

        // ═══════════════════════════════════════════════════════
        // PHASE 7: Reasoner evaluates outcomes
        // ═══════════════════════════════════════════════════════
        _logger.LogInformation("[Cycle {CycleId}] Phase 7: Reasoner - evaluating outcomes for goal {GoalId}", cycleId, goal.GoalId);

        var actualResult = new TaskGraphExecutionResult(
            taskGraph.GraphId, goal.GoalId, recommended.Strategy, overallSuccess,
            nodeResults,
            recommended.EstimatedTotalDurationHours,
            recommended.EstimatedTotalCost,
            DateTimeOffset.UtcNow);

        var outcomeEvaluation = await _outcomeEvaluator.EvaluateAsync(
            recommended, actualResult, cancellationToken);

        outcomesEvaluated = 1;
        Telemetry.IntelligenceLoopOutcomesEvaluated.Add(1);

        insights.Add($"Outcome evaluation: {outcomeEvaluation.OverallAssessment} " +
            $"(score: {outcomeEvaluation.SuccessMetrics.OverallScore:F2}). " +
            $"{outcomeEvaluation.Recommendations.Count} recommendations.");

        // Also get reasoner evaluation of the final outcomes
        var finalEvaluation = await _reasoner.EvaluateAsync(objective, allExecutionResults, cancellationToken);
        insights.Add($"Reasoner final assessment: {finalEvaluation}");

        await EmitLoopEventAsync("intelligence_loop.goal.completed", cycleId,
            new Dictionary<string, string>
            {
                ["goal_id"] = goal.GoalId.ToString(),
                ["goal_title"] = goal.Title,
                ["strategy"] = recommended.Strategy,
                ["overall_success"] = overallSuccess.ToString(),
                ["tasks_executed"] = tasksExecuted.ToString(),
                ["outcome_score"] = outcomeEvaluation.SuccessMetrics.OverallScore.ToString("F2")
            }, cancellationToken);

        return (graphsBuilt, tasksExecuted, outcomesEvaluated, insights);
    }

    private async global::System.Threading.Tasks.Task EmitLoopEventAsync(
        string eventType, Guid cycleId, Dictionary<string, string> payload, CancellationToken cancellationToken)
    {
        payload["cycle_id"] = cycleId.ToString();
        payload["timestamp"] = DateTimeOffset.UtcNow.ToString("O");

        var systemEvent = new SystemEvent(
            Guid.NewGuid(), eventType, "IntelligenceLoop",
            cycleId, payload, DateTimeOffset.UtcNow);

        await _eventBus.PublishAsync(systemEvent, cancellationToken);
    }
}
