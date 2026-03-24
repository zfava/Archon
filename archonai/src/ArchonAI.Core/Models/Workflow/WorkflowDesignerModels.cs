namespace ArchonAI.Core.Models.Workflow;

/// <summary>
/// Represents a node type in a workflow graph.
/// </summary>
public enum WorkflowNodeType
{
    /// <summary>A node that delegates work to an agent.</summary>
    AgentNode,
    /// <summary>A branching node that routes execution based on a decision.</summary>
    DecisionNode,
    /// <summary>A node that invokes an external tool.</summary>
    ToolNode,
    /// <summary>A node that evaluates a boolean condition to gate execution.</summary>
    ConditionNode
}

/// <summary>
/// A single node in a workflow graph.
/// </summary>
public sealed record WorkflowNode(
    Guid Id,
    string Name,
    string Description,
    WorkflowNodeType NodeType,
    IReadOnlyDictionary<string, string> Configuration);

/// <summary>
/// A directed edge connecting two nodes in a workflow graph.
/// </summary>
public sealed record WorkflowEdge(
    Guid Id,
    Guid SourceNodeId,
    Guid TargetNodeId,
    string? Label);

/// <summary>
/// A complete workflow graph definition composed of nodes and edges.
/// </summary>
public sealed record WorkflowGraph(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyList<WorkflowNode> Nodes,
    IReadOnlyList<WorkflowEdge> Edges,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Result of validating a workflow graph.
/// </summary>
public sealed record WorkflowGraphValidationResult(
    Guid GraphId,
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    DateTimeOffset ValidatedAtUtc);

/// <summary>
/// Result of simulating a workflow graph execution.
/// </summary>
public sealed record WorkflowSimulationResult(
    Guid GraphId,
    bool IsSuccess,
    IReadOnlyList<WorkflowSimulationStep> Steps,
    double TotalDurationMs,
    IReadOnlyList<string> Warnings,
    DateTimeOffset SimulatedAtUtc);

/// <summary>
/// A single step in a workflow simulation trace.
/// </summary>
public sealed record WorkflowSimulationStep(
    Guid NodeId,
    string NodeName,
    WorkflowNodeType NodeType,
    int Order,
    string Outcome,
    double DurationMs);

/// <summary>
/// A JSON-serializable workflow export definition.
/// </summary>
public sealed record WorkflowExportDefinition(
    Guid GraphId,
    string Name,
    string Description,
    string Version,
    IReadOnlyList<WorkflowNode> Nodes,
    IReadOnlyList<WorkflowEdge> Edges,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset ExportedAtUtc);
