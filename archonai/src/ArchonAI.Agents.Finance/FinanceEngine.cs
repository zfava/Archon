using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.DataFabric;
using ArchonAI.Core.Models.Finance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Agents.Finance;

public sealed class FinanceEngine : IFinanceEngine
{
    private readonly IDataFabricEngine _dataFabric;
    private readonly IEventBus _eventBus;
    private readonly ILogger<FinanceEngine> _logger;
    private readonly FinanceOptions _options;

    private long _analysesPerformed;
    private long _anomaliesDetected;
    private long _summariesGenerated;
    private long _budgetWorkflows;
    private long _dataFabricQueries;
    private long _auditEventsEmitted;

    public FinanceEngine(
        IDataFabricEngine dataFabric,
        IEventBus eventBus,
        ILogger<FinanceEngine> logger,
        IOptions<FinanceOptions> options)
    {
        _dataFabric = dataFabric;
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
    }

    public async global::System.Threading.Tasks.Task<FinancialAnalysisResult> AnalyzePerformanceAsync(
        string scope,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Finance.AnalyzePerformance");
        activity?.SetTag("finance.scope", scope);

        Telemetry.FinanceAnalyses.Add(1);

        var fabricResult = await QueryFinancialDataAsync(scope, parameters, cancellationToken);

        // Extract financial metrics from data fabric rows
        double totalRevenue = 0, totalExpenses = 0;
        double prevRevenue = 0, prevExpenses = 0;

        foreach (var row in fabricResult.Rows)
        {
            if (row.TryGetValue("revenue", out var rev) && double.TryParse(rev, out var r))
                totalRevenue += r;
            if (row.TryGetValue("expenses", out var exp) && double.TryParse(exp, out var e))
                totalExpenses += e;
            if (row.TryGetValue("prevRevenue", out var pr) && double.TryParse(pr, out var prv))
                prevRevenue += prv;
            if (row.TryGetValue("prevExpenses", out var pe) && double.TryParse(pe, out var pev))
                prevExpenses += pev;
        }

        double revenueGrowth = prevRevenue > 0 ? (totalRevenue - prevRevenue) / prevRevenue : 0;
        double expenseGrowth = prevExpenses > 0 ? (totalExpenses - prevExpenses) / prevExpenses : 0;
        double profitMargin = totalRevenue > 0 ? (totalRevenue - totalExpenses) / totalRevenue : 0;

        var findings = new List<string>();
        var risks = new List<string>();

        if (revenueGrowth < 0)
            findings.Add($"Revenue declined by {Math.Abs(revenueGrowth):P1}");
        else if (revenueGrowth > 0)
            findings.Add($"Revenue grew by {revenueGrowth:P1}");

        if (expenseGrowth > revenueGrowth && revenueGrowth > 0)
            risks.Add("Expenses growing faster than revenue");

        if (profitMargin < 0.1)
            risks.Add($"Low profit margin: {profitMargin:P1}");

        if (profitMargin < 0)
            risks.Add("Operating at a loss");

        var metrics = new Dictionary<string, string>
        {
            ["totalRevenue"] = totalRevenue.ToString("F2"),
            ["totalExpenses"] = totalExpenses.ToString("F2"),
            ["netIncome"] = (totalRevenue - totalExpenses).ToString("F2"),
            ["dataRows"] = fabricResult.Rows.Count.ToString()
        };

        Interlocked.Increment(ref _analysesPerformed);
        _logger.LogInformation(
            "Financial analysis for scope '{Scope}': margin={Margin:P1}, findings={Findings}, risks={Risks}",
            scope, profitMargin, findings.Count, risks.Count);

        await EmitAuditEventAsync("finance.performance.analyzed", scope, cancellationToken);

        return new FinancialAnalysisResult(
            true, scope, revenueGrowth, expenseGrowth, profitMargin,
            findings, risks, metrics, DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<FinancialAnomaly>> DetectAnomaliesAsync(
        string scope,
        double sensitivityThreshold = 0.7,
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Finance.DetectAnomalies");

        Telemetry.FinanceAnomalyScans.Add(1);

        var fabricResult = await QueryFinancialDataAsync(scope, new Dictionary<string, string>(), cancellationToken);

        var anomalies = new List<FinancialAnomaly>();

        // Calculate averages for deviation detection
        var amounts = fabricResult.Rows
            .Where(r => r.TryGetValue("amount", out _))
            .Select(r => double.TryParse(r["amount"], out var a) ? a : 0)
            .ToList();

        if (amounts.Count > 2)
        {
            double avg = amounts.Average();
            double stdDev = Math.Sqrt(amounts.Average(a => Math.Pow(a - avg, 2)));

            if (stdDev > 0)
            {
                foreach (var row in fabricResult.Rows)
                {
                    if (!row.TryGetValue("amount", out var amtStr) || !double.TryParse(amtStr, out var amount))
                        continue;

                    double deviation = Math.Abs(amount - avg) / stdDev;
                    double deviationPercent = avg != 0 ? Math.Abs(amount - avg) / Math.Abs(avg) : 0;

                    if (deviation < 2.0 * sensitivityThreshold)
                        continue;

                    string anomalyType = amount > avg ? "unusually-high" : "unusually-low";
                    string account = row.GetValueOrDefault("account", "unknown");

                    anomalies.Add(new FinancialAnomaly(
                        Guid.NewGuid(),
                        anomalyType,
                        $"Anomalous {anomalyType} amount in '{account}'",
                        $"Amount {amount:F2} deviates {deviationPercent:P1} from average {avg:F2} (z-score={deviation:F2})",
                        Math.Min(1.0, deviation / 3.0),
                        deviationPercent,
                        account,
                        new Dictionary<string, string>
                        {
                            ["amount"] = amount.ToString("F2"),
                            ["average"] = avg.ToString("F2"),
                            ["stdDev"] = stdDev.ToString("F2"),
                            ["zScore"] = deviation.ToString("F2")
                        },
                        DateTimeOffset.UtcNow));
                }
            }
        }

        // Check for duplicate transaction patterns
        var duplicateGroups = fabricResult.Rows
            .Where(r => r.TryGetValue("amount", out _) && r.TryGetValue("description", out _))
            .GroupBy(r => $"{r["amount"]}:{r["description"]}")
            .Where(g => g.Count() > 2)
            .ToList();

        foreach (var group in duplicateGroups)
        {
            anomalies.Add(new FinancialAnomaly(
                Guid.NewGuid(),
                "potential-duplicate",
                $"Potential duplicate transactions ({group.Count()} occurrences)",
                $"Found {group.Count()} transactions with identical amount and description",
                0.6,
                0,
                group.First().GetValueOrDefault("account", "unknown"),
                new Dictionary<string, string> { ["count"] = group.Count().ToString() },
                DateTimeOffset.UtcNow));
        }

        int clampedMax = Math.Clamp(maxResults, 1, _options.MaxAnomaliesPerScan);
        var result = anomalies.OrderByDescending(a => a.Severity).Take(clampedMax).ToList();

        Interlocked.Add(ref _anomaliesDetected, result.Count);
        _logger.LogInformation("Detected {Count} anomalies in scope '{Scope}'", result.Count, scope);

        return result;
    }

    public async global::System.Threading.Tasks.Task<FinancialSummary> GenerateSummaryAsync(
        string scope,
        string period,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Finance.GenerateSummary");

        Telemetry.FinanceSummaries.Add(1);

        var filters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(period))
            filters["period"] = period;

        var fabricResult = await QueryFinancialDataAsync(scope, filters, cancellationToken);

        double totalRevenue = 0, totalExpenses = 0;
        var revenueItems = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var expenseItems = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in fabricResult.Rows)
        {
            string category = row.GetValueOrDefault("category", "Uncategorized");
            string type = row.GetValueOrDefault("type", "revenue");

            if (row.TryGetValue("amount", out var amtStr) && double.TryParse(amtStr, out var amount))
            {
                if (type.Equals("revenue", StringComparison.OrdinalIgnoreCase))
                {
                    totalRevenue += amount;
                    revenueItems[category] = revenueItems.GetValueOrDefault(category) + amount;
                }
                else
                {
                    totalExpenses += amount;
                    expenseItems[category] = expenseItems.GetValueOrDefault(category) + amount;
                }
            }
        }

        double netIncome = totalRevenue - totalExpenses;
        double profitMargin = totalRevenue > 0 ? netIncome / totalRevenue : 0;

        var topRevenue = revenueItems
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => new FinancialLineItem(
                kv.Key, $"Revenue from {kv.Key}", kv.Value,
                totalRevenue > 0 ? kv.Value / totalRevenue : 0))
            .ToList();

        var topExpenses = expenseItems
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => new FinancialLineItem(
                kv.Key, $"Expense for {kv.Key}", kv.Value,
                totalExpenses > 0 ? kv.Value / totalExpenses : 0))
            .ToList();

        Interlocked.Increment(ref _summariesGenerated);
        _logger.LogInformation(
            "Financial summary for scope '{Scope}' period '{Period}': revenue={Revenue:F2}, expenses={Expenses:F2}",
            scope, period, totalRevenue, totalExpenses);

        await EmitAuditEventAsync("finance.summary.generated", scope, cancellationToken);

        return new FinancialSummary(
            scope, period, totalRevenue, totalExpenses, netIncome, profitMargin,
            topRevenue, topExpenses,
            new Dictionary<string, string>
            {
                ["dataRows"] = fabricResult.Rows.Count.ToString(),
                ["revenueCategoryCount"] = revenueItems.Count.ToString(),
                ["expenseCategoryCount"] = expenseItems.Count.ToString()
            },
            DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<BudgetWorkflowResult> AssistBudgetingAsync(
        string departmentId,
        IReadOnlyDictionary<string, string> budgetParameters,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Finance.AssistBudgeting");
        activity?.SetTag("finance.department_id", departmentId);

        Telemetry.FinanceBudgetWorkflows.Add(1);

        if (string.IsNullOrWhiteSpace(departmentId))
            throw new ArgumentException("Department ID is required.", nameof(departmentId));

        var workflowId = Guid.NewGuid();

        // Query historical spending data
        var filters = new Dictionary<string, string> { ["departmentId"] = departmentId };
        var fabricResult = await QueryFinancialDataAsync("*", filters, cancellationToken);

        double currentBudget = 0;
        if (budgetParameters.TryGetValue("currentBudget", out var cb) && double.TryParse(cb, out var cbv))
            currentBudget = cbv;

        // Calculate spending patterns
        double totalSpending = 0;
        var categorySpending = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in fabricResult.Rows)
        {
            if (row.TryGetValue("amount", out var amtStr) && double.TryParse(amtStr, out var amount))
            {
                totalSpending += amount;
                string category = row.GetValueOrDefault("category", "General");
                categorySpending[category] = categorySpending.GetValueOrDefault(category) + amount;
            }
        }

        // Generate recommendations
        var recommendations = new List<BudgetRecommendation>();
        double recommendedBudget = totalSpending * 1.05; // 5% buffer

        foreach (var (category, spent) in categorySpending.OrderByDescending(kv => kv.Value))
        {
            double suggestedAmount = spent * 1.03; // 3% growth allowance
            double savings = currentBudget > 0 ? (currentBudget * (spent / Math.Max(totalSpending, 1))) - suggestedAmount : 0;

            recommendations.Add(new BudgetRecommendation(
                category,
                savings > 0 ? $"Reduce allocation for {category}" : $"Maintain allocation for {category}",
                spent,
                suggestedAmount,
                Math.Max(0, savings)));
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add(new BudgetRecommendation(
                "General",
                "No historical data available. Recommend establishing baseline budget.",
                0, currentBudget > 0 ? currentBudget : 10000, 0));
        }

        double variance = currentBudget > 0 ? (recommendedBudget - currentBudget) / currentBudget : 0;

        var outputs = new Dictionary<string, string>
        {
            ["workflowId"] = workflowId.ToString(),
            ["departmentId"] = departmentId,
            ["totalHistoricalSpending"] = totalSpending.ToString("F2"),
            ["categoryCount"] = categorySpending.Count.ToString()
        };

        Interlocked.Increment(ref _budgetWorkflows);
        _logger.LogInformation(
            "Budget workflow {WorkflowId} for department '{DepartmentId}': recommended={Recommended:F2}, variance={Variance:P1}",
            workflowId, departmentId, recommendedBudget, variance);

        await EmitAuditEventAsync("finance.budget.assisted", departmentId, cancellationToken);

        return new BudgetWorkflowResult(
            workflowId, true, departmentId, currentBudget, recommendedBudget,
            variance, recommendations, outputs, DateTimeOffset.UtcNow);
    }

    public FinanceEngineStatus GetStatus()
    {
        return new FinanceEngineStatus(
            IsActive: true,
            AnalysesPerformed: Interlocked.Read(ref _analysesPerformed),
            AnomaliesDetected: Interlocked.Read(ref _anomaliesDetected),
            SummariesGenerated: Interlocked.Read(ref _summariesGenerated),
            BudgetWorkflows: Interlocked.Read(ref _budgetWorkflows),
            DataFabricQueries: Interlocked.Read(ref _dataFabricQueries),
            AuditEventsEmitted: Interlocked.Read(ref _auditEventsEmitted),
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<DataFabricQueryResult> QueryFinancialDataAsync(
        string scope,
        IReadOnlyDictionary<string, string> filters,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _dataFabricQueries);
        Telemetry.FinanceDataFabricQueries.Add(1);

        string source = scope == "*"
            ? string.Join(",", _options.FinancialSources)
            : scope;

        return await _dataFabric.QueryEnterpriseDataAsync(
            source: source,
            filters: filters,
            schemaMapping: new Dictionary<string, string>(),
            permissions: _options.DataFabricPermissions,
            consumerType: _options.DefaultConsumerType,
            cancellationToken);
    }

    private async global::System.Threading.Tasks.Task EmitAuditEventAsync(
        string eventType, string entityId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _auditEventsEmitted);

        var auditEvent = new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: eventType,
            Source: "finance-engine",
            CorrelationId: Guid.NewGuid(),
            Payload: new Dictionary<string, string>
            {
                ["entityId"] = entityId,
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
            },
            OccurredAtUtc: DateTimeOffset.UtcNow);

        await _eventBus.PublishAsync(auditEvent, cancellationToken);
    }
}
