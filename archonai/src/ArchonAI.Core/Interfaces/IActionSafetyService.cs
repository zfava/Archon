using ArchonAI.Core.Models.ActionSafety;

namespace ArchonAI.Core.Interfaces;

public interface IActionSafetyService
{
    /// <summary>Get or create the safety classification for an action type.</summary>
    Task<ActionSafetyClassification> GetClassificationAsync(
        string actionType, CancellationToken ct = default);

    /// <summary>Set the safety classification for an action type.</summary>
    Task<ActionSafetyClassification> SetClassificationAsync(
        ActionSafetyClassification classification, CancellationToken ct = default);

    /// <summary>List all registered safety classifications.</summary>
    Task<IReadOnlyList<ActionSafetyClassification>> ListClassificationsAsync(
        CancellationToken ct = default);

    /// <summary>Record a governed action execution.</summary>
    Task<GovernedActionRecord> RecordActionAsync(
        GovernedActionRecord action, CancellationToken ct = default);

    /// <summary>Get a governed action by ID.</summary>
    Task<GovernedActionRecord?> GetActionAsync(
        Guid actionId, CancellationToken ct = default);

    /// <summary>List governed actions for a tenant.</summary>
    Task<IReadOnlyList<GovernedActionRecord>> ListActionsAsync(
        Guid tenantId, int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Attempt rollback of a governed action. Returns the updated record.
    /// Blocks if action is irreversible or rollback window has expired.
    /// </summary>
    Task<GovernedActionRecord> AttemptRollbackAsync(
        Guid actionId, string initiatedBy, CancellationToken ct = default);

    /// <summary>Get rollback summary for a tenant.</summary>
    Task<RollbackSummary> GetRollbackSummaryAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Get an explicit classification if one exists, otherwise infer a default
    /// classification based on action type keywords. Inferred classifications are
    /// transient (ClassifiedBy = "auto-inference") and not persisted unless an
    /// operator explicitly saves them.
    /// </summary>
    Task<ActionSafetyClassification> GetOrInferClassificationAsync(
        string actionType, string tenantId, CancellationToken ct = default);
}
