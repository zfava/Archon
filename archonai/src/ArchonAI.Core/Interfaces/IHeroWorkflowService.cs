using ArchonAI.Core.Models.HeroWorkflow;

namespace ArchonAI.Core.Interfaces;

public interface IHeroWorkflowService
{
    /// <summary>List all available hero workflow definitions.</summary>
    Task<IReadOnlyList<HeroWorkflowDefinition>> GetCatalogAsync(CancellationToken ct = default);

    /// <summary>Get a specific workflow definition by type.</summary>
    Task<HeroWorkflowDefinition?> GetDefinitionAsync(string workflowType, CancellationToken ct = default);

    /// <summary>Start a new hero workflow instance.</summary>
    Task<HeroWorkflowInstance> StartAsync(
        Guid tenantId, string workflowType, string title,
        IReadOnlyDictionary<string, string> initialInputs,
        string initiatedBy, CancellationToken ct = default);

    /// <summary>Advance the workflow to the next step.</summary>
    Task<HeroWorkflowInstance?> AdvanceAsync(
        Guid workflowId, Guid tenantId,
        IReadOnlyDictionary<string, string>? stepInputs,
        string actor, CancellationToken ct = default);

    /// <summary>Get a workflow instance.</summary>
    Task<HeroWorkflowInstance?> GetAsync(
        Guid workflowId, Guid tenantId, CancellationToken ct = default);

    /// <summary>List workflow instances for a tenant.</summary>
    Task<IReadOnlyList<HeroWorkflowSummary>> ListAsync(
        Guid tenantId, string? workflowType = null,
        HeroWorkflowStatus? status = null,
        int limit = 50, CancellationToken ct = default);

    /// <summary>Cancel a workflow.</summary>
    Task<HeroWorkflowInstance?> CancelAsync(
        Guid workflowId, Guid tenantId, string actor,
        CancellationToken ct = default);
}
