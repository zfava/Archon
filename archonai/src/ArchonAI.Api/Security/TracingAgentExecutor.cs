using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Observability;
using CoreTask = ArchonAI.Core.Models.Task;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using OTel = ArchonAI.Common.Observability.Telemetry;

namespace ArchonAI.Api.Security;

public sealed class TracingAgentExecutor
{
    private readonly IObservabilityService _observability;
    private readonly ILogger<TracingAgentExecutor> _logger;

    public TracingAgentExecutor(IObservabilityService observability, ILogger<TracingAgentExecutor> logger)
    {
        _observability = observability;
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteWithTracingAsync(
        IAgent agent, CoreTask task, CoreExecutionContext context, CancellationToken ct = default)
    {
        var desc = agent.Describe();
        using var activity = OTel.ActivitySource.StartActivity($"Agent.{desc.Name}.Execute");
        activity?.SetTag("agent.id", desc.Id.ToString());
        activity?.SetTag("agent.name", desc.Name);
        activity?.SetTag("task.id", task.Id.ToString());
        activity?.SetTag("task.capability", task.RequiredCapability);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        ExecutionResult result;
        try
        {
            result = await agent.ExecuteAsync(task, context, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetTag("error", true);
            _logger.LogError(ex, "Agent {AgentName} failed task {TaskId}", desc.Name, task.Id);

            var errorTrace = new AgentExecutionTrace(
                Guid.NewGuid(),
                desc.Id, desc.Name, task.Id, task.RequiredCapability, false, sw.Elapsed.TotalMilliseconds,
                new Dictionary<string, string> { ["error"] = ex.Message },
                DateTimeOffset.UtcNow.AddMilliseconds(-sw.Elapsed.TotalMilliseconds), DateTimeOffset.UtcNow);
            await _observability.RecordAgentExecutionAsync(errorTrace, ct);

            ObservabilityService.IncrementTasksExecuted();
            ObservabilityService.IncrementTasksFailed();

            throw;
        }

        sw.Stop();
        activity?.SetTag("result.success", result.IsSuccess);

        var trace = new AgentExecutionTrace(
            activity?.TraceId.ToHexString() is string traceId ? Guid.Parse(traceId.PadLeft(32, '0')[..32]) : Guid.NewGuid(),
            desc.Id, desc.Name, task.Id, task.RequiredCapability, result.IsSuccess, sw.Elapsed.TotalMilliseconds,
            new Dictionary<string, string> { ["summary"] = result.Summary },
            DateTimeOffset.UtcNow.AddMilliseconds(-sw.Elapsed.TotalMilliseconds), DateTimeOffset.UtcNow);
        await _observability.RecordAgentExecutionAsync(trace, ct);

        ObservabilityService.IncrementTasksExecuted();
        if (!result.IsSuccess)
        {
            ObservabilityService.IncrementTasksFailed();
        }

        return result;
    }
}
