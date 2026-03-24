using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Operations;
using Microsoft.Extensions.Logging;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Agents.Operations;

public sealed class OperationsAgent : IAgent
{
    private readonly IOperationsEngine _operationsEngine;
    private readonly IModelProvider _modelProvider;
    private readonly ILogger<OperationsAgent> _logger;

    public OperationsAgent(
        IOperationsEngine operationsEngine,
        IModelProvider modelProvider,
        ILogger<OperationsAgent> logger)
    {
        _operationsEngine = operationsEngine;
        _modelProvider = modelProvider;
        _logger = logger;
    }

    public Agent Describe() => new(
        Id: Guid.Parse("a7e2c891-5d34-4f8a-b1c6-9e3f7a204d58"),
        Name: "OperationsAgent",
        Version: "1.0.0",
        Capabilities: new[]
        {
            new AgentCapability("workflow-analysis", "Analyze workflows for efficiency and bottlenecks", "operations", "1.0.0"),
            new AgentCapability("inefficiency-detection", "Identify operational inefficiencies", "operations", "1.0.0"),
            new AgentCapability("improvement-recommendation", "Recommend operational improvements", "operations", "1.0.0"),
            new AgentCapability("workflow-coordination", "Coordinate multi-agent workflows", "operations", "1.0.0")
        },
        IsEnabled: true,
        RegisteredAtUtc: DateTimeOffset.UtcNow);

    public async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteAsync(
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("OperationsAgent.Execute");
        activity?.SetTag("operations.task_name", task.Name);
        activity?.SetTag("operations.capability", task.RequiredCapability);

        _logger.LogInformation(
            "OperationsAgent executing task {TaskId} with capability '{Capability}'",
            task.Id, task.RequiredCapability);

        return task.RequiredCapability switch
        {
            "workflow-analysis" => await ExecuteWorkflowAnalysisAsync(task, context, cancellationToken),
            "inefficiency-detection" => await ExecuteInefficiencyDetectionAsync(task, context, cancellationToken),
            "improvement-recommendation" => await ExecuteRecommendationsAsync(task, context, cancellationToken),
            "workflow-coordination" => await ExecuteWorkflowCoordinationAsync(task, context, cancellationToken),
            _ => await ExecuteWithReasoningLoopAsync(task, context, cancellationToken)
        };
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteWorkflowAnalysisAsync(
        CoreTask task, CoreExecutionContext context, CancellationToken ct)
    {
        var result = await _operationsEngine.AnalyzeWorkflowAsync(context.ObjectiveId, task.Inputs, ct);

        return new ExecutionResult(
            task.Id, result.IsSuccess,
            $"Workflow analysis complete. Efficiency: {result.EfficiencyScore:P1}, bottlenecks: {result.Bottlenecks.Count}, recommendations: {result.Recommendations.Count}",
            new Dictionary<string, string>
            {
                ["efficiencyScore"] = result.EfficiencyScore.ToString("F3"),
                ["totalSteps"] = result.TotalSteps.ToString(),
                ["successfulSteps"] = result.SuccessfulSteps.ToString(),
                ["failedSteps"] = result.FailedSteps.ToString(),
                ["bottleneckCount"] = result.Bottlenecks.Count.ToString(),
                ["recommendationCount"] = result.Recommendations.Count.ToString()
            },
            result.Bottlenecks.Count > 0 ? [$"Found {result.Bottlenecks.Count} bottleneck(s)"] : Array.Empty<string>(),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteInefficiencyDetectionAsync(
        CoreTask task, CoreExecutionContext context, CancellationToken ct)
    {
        string scope = task.Inputs.GetValueOrDefault("scope", "*");
        _ = int.TryParse(task.Inputs.GetValueOrDefault("maxResults", "20"), out int maxResults);

        var insights = await _operationsEngine.IdentifyInefficienciesAsync(scope, maxResults, ct);

        return new ExecutionResult(
            task.Id, true,
            $"Identified {insights.Count} inefficiencies in scope '{scope}'",
            new Dictionary<string, string>
            {
                ["insightCount"] = insights.Count.ToString(),
                ["scope"] = scope,
                ["insights"] = System.Text.Json.JsonSerializer.Serialize(insights)
            },
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteRecommendationsAsync(
        CoreTask task, CoreExecutionContext context, CancellationToken ct)
    {
        var recommendations = await _operationsEngine.RecommendImprovementsAsync(context.ObjectiveId, ct);

        return new ExecutionResult(
            task.Id, true,
            $"Generated {recommendations.Count} improvement recommendations",
            new Dictionary<string, string>
            {
                ["recommendationCount"] = recommendations.Count.ToString(),
                ["recommendations"] = System.Text.Json.JsonSerializer.Serialize(recommendations)
            },
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteWorkflowCoordinationAsync(
        CoreTask task, CoreExecutionContext context, CancellationToken ct)
    {
        string template = task.Inputs.GetValueOrDefault("workflowTemplate", "balanced");
        string[] capabilities = task.Inputs.GetValueOrDefault("agentCapabilities", "workflow-orchestration")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = await _operationsEngine.CoordinateWorkflowAsync(template, capabilities, task.Inputs, ct);

        return new ExecutionResult(
            task.Id, result.IsSuccess,
            $"Workflow coordination '{template}' completed. Steps: {result.StepsCompleted}/{result.StepsCompleted + result.StepsFailed}, Duration: {result.Duration.TotalMilliseconds:F0}ms",
            result.Outputs,
            Array.Empty<string>(),
            result.Errors,
            DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteWithReasoningLoopAsync(
        CoreTask task, CoreExecutionContext context, CancellationToken ct)
    {
        // General reasoning loop using model provider
        string prompt = task.Inputs.GetValueOrDefault("prompt",
            $"Analyze the operational context for task '{task.Name}' and provide actionable insights.");

        string model = task.Inputs.GetValueOrDefault("model", "");

        var modelRequest = new ModelRequest(
            Model: model,
            Prompt: prompt,
            Parameters: task.Inputs,
            RequestedBy: Describe().Name,
            RequestedAtUtc: DateTimeOffset.UtcNow);

        ModelResponse modelResponse = await _modelProvider.GenerateAsync(modelRequest, ct);

        return new ExecutionResult(
            task.Id,
            modelResponse.IsSuccess,
            modelResponse.IsSuccess
                ? $"Operations reasoning completed via {modelResponse.Provider}."
                : $"Operations reasoning failed via {modelResponse.Provider}.",
            new Dictionary<string, string>
            {
                ["provider"] = modelResponse.Provider,
                ["model"] = modelResponse.Model,
                ["content"] = modelResponse.Content
            },
            modelResponse.Warnings.ToArray(),
            modelResponse.Errors.ToArray(),
            DateTimeOffset.UtcNow);
    }
}
