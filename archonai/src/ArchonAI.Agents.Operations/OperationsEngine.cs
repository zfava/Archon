using System.Diagnostics;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.DataFabric;
using ArchonAI.Core.Models.Operations;
using ArchonAI.Core.Models.Patterns;
using ArchonAI.Core.Models.Planning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Agents.Operations;

public sealed class OperationsEngine : IOperationsEngine
{
    private readonly IDataFabricEngine _dataFabric;
    private readonly IStrategyStore _strategyStore;
    private readonly IPatternAnalyzer _patternAnalyzer;
    private readonly IReasoner _reasoner;
    private readonly IEventBus _eventBus;
    private readonly ILogger<OperationsEngine> _logger;
    private readonly OperationsOptions _options;

    private long _workflowsAnalyzed;
    private long _inefficienciesDetected;
    private long _recommendationsGenerated;
    private long _workflowsCoordinated;
    private long _reasoningCycles;
    private long _dataFabricQueries;
    private long _strategyLookups;

    public OperationsEngine(
        IDataFabricEngine dataFabric,
        IStrategyStore strategyStore,
        IPatternAnalyzer patternAnalyzer,
        IReasoner reasoner,
        IEventBus eventBus,
        ILogger<OperationsEngine> logger,
        IOptions<OperationsOptions> options)
    {
        _dataFabric = dataFabric;
        _strategyStore = strategyStore;
        _patternAnalyzer = patternAnalyzer;
        _reasoner = reasoner;
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
    }

    public async global::System.Threading.Tasks.Task<WorkflowAnalysisResult> AnalyzeWorkflowAsync(
        Guid objectiveId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Operations.AnalyzeWorkflow");
        activity?.SetTag("operations.objective_id", objectiveId.ToString());

        Telemetry.OperationsAnalyses.Add(1);

        // Step 1: Query Data Fabric for execution history
        Interlocked.Increment(ref _dataFabricQueries);
        Telemetry.OperationsDataFabricQueries.Add(1);

        var fabricResult = await _dataFabric.QueryEnterpriseDataAsync(
            source: "*",
            filters: new Dictionary<string, string> { ["objectiveId"] = objectiveId.ToString() },
            schemaMapping: new Dictionary<string, string>(),
            permissions: _options.DataFabricPermissions,
            consumerType: _options.DefaultConsumerType,
            cancellationToken);

        // Step 2: Discover patterns for this objective
        IReadOnlyList<OperationalPattern> patterns = await _patternAnalyzer.AnalyzeObjectiveAsync(objectiveId, cancellationToken);

        // Step 3: Reasoning loop - evaluate and iterate
        int totalSteps = fabricResult.Rows.Count;
        int successfulSteps = fabricResult.Rows.Count(r =>
            r.TryGetValue("isSuccess", out var v) && v.Equals("True", StringComparison.OrdinalIgnoreCase));
        int failedSteps = totalSteps - successfulSteps;

        double efficiencyScore = totalSteps > 0 ? (double)successfulSteps / totalSteps : 0.0;

        var bottlenecks = new List<string>();
        var recommendations = new List<string>();

        // Analyze patterns for bottlenecks
        foreach (var pattern in patterns)
        {
            if (pattern.PatternType.Contains("failure", StringComparison.OrdinalIgnoreCase))
            {
                bottlenecks.Add($"Recurring failure: {pattern.Title} (score={pattern.Score:F2})");
            }

            if (pattern.PatternType.Contains("latency", StringComparison.OrdinalIgnoreCase))
            {
                bottlenecks.Add($"High latency: {pattern.Title}");
            }
        }

        // Strategy-based recommendations
        string objectiveType = parameters.GetValueOrDefault("objectiveType", "default");
        Interlocked.Increment(ref _strategyLookups);
        Telemetry.OperationsStrategyLookups.Add(1);

        IReadOnlyList<OperationalStrategy> strategies = await _strategyStore.QueryByObjectiveTypeAsync(objectiveType, cancellationToken);

        foreach (var strategy in strategies)
        {
            if (double.TryParse(strategy.SuccessMetrics.GetValueOrDefault("successRate", "0"), out double targetRate)
                && efficiencyScore < targetRate)
            {
                recommendations.Add($"Consider '{strategy.WorkflowTemplate}' template (target success rate: {targetRate:P0})");
            }
        }

        if (efficiencyScore < _options.InefficiencyThreshold)
        {
            recommendations.Add("Workflow efficiency below threshold; consider decomposing into smaller tasks");
        }

        if (failedSteps > 0)
        {
            recommendations.Add($"Investigate {failedSteps} failed step(s) for root cause analysis");
        }

        Interlocked.Increment(ref _workflowsAnalyzed);
        Interlocked.Increment(ref _reasoningCycles);
        Telemetry.OperationsReasoningCycles.Add(1);

        var metadata = new Dictionary<string, string>
        {
            ["patternsDiscovered"] = patterns.Count.ToString(),
            ["strategiesConsidered"] = strategies.Count.ToString(),
            ["dataFabricRows"] = fabricResult.Rows.Count.ToString()
        };

        _logger.LogInformation(
            "Workflow analysis for objective {ObjectiveId}: efficiency={Efficiency:P1}, bottlenecks={Bottlenecks}, recommendations={Recommendations}",
            objectiveId, efficiencyScore, bottlenecks.Count, recommendations.Count);

        await EmitAuditEventAsync("operations.workflow.analyzed", objectiveId.ToString(), cancellationToken);

        return new WorkflowAnalysisResult(
            objectiveId, true, totalSteps, successfulSteps, failedSteps,
            efficiencyScore, bottlenecks, recommendations, metadata, DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<OperationsInsight>> IdentifyInefficienciesAsync(
        string scope,
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Operations.IdentifyInefficiencies");

        Telemetry.OperationsInefficiencyScans.Add(1);

        Interlocked.Increment(ref _dataFabricQueries);
        Telemetry.OperationsDataFabricQueries.Add(1);

        var fabricResult = await _dataFabric.QueryEnterpriseDataAsync(
            source: scope,
            filters: new Dictionary<string, string>(),
            schemaMapping: new Dictionary<string, string>(),
            permissions: _options.DataFabricPermissions,
            consumerType: _options.DefaultConsumerType,
            cancellationToken);

        var insights = new List<OperationsInsight>();

        // Analyze rows for inefficiencies
        var failureRows = fabricResult.Rows
            .Where(r => r.TryGetValue("isSuccess", out var v) && v.Equals("False", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (failureRows.Count > 0)
        {
            double failureRate = (double)failureRows.Count / Math.Max(fabricResult.Rows.Count, 1);
            insights.Add(new OperationsInsight(
                Guid.NewGuid(),
                "high-failure-rate",
                $"High failure rate in '{scope}'",
                $"Detected {failureRows.Count} failures out of {fabricResult.Rows.Count} records ({failureRate:P1})",
                failureRate,
                scope,
                new Dictionary<string, string>
                {
                    ["failureCount"] = failureRows.Count.ToString(),
                    ["totalCount"] = fabricResult.Rows.Count.ToString()
                },
                DateTimeOffset.UtcNow));
        }

        // Check for slow execution patterns
        var slowRows = fabricResult.Rows
            .Where(r => r.TryGetValue("executionMs", out var ms) && double.TryParse(ms, out var d) && d > 1500)
            .ToList();

        if (slowRows.Count > 0)
        {
            insights.Add(new OperationsInsight(
                Guid.NewGuid(),
                "slow-execution",
                $"Slow execution in '{scope}'",
                $"Found {slowRows.Count} slow-running operations exceeding 1500ms threshold",
                Math.Min(1.0, slowRows.Count / 10.0),
                scope,
                new Dictionary<string, string> { ["slowCount"] = slowRows.Count.ToString() },
                DateTimeOffset.UtcNow));
        }

        // Check for underutilized resources
        var uniqueSources = fabricResult.Rows
            .Where(r => r.TryGetValue("source", out _))
            .Select(r => r["source"])
            .Distinct()
            .ToList();

        if (uniqueSources.Count == 1 && fabricResult.Rows.Count > 10)
        {
            insights.Add(new OperationsInsight(
                Guid.NewGuid(),
                "single-source-dependency",
                "Single source dependency detected",
                $"All {fabricResult.Rows.Count} records come from '{uniqueSources[0]}'. Consider diversifying data sources.",
                0.5,
                scope,
                new Dictionary<string, string> { ["source"] = uniqueSources[0] },
                DateTimeOffset.UtcNow));
        }

        int clampedMax = Math.Clamp(maxResults, 1, _options.MaxInsightsPerAnalysis);
        var result = insights.OrderByDescending(i => i.Severity).Take(clampedMax).ToList();

        Interlocked.Add(ref _inefficienciesDetected, result.Count);
        _logger.LogInformation("Identified {Count} inefficiencies in scope '{Scope}'", result.Count, scope);

        return result;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<OperationsRecommendation>> RecommendImprovementsAsync(
        Guid objectiveId,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Operations.RecommendImprovements");

        Telemetry.OperationsRecommendations.Add(1);

        // Discover patterns
        IReadOnlyList<OperationalPattern> patterns = await _patternAnalyzer.AnalyzeObjectiveAsync(objectiveId, cancellationToken);

        // Query strategies
        Interlocked.Increment(ref _strategyLookups);
        Telemetry.OperationsStrategyLookups.Add(1);
        IReadOnlyList<OperationalStrategy> strategies = await _strategyStore.QueryByObjectiveTypeAsync("default", cancellationToken);

        var recommendations = new List<OperationsRecommendation>();

        // Generate recommendations based on patterns
        foreach (var pattern in patterns.OrderByDescending(p => p.Score).Take(5))
        {
            string recType = pattern.PatternType switch
            {
                var t when t.Contains("failure", StringComparison.OrdinalIgnoreCase) => "failure-mitigation",
                var t when t.Contains("latency", StringComparison.OrdinalIgnoreCase) => "performance-optimization",
                var t when t.Contains("success", StringComparison.OrdinalIgnoreCase) => "best-practice-replication",
                _ => "general-improvement"
            };

            var bestStrategy = strategies.FirstOrDefault();
            string suggestedStrategy = bestStrategy?.WorkflowTemplate ?? "balanced";
            IReadOnlyList<string> suggestedAgents = bestStrategy?.RecommendedAgents ?? ["workflow-orchestration"];

            recommendations.Add(new OperationsRecommendation(
                Guid.NewGuid(),
                objectiveId,
                recType,
                $"Improvement for: {pattern.Title}",
                $"Based on pattern analysis (score={pattern.Score:F2}): {pattern.Description}",
                pattern.Score,
                suggestedStrategy,
                suggestedAgents,
                DateTimeOffset.UtcNow));
        }

        // Add general recommendations if no patterns found
        if (recommendations.Count == 0)
        {
            recommendations.Add(new OperationsRecommendation(
                Guid.NewGuid(),
                objectiveId,
                "baseline-optimization",
                "Establish operational baseline",
                "No significant patterns detected. Consider establishing monitoring baselines to track future improvements.",
                0.5,
                "balanced",
                ["workflow-orchestration", "tooling-execution"],
                DateTimeOffset.UtcNow));
        }

        int max = _options.MaxRecommendationsPerObjective;
        var result = recommendations.Take(max).ToList();

        Interlocked.Add(ref _recommendationsGenerated, result.Count);
        _logger.LogInformation("Generated {Count} recommendations for objective {ObjectiveId}", result.Count, objectiveId);

        await EmitAuditEventAsync("operations.recommendations.generated", objectiveId.ToString(), cancellationToken);

        return result;
    }

    public async global::System.Threading.Tasks.Task<WorkflowCoordinationResult> CoordinateWorkflowAsync(
        string workflowTemplate,
        IReadOnlyList<string> agentCapabilities,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Operations.CoordinateWorkflow");
        activity?.SetTag("operations.template", workflowTemplate);

        Telemetry.OperationsCoordinations.Add(1);
        var sw = Stopwatch.StartNew();
        var coordinationId = Guid.NewGuid();

        _logger.LogInformation(
            "Starting workflow coordination {CoordinationId} with template '{Template}', agents={AgentCount}",
            coordinationId, workflowTemplate, agentCapabilities.Count);

        // Reasoning loop: evaluate strategy, execute steps, check results
        Interlocked.Increment(ref _strategyLookups);
        Telemetry.OperationsStrategyLookups.Add(1);

        IReadOnlyList<OperationalStrategy> strategies = await _strategyStore.QueryByObjectiveTypeAsync(
            inputs.GetValueOrDefault("objectiveType", "default"), cancellationToken);

        var selectedStrategy = strategies.FirstOrDefault();
        var outputs = new Dictionary<string, string>
        {
            ["coordinationId"] = coordinationId.ToString(),
            ["workflowTemplate"] = workflowTemplate,
            ["selectedStrategy"] = selectedStrategy?.WorkflowTemplate ?? "balanced"
        };

        int stepsCompleted = 0;
        int stepsFailed = 0;
        var errors = new List<string>();

        // Simulate multi-agent coordination reasoning loop
        for (int cycle = 0; cycle < Math.Min(agentCapabilities.Count, _options.MaxReasoningCycles); cycle++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _reasoningCycles);
            Telemetry.OperationsReasoningCycles.Add(1);

            string capability = agentCapabilities[cycle];

            // Query data fabric for capability-specific context
            Interlocked.Increment(ref _dataFabricQueries);
            Telemetry.OperationsDataFabricQueries.Add(1);

            var contextData = await _dataFabric.QueryEnterpriseDataAsync(
                source: "*",
                filters: new Dictionary<string, string> { ["capability"] = capability },
                schemaMapping: new Dictionary<string, string>(),
                permissions: _options.DataFabricPermissions,
                consumerType: _options.DefaultConsumerType,
                cancellationToken);

            if (contextData.IsAllowed)
            {
                stepsCompleted++;
                outputs[$"step.{cycle}.capability"] = capability;
                outputs[$"step.{cycle}.status"] = "completed";
                outputs[$"step.{cycle}.dataRows"] = contextData.Rows.Count.ToString();
            }
            else
            {
                stepsFailed++;
                errors.Add($"Step {cycle} ({capability}): data access denied - {contextData.Reason}");
                outputs[$"step.{cycle}.capability"] = capability;
                outputs[$"step.{cycle}.status"] = "failed";
            }
        }

        sw.Stop();
        bool isSuccess = stepsFailed == 0 && stepsCompleted > 0;

        Interlocked.Increment(ref _workflowsCoordinated);
        Telemetry.OperationsCoordinationDurationMs.Record(sw.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Workflow coordination {CoordinationId} completed: success={IsSuccess}, steps={Completed}/{Total}, duration={Duration}ms",
            coordinationId, isSuccess, stepsCompleted, stepsCompleted + stepsFailed, sw.ElapsedMilliseconds);

        await EmitAuditEventAsync("operations.workflow.coordinated", coordinationId.ToString(), cancellationToken);

        return new WorkflowCoordinationResult(
            coordinationId, isSuccess, workflowTemplate,
            agentCapabilities.Count, stepsCompleted, stepsFailed,
            outputs, errors, sw.Elapsed, DateTimeOffset.UtcNow);
    }

    public OperationsStatus GetStatus()
    {
        return new OperationsStatus(
            IsActive: true,
            WorkflowsAnalyzed: Interlocked.Read(ref _workflowsAnalyzed),
            InefficienciesDetected: Interlocked.Read(ref _inefficienciesDetected),
            RecommendationsGenerated: Interlocked.Read(ref _recommendationsGenerated),
            WorkflowsCoordinated: Interlocked.Read(ref _workflowsCoordinated),
            ReasoningCycles: Interlocked.Read(ref _reasoningCycles),
            DataFabricQueries: Interlocked.Read(ref _dataFabricQueries),
            StrategyLookups: Interlocked.Read(ref _strategyLookups),
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task EmitAuditEventAsync(
        string eventType, string entityId, CancellationToken cancellationToken)
    {
        var auditEvent = new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: eventType,
            Source: "operations-engine",
            CorrelationId: Guid.NewGuid(),
            Payload: new Dictionary<string, string>
            {
                ["entityId"] = entityId,
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
            },
            OccurredAtUtc: DateTimeOffset.UtcNow);

        await _eventBus.PublishAsync(auditEvent, cancellationToken);
    }
}
