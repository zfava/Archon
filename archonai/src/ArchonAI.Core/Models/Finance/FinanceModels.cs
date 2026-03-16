namespace ArchonAI.Core.Models.Finance;

public sealed record FinancialAnalysisResult(
    bool IsSuccess,
    string Scope,
    double RevenueGrowthRate,
    double ExpenseGrowthRate,
    double ProfitMargin,
    IReadOnlyList<string> KeyFindings,
    IReadOnlyList<string> Risks,
    IReadOnlyDictionary<string, string> Metrics,
    DateTimeOffset AnalyzedAtUtc);

public sealed record FinancialAnomaly(
    Guid Id,
    string AnomalyType,
    string Title,
    string Description,
    double Severity,
    double DeviationPercent,
    string AffectedAccount,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset DetectedAtUtc);

public sealed record FinancialSummary(
    string Scope,
    string Period,
    double TotalRevenue,
    double TotalExpenses,
    double NetIncome,
    double ProfitMargin,
    IReadOnlyList<FinancialLineItem> TopRevenueItems,
    IReadOnlyList<FinancialLineItem> TopExpenseItems,
    IReadOnlyDictionary<string, string> AdditionalMetrics,
    DateTimeOffset GeneratedAtUtc);

public sealed record FinancialLineItem(
    string Category,
    string Description,
    double Amount,
    double PercentOfTotal);

public sealed record BudgetWorkflowResult(
    Guid WorkflowId,
    bool IsSuccess,
    string DepartmentId,
    double AllocatedBudget,
    double RecommendedBudget,
    double Variance,
    IReadOnlyList<BudgetRecommendation> Recommendations,
    IReadOnlyDictionary<string, string> Outputs,
    DateTimeOffset CompletedAtUtc);

public sealed record BudgetRecommendation(
    string Category,
    string Recommendation,
    double CurrentAmount,
    double SuggestedAmount,
    double ExpectedSavings);

public sealed record FinanceEngineStatus(
    bool IsActive,
    long AnalysesPerformed,
    long AnomaliesDetected,
    long SummariesGenerated,
    long BudgetWorkflows,
    long DataFabricQueries,
    long AuditEventsEmitted,
    DateTimeOffset StatusAsOfUtc);
