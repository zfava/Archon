using ArchonAI.Core.Models.Decisions;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Core.Interfaces;

public interface IFinancialConsequenceService
{
    Task<FinancialConsequence> AttachAsync(FinancialConsequence consequence, CancellationToken ct = default);
    Task<FinancialConsequence?> GetByDecisionAsync(Guid decisionId, CancellationToken ct = default);
    Task<FinancialConsequence?> UpdateAsync(Guid decisionId, FinancialConsequence consequence, CancellationToken ct = default);
}
