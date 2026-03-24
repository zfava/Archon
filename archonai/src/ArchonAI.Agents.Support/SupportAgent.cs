using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Support;
using Microsoft.Extensions.Logging;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Agents.Support;

public sealed class SupportAgent : IAgent
{
    private readonly ISupportEngine _supportEngine;
    private readonly IModelProvider _modelProvider;
    private readonly ILogger<SupportAgent> _logger;

    public SupportAgent(
        ISupportEngine supportEngine,
        IModelProvider modelProvider,
        ILogger<SupportAgent> logger)
    {
        _supportEngine = supportEngine;
        _modelProvider = modelProvider;
        _logger = logger;
    }

    public Agent Describe() => new(
        Id: Guid.Parse("e7a1f945-ac63-4e8d-b514-3f9d70216ea8"),
        Name: "SupportAgent",
        Version: "1.0.0",
        Capabilities: new[]
        {
            new AgentCapability("ticket-analysis", "Analyze support ticket volumes and trends", "support", "1.0.0"),
            new AgentCapability("recurring-issue-detection", "Detect recurring support issues", "support", "1.0.0"),
            new AgentCapability("auto-response-recommendation", "Recommend automated responses", "support", "1.0.0")
        },
        IsEnabled: true,
        RegisteredAtUtc: DateTimeOffset.UtcNow);

    public async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteAsync(
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("SupportAgent.Execute");
        activity?.SetTag("support.task_name", task.Name);
        activity?.SetTag("support.capability", task.RequiredCapability);

        _logger.LogInformation(
            "SupportAgent executing task {TaskId} with capability '{Capability}'",
            task.Id, task.RequiredCapability);

        return task.RequiredCapability switch
        {
            "ticket-analysis" => await ExecuteTicketAnalysisAsync(task, cancellationToken),
            "recurring-issue-detection" => await ExecuteRecurringIssueDetectionAsync(task, cancellationToken),
            "auto-response-recommendation" => await ExecuteAutoResponseRecommendationAsync(task, cancellationToken),
            _ => await ExecuteWithModelAsync(task, cancellationToken)
        };
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteTicketAnalysisAsync(
        CoreTask task, CancellationToken ct)
    {
        string scope = task.Inputs.GetValueOrDefault("scope", "*");
        var result = await _supportEngine.AnalyzeTicketsAsync(scope, task.Inputs, ct);

        return new ExecutionResult(
            task.Id, result.IsSuccess,
            $"Ticket analysis for '{scope}': total={result.TotalTickets}, open={result.OpenTickets}, resolved={result.ResolvedTickets}, avgResolution={result.AverageResolutionHours:F1}h",
            new Dictionary<string, string>
            {
                ["totalTickets"] = result.TotalTickets.ToString(),
                ["openTickets"] = result.OpenTickets.ToString(),
                ["resolvedTickets"] = result.ResolvedTickets.ToString(),
                ["avgResolutionHours"] = result.AverageResolutionHours.ToString("F2"),
                ["satisfactionScore"] = result.SatisfactionScore.ToString("F2"),
                ["findingCount"] = result.KeyFindings.Count.ToString()
            },
            result.KeyFindings.Count > 0 ? result.KeyFindings.ToArray() : Array.Empty<string>(),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteRecurringIssueDetectionAsync(
        CoreTask task, CancellationToken ct)
    {
        string scope = task.Inputs.GetValueOrDefault("scope", "*");
        _ = int.TryParse(task.Inputs.GetValueOrDefault("minOccurrences", "3"), out int minOccurrences);

        var issues = await _supportEngine.DetectRecurringIssuesAsync(scope, minOccurrences, ct);

        return new ExecutionResult(
            task.Id, true,
            $"Detected {issues.Count} recurring issues for scope '{scope}' with >= {minOccurrences} occurrences",
            new Dictionary<string, string>
            {
                ["recurringIssueCount"] = issues.Count.ToString(),
                ["scope"] = scope,
                ["issues"] = System.Text.Json.JsonSerializer.Serialize(issues)
            },
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteAutoResponseRecommendationAsync(
        CoreTask task, CancellationToken ct)
    {
        string issueCategory = task.Inputs.GetValueOrDefault("issueCategory", "*");
        var recommendations = await _supportEngine.RecommendAutoResponsesAsync(issueCategory, ct);

        return new ExecutionResult(
            task.Id, true,
            $"Generated {recommendations.Count} auto-response recommendations for category '{issueCategory}'",
            new Dictionary<string, string>
            {
                ["recommendationCount"] = recommendations.Count.ToString(),
                ["issueCategory"] = issueCategory,
                ["recommendations"] = System.Text.Json.JsonSerializer.Serialize(recommendations)
            },
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteWithModelAsync(
        CoreTask task, CancellationToken ct)
    {
        string prompt = task.Inputs.GetValueOrDefault("prompt",
            $"Provide support analysis insights for task '{task.Name}'.");

        var modelRequest = new ModelRequest(
            Model: task.Inputs.GetValueOrDefault("model", ""),
            Prompt: prompt,
            Parameters: task.Inputs,
            RequestedBy: Describe().Name,
            RequestedAtUtc: DateTimeOffset.UtcNow);

        ModelResponse response = await _modelProvider.GenerateAsync(modelRequest, ct);

        return new ExecutionResult(
            task.Id, response.IsSuccess,
            response.IsSuccess ? $"Support reasoning via {response.Provider}." : "Support reasoning failed.",
            new Dictionary<string, string>
            {
                ["provider"] = response.Provider,
                ["model"] = response.Model,
                ["content"] = response.Content
            },
            response.Warnings.ToArray(), response.Errors.ToArray(), DateTimeOffset.UtcNow);
    }
}
