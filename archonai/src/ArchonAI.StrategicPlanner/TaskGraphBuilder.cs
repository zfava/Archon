using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using Microsoft.Extensions.Logging;
using CoreTask = ArchonAI.Core.Models.Task;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;

namespace ArchonAI.StrategicPlanner;

/// <summary>
/// Converts operational goals into executable task graphs with dependency edges,
/// then dispatches them to AgentRuntime for execution.
/// </summary>
public sealed class TaskGraphBuilder : ITaskGraphBuilder
{
    private readonly IRuntime _runtime;
    private readonly IWorkflowExecutionEngine _workflowEngine;
    private readonly IEventBus _eventBus;
    private readonly ILogger<TaskGraphBuilder> _logger;

    private readonly ConcurrentDictionary<Guid, TaskGraph> _graphs = new();

    public TaskGraphBuilder(
        IRuntime runtime,
        IWorkflowExecutionEngine workflowEngine,
        IEventBus eventBus,
        ILogger<TaskGraphBuilder> logger)
    {
        _runtime = runtime;
        _workflowEngine = workflowEngine;
        _eventBus = eventBus;
        _logger = logger;
    }

    public global::System.Threading.Tasks.Task<TaskGraph> BuildGraphAsync(
        OperationalGoal goal,
        string strategy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nodes = new List<TaskGraphNode>();
        var edges = new List<TaskGraphEdge>();

        // ── Analysis node (root) ────────────────────────────────
        var analysisNode = CreateNode(
            name: "Goal Analysis",
            description: $"Analyze goal '{goal.Title}' to determine execution approach.",
            agentType: "context-analysis",
            requiredInputs: new Dictionary<string, string>
            {
                ["goalId"] = goal.GoalId.ToString(),
                ["goalTitle"] = goal.Title,
                ["department"] = goal.Department,
                ["priority"] = goal.Priority.ToString(),
                ["expectedImpact"] = goal.ExpectedImpact
            },
            expectedOutput: "analysis_report",
            priority: 1,
            estimatedHours: 0.5);
        nodes.Add(analysisNode);

        // ── Build domain-specific subgraph based on goal source and department ──
        switch (goal.Source)
        {
            case GoalSource.StateAnomaly:
                BuildStateAnomalySubgraph(goal, strategy, analysisNode.NodeId, nodes, edges);
                break;
            case GoalSource.BusinessSignal:
                BuildBusinessSignalSubgraph(goal, strategy, analysisNode.NodeId, nodes, edges);
                break;
            case GoalSource.PerformanceTrend:
                BuildPerformanceTrendSubgraph(goal, strategy, analysisNode.NodeId, nodes, edges);
                break;
            default:
                BuildDefaultSubgraph(goal, strategy, analysisNode.NodeId, nodes, edges);
                break;
        }

        // ── Evaluation node (terminal) ──────────────────────────
        var evaluationNode = CreateNode(
            name: "Outcome Evaluation",
            description: "Evaluate execution outcomes and capture feedback for future optimization.",
            agentType: "outcome-validation",
            requiredInputs: new Dictionary<string, string>
            {
                ["goalId"] = goal.GoalId.ToString(),
                ["strategy"] = strategy,
                ["feedbackMode"] = "adaptive"
            },
            expectedOutput: "evaluation_report",
            priority: 100,
            estimatedHours: 0.25);
        nodes.Add(evaluationNode);

        // Connect all leaf nodes (no outgoing edges) to evaluation
        var sourceIds = new HashSet<Guid>(edges.Select(e => e.SourceNodeId));
        var leafNodes = nodes
            .Where(n => n.NodeId != evaluationNode.NodeId && !sourceIds.Contains(n.NodeId))
            .ToList();

        foreach (var leaf in leafNodes)
        {
            edges.Add(CreateEdge(leaf.NodeId, evaluationNode.NodeId, "result→evaluation_input", isRequired: true));
        }

        var graph = new TaskGraph(
            GraphId: Guid.NewGuid(),
            GoalId: goal.GoalId,
            GoalTitle: goal.Title,
            Nodes: nodes,
            Edges: edges,
            Strategy: strategy,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        _graphs[graph.GraphId] = graph;

        _logger.LogInformation(
            "Built task graph {GraphId} for goal '{GoalTitle}': {Nodes} nodes, {Edges} edges, {Layers} layers",
            graph.GraphId, goal.Title, nodes.Count, edges.Count, graph.GetExecutionLayers().Count);

        return global::System.Threading.Tasks.Task.FromResult(graph);
    }

    public async global::System.Threading.Tasks.Task<TaskGraphDispatchResult> DispatchGraphAsync(
        TaskGraph graph,
        CancellationToken cancellationToken = default)
    {
        var workflowId = graph.GraphId;
        await _workflowEngine.InitializeWorkflowAsync(workflowId, cancellationToken);

        var layers = graph.GetExecutionLayers();
        var scheduledTaskIds = new List<Guid>();
        var coreTasks = new List<CoreTask>();

        int orderBase = 0;
        foreach (var layer in layers)
        {
            foreach (var node in layer)
            {
                var coreTask = new CoreTask(
                    Id: node.NodeId,
                    ObjectiveId: graph.GoalId,
                    Order: orderBase + node.Priority,
                    Name: node.Name,
                    Description: node.Description,
                    RequiredCapability: node.AgentType,
                    Inputs: new Dictionary<string, string>(node.RequiredInputs)
                    {
                        ["graphId"] = graph.GraphId.ToString(),
                        ["strategy"] = graph.Strategy,
                        ["expectedOutput"] = node.ExpectedOutput
                    },
                    CreatedAtUtc: DateTimeOffset.UtcNow,
                    StartedAtUtc: null,
                    CompletedAtUtc: null);

                coreTasks.Add(coreTask);
                scheduledTaskIds.Add(coreTask.Id);
            }

            orderBase += 1000;
        }

        // Queue tasks in the workflow engine
        await _workflowEngine.CreateTaskQueueAsync(workflowId, coreTasks, cancellationToken);

        // Schedule each task in the runtime for execution
        foreach (var task in coreTasks)
        {
            await _runtime.ScheduleTaskAsync(task, cancellationToken);
        }

        // Publish dispatch event
        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: "strategic.taskgraph.dispatched",
            Source: "task-graph-builder",
            CorrelationId: graph.GoalId,
            Payload: new Dictionary<string, string>
            {
                ["graphId"] = graph.GraphId.ToString(),
                ["goalId"] = graph.GoalId.ToString(),
                ["totalTasks"] = coreTasks.Count.ToString(),
                ["executionLayers"] = layers.Count.ToString(),
                ["strategy"] = graph.Strategy
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        _logger.LogInformation(
            "Dispatched task graph {GraphId} to runtime: {Tasks} tasks across {Layers} layers",
            graph.GraphId, coreTasks.Count, layers.Count);

        return new TaskGraphDispatchResult(
            GraphId: graph.GraphId,
            WorkflowId: workflowId,
            TotalTasks: coreTasks.Count,
            ExecutionLayers: layers.Count,
            ScheduledTaskIds: scheduledTaskIds,
            DispatchedAtUtc: DateTimeOffset.UtcNow);
    }

    public global::System.Threading.Tasks.Task<TaskGraph?> GetGraphAsync(
        Guid graphId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _graphs.TryGetValue(graphId, out var graph);
        return global::System.Threading.Tasks.Task.FromResult(graph);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<TaskGraph>> GetGraphsByGoalAsync(
        Guid goalId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TaskGraph> result = _graphs.Values
            .Where(g => g.GoalId == goalId)
            .OrderByDescending(g => g.CreatedAtUtc)
            .ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    // ══════════════════════════════════════════════════════════════
    //  Domain-specific subgraph builders
    // ══════════════════════════════════════════════════════════════

    private void BuildStateAnomalySubgraph(
        OperationalGoal goal,
        string strategy,
        Guid analysisNodeId,
        List<TaskGraphNode> nodes,
        List<TaskGraphEdge> edges)
    {
        // Diagnose → Remediate (parallel branches possible) → Verify
        var diagnoseNode = CreateNode(
            name: "Anomaly Diagnosis",
            description: $"Diagnose root cause of anomaly in {goal.Department}.",
            agentType: "context-analysis",
            requiredInputs: new Dictionary<string, string>
            {
                ["department"] = goal.Department,
                ["anomalyContext"] = goal.Context.GetValueOrDefault("currentHealthScore", "unknown")
            },
            expectedOutput: "diagnosis_report",
            priority: 10,
            estimatedHours: 1.0);
        nodes.Add(diagnoseNode);
        edges.Add(CreateEdge(analysisNodeId, diagnoseNode.NodeId, "analysis_report→diagnosis_input", isRequired: true));

        var remediateNode = CreateNode(
            name: "Execute Remediation",
            description: $"Apply corrective actions for {goal.Department}: {goal.Description}",
            agentType: SelectAgentForDepartment(goal.Department, strategy),
            requiredInputs: new Dictionary<string, string>
            {
                ["department"] = goal.Department,
                ["strategy"] = strategy,
                ["goalTitle"] = goal.Title
            },
            expectedOutput: "remediation_result",
            priority: 20,
            estimatedHours: 2.0);
        nodes.Add(remediateNode);
        edges.Add(CreateEdge(diagnoseNode.NodeId, remediateNode.NodeId, "diagnosis_report→remediation_plan", isRequired: true));

        var verifyNode = CreateNode(
            name: "Verify Resolution",
            description: "Confirm anomaly has been resolved and metrics are within thresholds.",
            agentType: "outcome-validation",
            requiredInputs: new Dictionary<string, string>
            {
                ["department"] = goal.Department,
                ["expectedImpact"] = goal.ExpectedImpact
            },
            expectedOutput: "verification_result",
            priority: 30,
            estimatedHours: 0.5);
        nodes.Add(verifyNode);
        edges.Add(CreateEdge(remediateNode.NodeId, verifyNode.NodeId, "remediation_result→verification_input", isRequired: true));
    }

    private void BuildBusinessSignalSubgraph(
        OperationalGoal goal,
        string strategy,
        Guid analysisNodeId,
        List<TaskGraphNode> nodes,
        List<TaskGraphEdge> edges)
    {
        // Data Gathering → Strategy Formation → Execution (parallel: operations + communications) → Measurement
        var dataNode = CreateNode(
            name: "Signal Data Gathering",
            description: $"Collect relevant data for business signal in {goal.Department}.",
            agentType: "context-analysis",
            requiredInputs: new Dictionary<string, string>
            {
                ["department"] = goal.Department,
                ["signalCategory"] = goal.Context.GetValueOrDefault("category", "general")
            },
            expectedOutput: "signal_data",
            priority: 10,
            estimatedHours: 1.0);
        nodes.Add(dataNode);
        edges.Add(CreateEdge(analysisNodeId, dataNode.NodeId, "analysis_report→data_query", isRequired: true));

        var strategyNode = CreateNode(
            name: "Strategy Formation",
            description: "Formulate action strategy based on gathered signal data.",
            agentType: "workflow-orchestration",
            requiredInputs: new Dictionary<string, string>
            {
                ["strategy"] = strategy,
                ["department"] = goal.Department,
                ["goalTitle"] = goal.Title
            },
            expectedOutput: "action_strategy",
            priority: 20,
            estimatedHours: 1.0);
        nodes.Add(strategyNode);
        edges.Add(CreateEdge(dataNode.NodeId, strategyNode.NodeId, "signal_data→strategy_input", isRequired: true));

        var opsNode = CreateNode(
            name: "Operational Execution",
            description: $"Execute operational changes for: {goal.Title}",
            agentType: SelectAgentForDepartment(goal.Department, strategy),
            requiredInputs: new Dictionary<string, string>
            {
                ["department"] = goal.Department,
                ["strategy"] = strategy
            },
            expectedOutput: "ops_result",
            priority: 30,
            estimatedHours: 3.0);
        nodes.Add(opsNode);
        edges.Add(CreateEdge(strategyNode.NodeId, opsNode.NodeId, "action_strategy→ops_plan", isRequired: true));

        var commsNode = CreateNode(
            name: "Stakeholder Communication",
            description: "Notify relevant stakeholders and coordinate cross-department actions.",
            agentType: "workflow-orchestration",
            requiredInputs: new Dictionary<string, string>
            {
                ["department"] = goal.Department,
                ["goalTitle"] = goal.Title
            },
            expectedOutput: "comms_result",
            priority: 30,
            estimatedHours: 0.5);
        nodes.Add(commsNode);
        edges.Add(CreateEdge(strategyNode.NodeId, commsNode.NodeId, "action_strategy→comms_plan", isRequired: false));

        var measureNode = CreateNode(
            name: "Impact Measurement",
            description: "Measure the impact of executed actions against expected outcomes.",
            agentType: "outcome-validation",
            requiredInputs: new Dictionary<string, string>
            {
                ["expectedImpact"] = goal.ExpectedImpact,
                ["department"] = goal.Department
            },
            expectedOutput: "impact_measurement",
            priority: 40,
            estimatedHours: 0.5);
        nodes.Add(measureNode);
        edges.Add(CreateEdge(opsNode.NodeId, measureNode.NodeId, "ops_result→measurement_input", isRequired: true));
        edges.Add(CreateEdge(commsNode.NodeId, measureNode.NodeId, "comms_result→measurement_context", isRequired: false));
    }

    private void BuildPerformanceTrendSubgraph(
        OperationalGoal goal,
        string strategy,
        Guid analysisNodeId,
        List<TaskGraphNode> nodes,
        List<TaskGraphEdge> edges)
    {
        // Trend Analysis → Optimization Planning → Apply Optimization → Validate
        var trendNode = CreateNode(
            name: "Deep Trend Analysis",
            description: "Perform detailed analysis of performance trend data and identify root causes.",
            agentType: "context-analysis",
            requiredInputs: new Dictionary<string, string>
            {
                ["category"] = goal.Context.GetValueOrDefault("category", "general"),
                ["target"] = goal.Context.GetValueOrDefault("target", "unknown")
            },
            expectedOutput: "trend_analysis",
            priority: 10,
            estimatedHours: 1.5);
        nodes.Add(trendNode);
        edges.Add(CreateEdge(analysisNodeId, trendNode.NodeId, "analysis_report→trend_query", isRequired: true));

        var optimizeNode = CreateNode(
            name: "Optimization Planning",
            description: "Design optimization plan based on trend analysis findings.",
            agentType: "workflow-orchestration",
            requiredInputs: new Dictionary<string, string>
            {
                ["strategy"] = strategy,
                ["expectedImpact"] = goal.Context.GetValueOrDefault("expectedImpact", "0")
            },
            expectedOutput: "optimization_plan",
            priority: 20,
            estimatedHours: 1.0);
        nodes.Add(optimizeNode);
        edges.Add(CreateEdge(trendNode.NodeId, optimizeNode.NodeId, "trend_analysis→optimization_input", isRequired: true));

        var applyNode = CreateNode(
            name: "Apply Optimization",
            description: $"Execute optimizations: {goal.Description}",
            agentType: "operation-execution",
            requiredInputs: new Dictionary<string, string>
            {
                ["strategy"] = strategy,
                ["department"] = goal.Department
            },
            expectedOutput: "optimization_result",
            priority: 30,
            estimatedHours: 2.0);
        nodes.Add(applyNode);
        edges.Add(CreateEdge(optimizeNode.NodeId, applyNode.NodeId, "optimization_plan→execution_input", isRequired: true));

        var validateNode = CreateNode(
            name: "Performance Validation",
            description: "Validate that optimizations achieved expected performance improvements.",
            agentType: "outcome-validation",
            requiredInputs: new Dictionary<string, string>
            {
                ["expectedImpact"] = goal.ExpectedImpact,
                ["target"] = goal.Context.GetValueOrDefault("target", "unknown")
            },
            expectedOutput: "validation_result",
            priority: 40,
            estimatedHours: 0.5);
        nodes.Add(validateNode);
        edges.Add(CreateEdge(applyNode.NodeId, validateNode.NodeId, "optimization_result→validation_input", isRequired: true));
    }

    private void BuildDefaultSubgraph(
        OperationalGoal goal,
        string strategy,
        Guid analysisNodeId,
        List<TaskGraphNode> nodes,
        List<TaskGraphEdge> edges)
    {
        var planNode = CreateNode(
            name: "Operational Planning",
            description: "Design operational tasks aligned to goal.",
            agentType: "workflow-orchestration",
            requiredInputs: new Dictionary<string, string>
            {
                ["strategy"] = strategy,
                ["goalTitle"] = goal.Title
            },
            expectedOutput: "operational_plan",
            priority: 10,
            estimatedHours: 1.0);
        nodes.Add(planNode);
        edges.Add(CreateEdge(analysisNodeId, planNode.NodeId, "analysis_report→planning_input", isRequired: true));

        var executeNode = CreateNode(
            name: "Execution",
            description: goal.Description,
            agentType: SelectAgentForDepartment(goal.Department, strategy),
            requiredInputs: new Dictionary<string, string>
            {
                ["strategy"] = strategy,
                ["goalId"] = goal.GoalId.ToString()
            },
            expectedOutput: "execution_result",
            priority: 20,
            estimatedHours: 2.0);
        nodes.Add(executeNode);
        edges.Add(CreateEdge(planNode.NodeId, executeNode.NodeId, "operational_plan→execution_input", isRequired: true));
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private static TaskGraphNode CreateNode(
        string name,
        string description,
        string agentType,
        Dictionary<string, string> requiredInputs,
        string expectedOutput,
        int priority,
        double estimatedHours)
    {
        return new TaskGraphNode(
            NodeId: Guid.NewGuid(),
            Name: name,
            Description: description,
            AgentType: agentType,
            RequiredInputs: requiredInputs,
            ExpectedOutput: expectedOutput,
            Priority: priority,
            EstimatedDurationHours: estimatedHours,
            Status: TaskGraphNodeStatus.Pending,
            CreatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static TaskGraphEdge CreateEdge(
        Guid sourceNodeId,
        Guid targetNodeId,
        string outputToInputMapping,
        bool isRequired)
    {
        return new TaskGraphEdge(
            EdgeId: Guid.NewGuid(),
            SourceNodeId: sourceNodeId,
            TargetNodeId: targetNodeId,
            OutputToInputMapping: outputToInputMapping,
            IsRequired: isRequired);
    }

    private static string SelectAgentForDepartment(string department, string strategy)
    {
        if (strategy.Equals("safe-mode", StringComparison.OrdinalIgnoreCase))
            return "workflow-orchestration";

        return department.ToLowerInvariant() switch
        {
            "finance" => "financial-operations",
            "sales" => "operation-execution",
            "marketing" => "operation-execution",
            "logistics" => "operation-execution",
            "operations" => "operation-execution",
            _ => "operation-execution"
        };
    }
}
