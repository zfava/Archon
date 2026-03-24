using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Tooling;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Agents.Agents;

/// <summary>
/// Agent that executes registered tools.
/// </summary>
public sealed class ToolEnabledAgent : IAgent
{
    private readonly IAgentToolExecutor _toolExecutor;
    private readonly IModelProvider _modelProvider;

    public ToolEnabledAgent(IAgentToolExecutor toolExecutor, IModelProvider modelProvider)
    {
        _toolExecutor = toolExecutor;
        _modelProvider = modelProvider;
    }

    public Agent Describe() => new(
        Id: Guid.Parse("18f33f15-4089-41ec-9f7f-e2854a84f6a1"),
        Name: "ToolEnabledAgent",
        Version: "1.0.0",
        Capabilities: new[]
        {
            new AgentCapability("tooling-execution", "Execute registered tools", "runtime", "1.0.0")
        },
        IsEnabled: true,
        RegisteredAtUtc: DateTimeOffset.UtcNow);

    public async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteAsync(
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (task.Inputs.TryGetValue("prompt", out string? prompt) && !string.IsNullOrWhiteSpace(prompt))
        {
            string model = task.Inputs.GetValueOrDefault("model", "");

            var modelRequest = new ModelRequest(
                Model: model,
                Prompt: prompt,
                Parameters: task.Inputs,
                RequestedBy: Describe().Name,
                RequestedAtUtc: DateTimeOffset.UtcNow);

            ModelResponse modelResponse = await _modelProvider.GenerateAsync(modelRequest, cancellationToken);
            return new ExecutionResult(
                TaskId: task.Id,
                IsSuccess: modelResponse.IsSuccess,
                Summary: modelResponse.IsSuccess
                    ? $"Model '{modelResponse.Model}' response generated via {modelResponse.Provider}."
                    : $"Model generation failed via {modelResponse.Provider}.",
                Outputs: new Dictionary<string, string>
                {
                    ["provider"] = modelResponse.Provider,
                    ["model"] = modelResponse.Model,
                    ["content"] = modelResponse.Content
                },
                Warnings: modelResponse.Warnings.ToArray(),
                Errors: modelResponse.Errors.ToArray(),
                CompletedAtUtc: DateTimeOffset.UtcNow);
        }

        string toolName = task.Inputs.GetValueOrDefault("toolName", "");

        var request = new ToolExecutionRequest(
            ToolName: toolName,
            Parameters: task.Inputs,
            RequestedBy: Describe().Name,
            RequestedAtUtc: DateTimeOffset.UtcNow);

        ToolExecutionResult toolResult = await _toolExecutor.ExecuteToolAsync(request, cancellationToken);

        return new ExecutionResult(
            TaskId: task.Id,
            IsSuccess: toolResult.IsSuccess,
            Summary: toolResult.IsSuccess ? $"Tool '{toolName}' executed." : $"Tool '{toolName}' failed.",
            Outputs: toolResult.Outputs,
            Warnings: Array.Empty<string>(),
            Errors: toolResult.Errors,
            CompletedAtUtc: DateTimeOffset.UtcNow);
    }
}
