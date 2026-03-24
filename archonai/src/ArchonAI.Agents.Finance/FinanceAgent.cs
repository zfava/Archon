using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Finance;
using Microsoft.Extensions.Logging;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Agents.Finance;

public sealed class FinanceAgent : IAgent
{
    private readonly IFinanceEngine _financeEngine;
    private readonly IModelProvider _modelProvider;
    private readonly ILogger<FinanceAgent> _logger;

    public FinanceAgent(
        IFinanceEngine financeEngine,
        IModelProvider modelProvider,
        ILogger<FinanceAgent> logger)
    {
        _financeEngine = financeEngine;
        _modelProvider = modelProvider;
        _logger = logger;
    }

    public Agent Describe() => new(
        Id: Guid.Parse("c4d8e912-7f56-4a3b-b2e1-0f5a8c6d39b7"),
        Name: "FinanceAgent",
        Version: "1.0.0",
        Capabilities: new[]
        {
            new AgentCapability("financial-analysis", "Analyze financial performance metrics", "finance", "1.0.0"),
            new AgentCapability("anomaly-detection", "Detect financial anomalies and irregularities", "finance", "1.0.0"),
            new AgentCapability("financial-summary", "Generate financial summaries and reports", "finance", "1.0.0"),
            new AgentCapability("budget-assistance", "Assist with budgeting workflows", "finance", "1.0.0")
        },
        IsEnabled: true,
        RegisteredAtUtc: DateTimeOffset.UtcNow);

    public async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteAsync(
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("FinanceAgent.Execute");
        activity?.SetTag("finance.task_name", task.Name);
        activity?.SetTag("finance.capability", task.RequiredCapability);

        _logger.LogInformation(
            "FinanceAgent executing task {TaskId} with capability '{Capability}'",
            task.Id, task.RequiredCapability);

        return task.RequiredCapability switch
        {
            "financial-analysis" => await ExecuteAnalysisAsync(task, cancellationToken),
            "anomaly-detection" => await ExecuteAnomalyDetectionAsync(task, cancellationToken),
            "financial-summary" => await ExecuteSummaryAsync(task, cancellationToken),
            "budget-assistance" => await ExecuteBudgetAssistanceAsync(task, cancellationToken),
            _ => await ExecuteWithModelAsync(task, cancellationToken)
        };
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteAnalysisAsync(
        CoreTask task, CancellationToken ct)
    {
        string scope = task.Inputs.GetValueOrDefault("scope", "*");
        var result = await _financeEngine.AnalyzePerformanceAsync(scope, task.Inputs, ct);

        return new ExecutionResult(
            task.Id, result.IsSuccess,
            $"Financial analysis for '{scope}': margin={result.ProfitMargin:P1}, findings={result.KeyFindings.Count}, risks={result.Risks.Count}",
            new Dictionary<string, string>
            {
                ["revenueGrowthRate"] = result.RevenueGrowthRate.ToString("F4"),
                ["expenseGrowthRate"] = result.ExpenseGrowthRate.ToString("F4"),
                ["profitMargin"] = result.ProfitMargin.ToString("F4"),
                ["findingCount"] = result.KeyFindings.Count.ToString(),
                ["riskCount"] = result.Risks.Count.ToString()
            },
            result.Risks.Count > 0 ? result.Risks.ToArray() : Array.Empty<string>(),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteAnomalyDetectionAsync(
        CoreTask task, CancellationToken ct)
    {
        string scope = task.Inputs.GetValueOrDefault("scope", "*");
        _ = double.TryParse(task.Inputs.GetValueOrDefault("sensitivity", "0.7"), out double sensitivity);
        _ = int.TryParse(task.Inputs.GetValueOrDefault("maxResults", "20"), out int maxResults);

        var anomalies = await _financeEngine.DetectAnomaliesAsync(scope, sensitivity, maxResults, ct);

        return new ExecutionResult(
            task.Id, true,
            $"Detected {anomalies.Count} anomalies in scope '{scope}'",
            new Dictionary<string, string>
            {
                ["anomalyCount"] = anomalies.Count.ToString(),
                ["scope"] = scope,
                ["anomalies"] = System.Text.Json.JsonSerializer.Serialize(anomalies)
            },
            anomalies.Where(a => a.Severity >= 0.8).Select(a => $"High severity: {a.Title}").ToArray(),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteSummaryAsync(
        CoreTask task, CancellationToken ct)
    {
        string scope = task.Inputs.GetValueOrDefault("scope", "*");
        string period = task.Inputs.GetValueOrDefault("period", "current");

        var summary = await _financeEngine.GenerateSummaryAsync(scope, period, ct);

        return new ExecutionResult(
            task.Id, true,
            $"Financial summary for '{scope}' ({period}): revenue={summary.TotalRevenue:F2}, net={summary.NetIncome:F2}",
            new Dictionary<string, string>
            {
                ["totalRevenue"] = summary.TotalRevenue.ToString("F2"),
                ["totalExpenses"] = summary.TotalExpenses.ToString("F2"),
                ["netIncome"] = summary.NetIncome.ToString("F2"),
                ["profitMargin"] = summary.ProfitMargin.ToString("F4"),
                ["topRevenueCount"] = summary.TopRevenueItems.Count.ToString(),
                ["topExpenseCount"] = summary.TopExpenseItems.Count.ToString()
            },
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteBudgetAssistanceAsync(
        CoreTask task, CancellationToken ct)
    {
        string departmentId = task.Inputs.GetValueOrDefault("departmentId", string.Empty);
        var result = await _financeEngine.AssistBudgetingAsync(departmentId, task.Inputs, ct);

        return new ExecutionResult(
            task.Id, result.IsSuccess,
            $"Budget workflow for '{departmentId}': recommended={result.RecommendedBudget:F2}, variance={result.Variance:P1}",
            result.Outputs,
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteWithModelAsync(
        CoreTask task, CancellationToken ct)
    {
        string prompt = task.Inputs.GetValueOrDefault("prompt",
            $"Provide financial analysis insights for task '{task.Name}'.");

        var modelRequest = new ModelRequest(
            Model: task.Inputs.GetValueOrDefault("model", ""),
            Prompt: prompt,
            Parameters: task.Inputs,
            RequestedBy: Describe().Name,
            RequestedAtUtc: DateTimeOffset.UtcNow);

        ModelResponse response = await _modelProvider.GenerateAsync(modelRequest, ct);

        return new ExecutionResult(
            task.Id, response.IsSuccess,
            response.IsSuccess ? $"Finance reasoning via {response.Provider}." : $"Finance reasoning failed.",
            new Dictionary<string, string>
            {
                ["provider"] = response.Provider,
                ["model"] = response.Model,
                ["content"] = response.Content
            },
            response.Warnings.ToArray(), response.Errors.ToArray(), DateTimeOffset.UtcNow);
    }
}
