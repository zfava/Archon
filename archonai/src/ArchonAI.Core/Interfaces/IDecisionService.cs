using ArchonAI.Core.Models.Decisions;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Core.Interfaces;

public interface IDecisionService
{
    Task<DecisionRecord> CreateAsync(DecisionRecord decision, CancellationToken ct = default);
    Task<DecisionRecord?> GetAsync(Guid decisionId, CancellationToken ct = default);
    Task<IReadOnlyList<DecisionRecord>> ListAsync(Guid tenantId, string? domain = null,
        DecisionStatus? status = null, int limit = 50, CancellationToken ct = default);
    Task<DecisionRecord?> UpdateStatusAsync(Guid decisionId, DecisionStatus newStatus,
        string actor, string? detail = null, CancellationToken ct = default);
    Task<DecisionRecord?> LinkArtifactAsync(Guid decisionId, DecisionLink link,
        CancellationToken ct = default);
    Task<IReadOnlyList<DecisionLifecycleEvent>> GetHistoryAsync(Guid decisionId,
        CancellationToken ct = default);
}
