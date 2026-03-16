using ArchonAI.Core.Models.Finance;

namespace ArchonAI.Core.Interfaces;

public interface IFinanceEngine
{
    global::System.Threading.Tasks.Task<FinancialAnalysisResult> AnalyzePerformanceAsync(
        string scope,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<FinancialAnomaly>> DetectAnomaliesAsync(
        string scope,
        double sensitivityThreshold = 0.7,
        int maxResults = 20,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<FinancialSummary> GenerateSummaryAsync(
        string scope,
        string period,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<BudgetWorkflowResult> AssistBudgetingAsync(
        string departmentId,
        IReadOnlyDictionary<string, string> budgetParameters,
        CancellationToken cancellationToken = default);

    FinanceEngineStatus GetStatus();
}
