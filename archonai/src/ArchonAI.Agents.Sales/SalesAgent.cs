using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Sales;
using Microsoft.Extensions.Logging;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Agents.Sales;

public sealed class SalesAgent : IAgent
{
    private readonly ISalesEngine _salesEngine;
    private readonly IModelProvider _modelProvider;
    private readonly ILogger<SalesAgent> _logger;

    public SalesAgent(
        ISalesEngine salesEngine,
        IModelProvider modelProvider,
        ILogger<SalesAgent> logger)
    {
        _salesEngine = salesEngine;
        _modelProvider = modelProvider;
        _logger = logger;
    }

    public Agent Describe() => new(
        Id: Guid.Parse("b5e9d723-8a41-4c6b-a3f2-1d7b5e094c86"),
        Name: "SalesAgent",
        Version: "1.0.0",
        Capabilities: new[]
        {
            new AgentCapability("pipeline-analysis", "Analyze sales pipeline health and metrics", "sales", "1.0.0"),
            new AgentCapability("opportunity-prioritization", "Prioritize opportunities by scoring criteria", "sales", "1.0.0"),
            new AgentCapability("outreach-recommendation", "Recommend outreach actions for opportunities", "sales", "1.0.0"),
            new AgentCapability("sales-metrics", "Generate sales performance metrics snapshots", "sales", "1.0.0")
        },
        IsEnabled: true,
        RegisteredAtUtc: DateTimeOffset.UtcNow);

    public async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteAsync(
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("SalesAgent.Execute");
        activity?.SetTag("sales.task_name", task.Name);
        activity?.SetTag("sales.capability", task.RequiredCapability);

        _logger.LogInformation(
            "SalesAgent executing task {TaskId} with capability '{Capability}'",
            task.Id, task.RequiredCapability);

        return task.RequiredCapability switch
        {
            "pipeline-analysis" => await ExecutePipelineAnalysisAsync(task, cancellationToken),
            "opportunity-prioritization" => await ExecuteOpportunityPrioritizationAsync(task, cancellationToken),
            "outreach-recommendation" => await ExecuteOutreachRecommendationAsync(task, cancellationToken),
            "sales-metrics" => await ExecuteSalesMetricsAsync(task, cancellationToken),
            _ => await ExecuteWithModelAsync(task, cancellationToken)
        };
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecutePipelineAnalysisAsync(
        CoreTask task, CancellationToken ct)
    {
        string pipelineId = task.Inputs.GetValueOrDefault("pipelineId", "*");
        var result = await _salesEngine.AnalyzePipelineAsync(pipelineId, task.Inputs, ct);

        return new ExecutionResult(
            task.Id, result.IsSuccess,
            $"Pipeline analysis for '{pipelineId}': opportunities={result.TotalOpportunities}, totalValue={result.TotalValue:F2}, weightedValue={result.WeightedValue:F2}",
            new Dictionary<string, string>
            {
                ["totalOpportunities"] = result.TotalOpportunities.ToString(),
                ["totalValue"] = result.TotalValue.ToString("F2"),
                ["weightedValue"] = result.WeightedValue.ToString("F2"),
                ["averageCloseRate"] = result.AverageCloseRate.ToString("F4"),
                ["riskCount"] = result.Risks.Count.ToString()
            },
            result.Risks.Count > 0 ? result.Risks.ToArray() : Array.Empty<string>(),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteOpportunityPrioritizationAsync(
        CoreTask task, CancellationToken ct)
    {
        string pipelineId = task.Inputs.GetValueOrDefault("pipelineId", "*");
        _ = int.TryParse(task.Inputs.GetValueOrDefault("maxResults", "20"), out int maxResults);

        var opportunities = await _salesEngine.PrioritizeOpportunitiesAsync(pipelineId, maxResults, ct);

        return new ExecutionResult(
            task.Id, true,
            $"Prioritized {opportunities.Count} opportunities for pipeline '{pipelineId}'",
            new Dictionary<string, string>
            {
                ["opportunityCount"] = opportunities.Count.ToString(),
                ["pipelineId"] = pipelineId,
                ["opportunities"] = System.Text.Json.JsonSerializer.Serialize(opportunities)
            },
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteOutreachRecommendationAsync(
        CoreTask task, CancellationToken ct)
    {
        string opportunityId = task.Inputs.GetValueOrDefault("opportunityId", string.Empty);
        var recommendations = await _salesEngine.RecommendOutreachAsync(opportunityId, ct);

        return new ExecutionResult(
            task.Id, true,
            $"Generated {recommendations.Count} outreach recommendations for opportunity '{opportunityId}'",
            new Dictionary<string, string>
            {
                ["recommendationCount"] = recommendations.Count.ToString(),
                ["opportunityId"] = opportunityId,
                ["recommendations"] = System.Text.Json.JsonSerializer.Serialize(recommendations)
            },
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteSalesMetricsAsync(
        CoreTask task, CancellationToken ct)
    {
        string scope = task.Inputs.GetValueOrDefault("scope", "*");
        string period = task.Inputs.GetValueOrDefault("period", "current");

        var metrics = await _salesEngine.GetMetricsAsync(scope, period, ct);

        return new ExecutionResult(
            task.Id, true,
            $"Sales metrics for '{scope}' ({period}): revenue={metrics.TotalRevenue:F2}, winRate={metrics.WinRate:P1}",
            new Dictionary<string, string>
            {
                ["totalRevenue"] = metrics.TotalRevenue.ToString("F2"),
                ["pipelineValue"] = metrics.PipelineValue.ToString("F2"),
                ["dealsWon"] = metrics.DealsWon.ToString(),
                ["dealsLost"] = metrics.DealsLost.ToString(),
                ["winRate"] = metrics.WinRate.ToString("F4"),
                ["averageDealSize"] = metrics.AverageDealSize.ToString("F2"),
                ["averageSalesCycle"] = metrics.AverageSalesCycle.ToString("F2")
            },
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteWithModelAsync(
        CoreTask task, CancellationToken ct)
    {
        string prompt = task.Inputs.GetValueOrDefault("prompt",
            $"Provide sales analysis insights for task '{task.Name}'.");

        var modelRequest = new ModelRequest(
            Model: task.Inputs.GetValueOrDefault("model", ""),
            Prompt: prompt,
            Parameters: task.Inputs,
            RequestedBy: Describe().Name,
            RequestedAtUtc: DateTimeOffset.UtcNow);

        ModelResponse response = await _modelProvider.GenerateAsync(modelRequest, ct);

        return new ExecutionResult(
            task.Id, response.IsSuccess,
            response.IsSuccess ? $"Sales reasoning via {response.Provider}." : $"Sales reasoning failed.",
            new Dictionary<string, string>
            {
                ["provider"] = response.Provider,
                ["model"] = response.Model,
                ["content"] = response.Content
            },
            response.Warnings.ToArray(), response.Errors.ToArray(), DateTimeOffset.UtcNow);
    }
}
