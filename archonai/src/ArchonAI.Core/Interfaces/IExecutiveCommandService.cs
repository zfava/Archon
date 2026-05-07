using ArchonAI.Core.Models.ExecutiveCommand;

namespace ArchonAI.Core.Interfaces;

public interface IExecutiveCommandService
{
    Task<ExecutiveCommandSummary> GetCommandSummaryAsync(
        Guid tenantId, CancellationToken ct = default);
}
