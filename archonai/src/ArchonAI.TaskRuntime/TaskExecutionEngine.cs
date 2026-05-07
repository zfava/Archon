using System.Diagnostics;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.TaskRuntime;
using Microsoft.Extensions.Options;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.TaskRuntime;

public sealed class TaskExecutionEngine : ITaskExecutionEngine
{
    private readonly IAgentSupervisor _agentSupervisor;
    private readonly IAgentSandboxManager _agentSandboxManager;
    private readonly IGovernanceKernel _governanceKernel;
    private readonly TaskRuntimeOptions _options;

    public TaskExecutionEngine(
        IAgentSupervisor agentSupervisor,
        IAgentSandboxManager agentSandboxManager,
        IGovernanceKernel governanceKernel,
        IOptions<TaskRuntimeOptions> options)
    {
        _agentSupervisor = agentSupervisor;
        _agentSandboxManager = agentSandboxManager;
        _governanceKernel = governanceKernel;
        _options = options.Value;
    }

    public async global::System.Threading.Tasks.Task<TaskExecutionOutcome> ExecuteTaskAsync(
        CoreTask task,
        CoreExecutionContext context,
        IReadOnlyList<IAgent> availableAgents,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<IAgent> candidates = availableAgents
            .Where(agent =>
            {
                Agent descriptor = agent.Describe();
                return descriptor.IsEnabled
                    && descriptor.Capabilities.Any(capability =>
                        capability.Name.Equals(task.RequiredCapability, StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();

        if (candidates.Count == 0)
        {
            return CreateFailure(task, Guid.Empty, "No registered agent for required capability.", "AgentNotFound", 0, 0, DateTimeOffset.UtcNow);
        }

        Agent? selectedDescriptor = await _agentSupervisor.SelectAgentAsync(
            candidates.Select(c => c.Describe()).ToArray(),
            task,
            cancellationToken);

        IAgent selectedAgent = selectedDescriptor is null
            ? candidates[0]
            : candidates.FirstOrDefault(candidate => candidate.Describe().Id == selectedDescriptor.Id) ?? candidates[0];

        Agent agent = selectedAgent.Describe();

        var enrichedContext = context with
        {
            TaskId = task.Id,
            Metadata = new Dictionary<string, string>(context.Metadata)
            {
                ["requiredCapability"] = task.RequiredCapability,
                ["scheduledOrder"] = task.Order.ToString()
            }
        };

        var supervisionDecision = await _agentSupervisor.ValidateExecutionAsync(agent, task, enrichedContext, cancellationToken);
        if (!supervisionDecision.IsAllowed)
        {
            return CreateFailure(task, agent.Id, $"Supervisor denied execution: {supervisionDecision.Reason}", "SupervisorDenied", 0, 0, DateTimeOffset.UtcNow, supervisionDecision.Signals);
        }

        var sandboxDecision = await _agentSandboxManager.EnsureSandboxAsync(agent, task, enrichedContext, cancellationToken);
        if (!sandboxDecision.IsAllowed)
        {
            return CreateFailure(task, agent.Id, $"Sandbox denied execution: {sandboxDecision.Reason}", "SandboxDenied", 0, 0, DateTimeOffset.UtcNow);
        }

        var governanceDecision = await _governanceKernel.ValidateExecutionAsync(agent, task, enrichedContext, cancellationToken);
        if (!governanceDecision.IsAllowed)
        {
            return CreateFailure(task, agent.Id, $"Governance denied execution: {governanceDecision.Reason}", "GovernanceDenied", 0, 0, DateTimeOffset.UtcNow, governanceDecision.Violations);
        }

        var stopwatch = Stopwatch.StartNew();
        int attempts = 0;
        try
        {
            while (true)
            {
                attempts++;
                try
                {
                    ExecutionResult result = await selectedAgent.ExecuteAsync(task, enrichedContext, cancellationToken);
                    stopwatch.Stop();

                    decimal cost = result.IsSuccess ? 0.01m : 0.02m;
                    string errorType = result.IsSuccess ? "none" : (result.Errors.FirstOrDefault() ?? "ExecutionFailed");

                    if (result.IsSuccess || attempts > _options.MaxRetries)
                    {
                        await _agentSupervisor.RecordExecutionCompletedAsync(agent, result, stopwatch.Elapsed.TotalMilliseconds, cancellationToken);
                        return new TaskExecutionOutcome(result, agent.Id, stopwatch.Elapsed.TotalMilliseconds, cost, errorType, attempts, DateTimeOffset.UtcNow);
                    }
                }
                catch (Exception ex) when (attempts <= _options.MaxRetries)
                {
                    if (_options.BaseRetryDelayMs > 0)
                    {
                        await global::System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(_options.BaseRetryDelayMs * attempts), cancellationToken);
                    }

                    if (attempts > _options.MaxRetries)
                    {
                        stopwatch.Stop();
                        var failed = CreateFailure(task, agent.Id, $"Task execution failed: {ex.Message}", "ExecutionException", attempts, stopwatch.Elapsed.TotalMilliseconds, DateTimeOffset.UtcNow);
                        await _agentSupervisor.RecordExecutionCompletedAsync(agent, failed.Result, stopwatch.Elapsed.TotalMilliseconds, cancellationToken);
                        return failed;
                    }
                }

                if (_options.BaseRetryDelayMs > 0)
                {
                    await global::System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(_options.BaseRetryDelayMs * attempts), cancellationToken);
                }
            }
        }
        finally
        {
            await _governanceKernel.MarkExecutionCompletedAsync(agent.Id, cancellationToken);
            await _agentSandboxManager.RecordExecutionCompletedAsync(sandboxDecision.SandboxId, cancellationToken);
        }
    }

    private static TaskExecutionOutcome CreateFailure(
        CoreTask task,
        Guid agentId,
        string summary,
        string errorType,
        int attempts,
        double executionMs,
        DateTimeOffset completedAtUtc,
        IReadOnlyList<string>? warnings = null)
    {
        var result = new ExecutionResult(
            TaskId: task.Id,
            IsSuccess: false,
            Summary: summary,
            Outputs: new Dictionary<string, string>(),
            Warnings: warnings ?? Array.Empty<string>(),
            Errors: new[] { errorType },
            CompletedAtUtc: completedAtUtc);

        return new TaskExecutionOutcome(
            Result: result,
            AgentId: agentId,
            ExecutionTimeMs: Math.Max(0, executionMs),
            Cost: 0,
            ErrorType: errorType,
            AttemptCount: Math.Max(1, attempts),
            CompletedAtUtc: completedAtUtc);
    }
}
