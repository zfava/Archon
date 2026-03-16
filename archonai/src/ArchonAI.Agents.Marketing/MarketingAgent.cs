using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Marketing;
using Microsoft.Extensions.Logging;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Agents.Marketing;

public sealed class MarketingAgent : IAgent
{
    private readonly IMarketingEngine _marketingEngine;
    private readonly IModelProvider _modelProvider;
    private readonly ILogger<MarketingAgent> _logger;

    public MarketingAgent(
        IMarketingEngine marketingEngine,
        IModelProvider modelProvider,
        ILogger<MarketingAgent> logger)
    {
        _marketingEngine = marketingEngine;
        _modelProvider = modelProvider;
        _logger = logger;
    }

    public Agent Describe() => new(
        Id: Guid.Parse("d6f0e834-9b52-4d7c-b403-2e8c6f105d97"),
        Name: "MarketingAgent",
        Version: "1.0.0",
        Capabilities: new[]
        {
            new AgentCapability("campaign-analysis", "Analyze marketing campaign performance", "marketing", "1.0.0"),
            new AgentCapability("strategy-recommendation", "Recommend marketing strategies", "marketing", "1.0.0"),
            new AgentCapability("engagement-metrics", "Generate engagement metrics snapshots", "marketing", "1.0.0")
        },
        IsEnabled: true,
        RegisteredAtUtc: DateTimeOffset.UtcNow);

    public async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteAsync(
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("MarketingAgent.Execute");
        activity?.SetTag("marketing.task_name", task.Name);
        activity?.SetTag("marketing.capability", task.RequiredCapability);

        _logger.LogInformation(
            "MarketingAgent executing task {TaskId} with capability '{Capability}'",
            task.Id, task.RequiredCapability);

        return task.RequiredCapability switch
        {
            "campaign-analysis" => await ExecuteCampaignAnalysisAsync(task, cancellationToken),
            "strategy-recommendation" => await ExecuteStrategyRecommendationAsync(task, cancellationToken),
            "engagement-metrics" => await ExecuteEngagementMetricsAsync(task, cancellationToken),
            _ => await ExecuteWithModelAsync(task, cancellationToken)
        };
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteCampaignAnalysisAsync(
        CoreTask task, CancellationToken ct)
    {
        string campaignId = task.Inputs.GetValueOrDefault("campaignId", "*");
        var result = await _marketingEngine.AnalyzeCampaignAsync(campaignId, task.Inputs, ct);

        return new ExecutionResult(
            task.Id, result.IsSuccess,
            $"Campaign analysis for '{campaignId}': ROI={result.ROI:P1}, spend={result.TotalSpend:F2}, revenue={result.TotalRevenue:F2}",
            new Dictionary<string, string>
            {
                ["campaignId"] = result.CampaignId,
                ["totalSpend"] = result.TotalSpend.ToString("F2"),
                ["totalRevenue"] = result.TotalRevenue.ToString("F2"),
                ["roi"] = result.ROI.ToString("F4"),
                ["conversionRate"] = result.ConversionRate.ToString("F4"),
                ["findingCount"] = result.KeyFindings.Count.ToString(),
                ["improvementCount"] = result.Improvements.Count.ToString()
            },
            result.Improvements.Count > 0 ? result.Improvements.ToArray() : Array.Empty<string>(),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteStrategyRecommendationAsync(
        CoreTask task, CancellationToken ct)
    {
        string scope = task.Inputs.GetValueOrDefault("scope", "*");
        var strategies = await _marketingEngine.RecommendStrategiesAsync(scope, ct);

        return new ExecutionResult(
            task.Id, true,
            $"Generated {strategies.Count} strategy recommendations for scope '{scope}'",
            new Dictionary<string, string>
            {
                ["strategyCount"] = strategies.Count.ToString(),
                ["scope"] = scope,
                ["strategies"] = System.Text.Json.JsonSerializer.Serialize(strategies)
            },
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteEngagementMetricsAsync(
        CoreTask task, CancellationToken ct)
    {
        string scope = task.Inputs.GetValueOrDefault("scope", "*");
        string period = task.Inputs.GetValueOrDefault("period", "current");

        var metrics = await _marketingEngine.GetEngagementMetricsAsync(scope, period, ct);

        return new ExecutionResult(
            task.Id, true,
            $"Engagement metrics for '{scope}' ({period}): impressions={metrics.TotalImpressions}, clicks={metrics.TotalClicks}, conversions={metrics.TotalConversions}",
            new Dictionary<string, string>
            {
                ["totalImpressions"] = metrics.TotalImpressions.ToString(),
                ["totalClicks"] = metrics.TotalClicks.ToString(),
                ["totalConversions"] = metrics.TotalConversions.ToString(),
                ["clickThroughRate"] = metrics.ClickThroughRate.ToString("F4"),
                ["conversionRate"] = metrics.ConversionRate.ToString("F4"),
                ["costPerAcquisition"] = metrics.CostPerAcquisition.ToString("F2")
            },
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteWithModelAsync(
        CoreTask task, CancellationToken ct)
    {
        string prompt = task.Inputs.GetValueOrDefault("prompt",
            $"Provide marketing analysis insights for task '{task.Name}'.");

        var modelRequest = new ModelRequest(
            Model: task.Inputs.GetValueOrDefault("model", ""),
            Prompt: prompt,
            Parameters: task.Inputs,
            RequestedBy: Describe().Name,
            RequestedAtUtc: DateTimeOffset.UtcNow);

        ModelResponse response = await _modelProvider.GenerateAsync(modelRequest, ct);

        return new ExecutionResult(
            task.Id, response.IsSuccess,
            response.IsSuccess ? $"Marketing reasoning via {response.Provider}." : $"Marketing reasoning failed.",
            new Dictionary<string, string>
            {
                ["provider"] = response.Provider,
                ["model"] = response.Model,
                ["content"] = response.Content
            },
            response.Warnings.ToArray(), response.Errors.ToArray(), DateTimeOffset.UtcNow);
    }
}
