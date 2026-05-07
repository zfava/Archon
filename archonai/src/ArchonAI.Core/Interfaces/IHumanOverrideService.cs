using ArchonAI.Core.Models.HumanOverride;

namespace ArchonAI.Core.Interfaces;

public interface IHumanOverrideService
{
    Task<OverrideResult> PauseWorkflowAsync(PauseWorkflowRequest request, CancellationToken ct = default);
    Task<OverrideResult> ResumeWorkflowAsync(ResumeWorkflowRequest request, CancellationToken ct = default);
    Task<OverrideResult> CancelActionAsync(CancelActionRequest request, CancellationToken ct = default);
    Task<OverrideResult> ModifyStrategyAsync(ModifyStrategyRequest request, CancellationToken ct = default);
    Task<OverrideResult> RollbackAsync(RollbackRequest request, CancellationToken ct = default);
    Task<OverrideLog> GetOverrideLogAsync(Guid? workflowId = null, int limit = 100, CancellationToken ct = default);
}
