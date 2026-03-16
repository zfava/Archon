using ArchonAI.Core.Models.Workflow;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Service for designing, validating, simulating, and exporting workflow graphs.
/// </summary>
public interface IWorkflowDesignerService
{
    /// <summary>Creates a new workflow graph from a set of nodes and edges.</summary>
    global::System.Threading.Tasks.Task<WorkflowGraph> CreateWorkflowGraphAsync(
        string name,
        string description,
        IReadOnlyList<WorkflowNode> nodes,
        IReadOnlyList<WorkflowEdge> edges,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>Retrieves an existing workflow graph by id.</summary>
    global::System.Threading.Tasks.Task<WorkflowGraph?> GetWorkflowGraphAsync(
        Guid graphId,
        CancellationToken ct = default);

    /// <summary>Lists workflow graphs with optional name filter and pagination.</summary>
    global::System.Threading.Tasks.Task<IReadOnlyList<WorkflowGraph>> ListWorkflowGraphsAsync(
        string? nameFilter = null,
        int offset = 0,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>Validates a workflow graph for structural correctness, invalid loops, and node compatibility.</summary>
    global::System.Threading.Tasks.Task<WorkflowGraphValidationResult> ValidateWorkflowGraphAsync(
        Guid graphId,
        CancellationToken ct = default);

    /// <summary>Simulates execution of a workflow graph without side effects.</summary>
    global::System.Threading.Tasks.Task<WorkflowSimulationResult> SimulateWorkflowGraphAsync(
        Guid graphId,
        CancellationToken ct = default);

    /// <summary>Exports a workflow graph as a JSON-serializable definition.</summary>
    global::System.Threading.Tasks.Task<WorkflowExportDefinition> ExportWorkflowGraphAsync(
        Guid graphId,
        string version = "1.0",
        CancellationToken ct = default);

    /// <summary>Deletes a workflow graph.</summary>
    global::System.Threading.Tasks.Task<bool> DeleteWorkflowGraphAsync(
        Guid graphId,
        CancellationToken ct = default);
}
