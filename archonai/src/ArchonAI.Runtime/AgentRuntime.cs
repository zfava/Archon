using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Evaluation;
using ArchonAI.Core.Models.Policy;
using ArchonAI.Core.Models.Trace;
using ArchonAI.Core.Models.Telemetry;
using ArchonAI.Core.Models.TaskRuntime;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Runtime.Execution;
using ArchonAI.Registry;
using ArchonAI.Workflow;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Runtime;

/// <summary>
/// Agent runtime capable of agent registration, task scheduling, concurrent task execution, and result reporting.
/// Runtime services communicate via event publishing/subscription.
/// </summary>
public sealed class AgentRuntime : IRuntime
{
    private const string TaskDistributedEvent = "task.distribute";
    private const string TaskScheduledEvent = "task.scheduled";
    private const string TaskCompletedEvent = "task.completed";
    private const string TaskFailedEvent = "task.failed";

    private readonly ConcurrentDictionary<Guid, Agent> _agentRegistry = new();
    private readonly IEnumerable<IAgent> _agentImplementations;
    private readonly IDistributedTaskOrchestrator _taskOrchestrator;
    private readonly IEventBus _eventBus;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly IWorkflowExecutionEngine _workflowExecutionEngine;
    private readonly ITaskExecutionEngine _taskExecutionEngine;
    private readonly IAgentCapabilityRegistry _capabilityRegistry;
    private readonly IEvaluationEngine _evaluationEngine;
    private readonly IGovernanceKernel _governanceKernel;
    private readonly IAgentSupervisor _agentSupervisor;
    private readonly IAgentSandboxManager _agentSandboxManager;
    private readonly IAgentIdentityStore _identityStore;
    private readonly IMemoryStore _memoryStore;
    private readonly ITraceStore _traceStore;
    private readonly ITaskTelemetryStore _taskTelemetryStore;
    private readonly IPerformanceAnalyzer _performanceAnalyzer;
    private readonly ILogger<AgentRuntime> _logger;

    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
    private bool _subscriptionsInitialized;

    public AgentRuntime(
        IEnumerable<IAgent> agentImplementations,
        IDistributedTaskOrchestrator taskOrchestrator,
        IEventBus eventBus,
        IWorkflowEngine workflowEngine,
        IWorkflowExecutionEngine workflowExecutionEngine,
        ITaskExecutionEngine taskExecutionEngine,
        IAgentCapabilityRegistry capabilityRegistry,
        IEvaluationEngine evaluationEngine,
        IGovernanceKernel governanceKernel,
        IAgentSupervisor agentSupervisor,
        IAgentSandboxManager agentSandboxManager,
        IAgentIdentityStore identityStore,
        IMemoryStore memoryStore,
        ITraceStore traceStore,
        ITaskTelemetryStore taskTelemetryStore,
        IPerformanceAnalyzer performanceAnalyzer,
        ILogger<AgentRuntime> logger)
    {
        _agentImplementations = agentImplementations;
        _taskOrchestrator = taskOrchestrator;
        _eventBus = eventBus;
        _workflowEngine = workflowEngine;
        _workflowExecutionEngine = workflowExecutionEngine;
        _taskExecutionEngine = taskExecutionEngine;
        _capabilityRegistry = capabilityRegistry;
        _evaluationEngine = evaluationEngine;
        _governanceKernel = governanceKernel;
        _agentSupervisor = agentSupervisor;
        _agentSandboxManager = agentSandboxManager;
        _identityStore = identityStore;
        _memoryStore = memoryStore;
        _traceStore = traceStore;
        _taskTelemetryStore = taskTelemetryStore;
        _performanceAnalyzer = performanceAnalyzer;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task RegisterAgentAsync(Agent agent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        GovernanceDecision registrationDecision = await _governanceKernel.ValidateAgentRegistrationAsync(agent, cancellationToken);
        if (!registrationDecision.IsAllowed)
        {
            _logger.LogWarning("Governance denied registration for agent {AgentId}: {Reason}", agent.Id, registrationDecision.Reason);
            throw new InvalidOperationException($"Agent registration denied by governance kernel: {registrationDecision.Reason}");
        }

        _agentRegistry[agent.Id] = agent;
        await _agentSupervisor.RegisterAgentAsync(agent, cancellationToken);

        var permissions = new[] { "execute:tasks", "read:memory" };

        await _capabilityRegistry.RegisterOrUpdateAgentAsync(
            agent,
            tools: agent.Capabilities.Select(c => c.Name).ToArray(),
            permissions: permissions,
            cancellationToken);

        var taskTypes = agent.Capabilities
            .Select(c => c.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (taskTypes.Length > 0)
        {
            await _capabilityRegistry.RegisterSupportedTaskTypesAsync(agent.Id, taskTypes, cancellationToken);
        }

        await _identityStore.RegisterOrUpdateAsync(agent, permissions, cancellationToken);

        _logger.LogInformation("Registered agent {AgentName} ({AgentId})", agent.Name, agent.Id);
    }

    public async global::System.Threading.Tasks.Task ScheduleTaskAsync(CoreTask task, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureEventSubscriptionsAsync(cancellationToken);

        await _workflowExecutionEngine.InitializeWorkflowAsync(task.ObjectiveId, cancellationToken);
        await _workflowExecutionEngine.CreateTaskQueueAsync(task.ObjectiveId, new[] { task }, cancellationToken);
        await _workflowExecutionEngine.DispatchTasksToSchedulerAsync(task.ObjectiveId, 1, cancellationToken);
        await RecordTraceAsync(task, "decision", "Task was scheduled for execution.", new Dictionary<string, string>
        {
            ["requiredCapability"] = task.RequiredCapability
        }, cancellationToken);

        using var scheduleActivity = Telemetry.ActivitySource.StartActivity("runtime.task.schedule");
        scheduleActivity?.SetTag("task.id", task.Id.ToString());
        scheduleActivity?.SetTag("workflow.state", _workflowExecutionEngine.GetWorkflowState(task.ObjectiveId).ToString());

        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: TaskScheduledEvent,
            Source: nameof(AgentRuntime),
            CorrelationId: task.ObjectiveId,
            Payload: new Dictionary<string, string>
            {
                ["taskId"] = task.Id.ToString(),
                ["requiredCapability"] = task.RequiredCapability,
                ["workflowState"] = _workflowExecutionEngine.GetWorkflowState(task.ObjectiveId).ToString(),
                ["task"] = JsonSerializer.Serialize(task)
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<ExecutionResult>> ExecuteScheduledTasksAsync(
        CoreExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        await EnsureEventSubscriptionsAsync(cancellationToken);

        using var executionActivity = Telemetry.ActivitySource.StartActivity("runtime.scheduled.execute");

        IReadOnlyList<ExecutionResult> results = await _taskOrchestrator.ExecuteAllAsync(context, async (task, ct) =>
        {
            await RecordTraceAsync(task, "reasoning-step", "Starting task execution lifecycle.", new Dictionary<string, string>
            {
                ["phase"] = "execute"
            }, ct);

            TransitionWorkflow(task.Id, WorkflowTrigger.StartExecuting);

            TaskExecutionOutcome outcome = await _taskExecutionEngine.ExecuteTaskAsync(
                task,
                context,
                _agentImplementations.ToArray(),
                ct);

            if (outcome.AgentId != Guid.Empty)
            {
                await _identityStore.RecordExecutionAsync(
                    agentId: outcome.AgentId,
                    taskId: outcome.Result.TaskId,
                    success: outcome.Result.IsSuccess,
                    executionTimeMs: outcome.ExecutionTimeMs,
                    cost: outcome.Cost,
                    errorType: outcome.ErrorType,
                    executedAtUtc: outcome.CompletedAtUtc,
                    cancellationToken: ct);

                string agentName = _agentRegistry.TryGetValue(outcome.AgentId, out Agent? registeredAgent)
                    ? registeredAgent.Name
                    : outcome.AgentId.ToString();

                _performanceAnalyzer.RecordAgentExecution(
                    outcome.AgentId,
                    agentName,
                    outcome.Result.IsSuccess,
                    outcome.ExecutionTimeMs,
                    outcome.Cost);
            }

            _performanceAnalyzer.RecordTaskCompletion(
                task.RequiredCapability,
                outcome.Result.IsSuccess,
                outcome.ExecutionTimeMs,
                outcome.Cost);

            await _capabilityRegistry.ReportExecutionAsync(
                outcome.AgentId,
                task.RequiredCapability,
                outcome.Result.IsSuccess,
                outcome.ExecutionTimeMs,
                outcome.Cost,
                ct);

            await RecordTaskTelemetryAsync(
                objectiveId: task.ObjectiveId,
                workflowId: context.CorrelationId,
                agentId: outcome.AgentId,
                taskId: task.Id,
                executionTimeMs: outcome.ExecutionTimeMs,
                cost: outcome.Cost,
                success: outcome.Result.IsSuccess,
                errorType: outcome.ErrorType,
                cancellationToken: ct);

            TransitionWorkflow(task.Id, WorkflowTrigger.StartEvaluating);
            TransitionWorkflow(task.Id, outcome.Result.IsSuccess ? WorkflowTrigger.Complete : WorkflowTrigger.Fail);

            await PublishResultEventAsync(task, outcome.Result, ct);
            return outcome.Result;
        }, cancellationToken);

        ReportResults(results);
        await _workflowExecutionEngine.UpdateExecutionResultsAsync(context.CorrelationId, results, cancellationToken);
        return results;
    }


    private async global::System.Threading.Tasks.Task RecordIdentityExecutionAsync(
        Agent agent,
        ExecutionResult result,
        double executionTimeMs,
        CancellationToken cancellationToken)
    {
        await _identityStore.RecordExecutionAsync(
            agentId: agent.Id,
            taskId: result.TaskId,
            success: result.IsSuccess,
            executionTimeMs: executionTimeMs,
            cost: result.IsSuccess ? 0.01m : 0.02m,
            errorType: result.IsSuccess ? "none" : (result.Errors.FirstOrDefault() ?? "ExecutionFailed"),
            executedAtUtc: result.CompletedAtUtc,
            cancellationToken: cancellationToken);
    }

    private async global::System.Threading.Tasks.Task RecordPolicyDecisionAsync(
        CoreTask task,
        PolicyDecision decision,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>
        {
            ["taskId"] = task.Id.ToString(),
            ["objectiveId"] = task.ObjectiveId.ToString(),
            ["isAllowed"] = decision.IsAllowed.ToString(),
            ["riskScore"] = decision.RiskScore.ToString("F2"),
            ["confidenceScore"] = decision.ConfidenceScore.ToString("F3"),
            ["requiresApproval"] = decision.RequiresApproval.ToString(),
            ["approvalState"] = decision.ApprovalState,
            ["manualOverrideState"] = decision.ManualOverrideState,
            ["approvalCheckpoint"] = decision.ApprovalCheckpoint,
            ["guardrailViolations"] = string.Join('|', decision.GuardrailViolations)
        };

        var memoryRecord = new MemoryRecord(
            Id: Guid.NewGuid(),
            MemoryType: "policy",
            Scope: $"task:{task.Id}",
            Content: decision.Reason,
            Metadata: metadata,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: null);

        await _memoryStore.SaveAsync(memoryRecord, cancellationToken);
    }

    private async global::System.Threading.Tasks.Task RecordEvaluationAsync(
        CoreTask task,
        ExecutionResult result,
        EvaluationReport evaluation,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>
        {
            ["taskId"] = task.Id.ToString(),
            ["objectiveId"] = task.ObjectiveId.ToString(),
            ["agentId"] = evaluation.AgentId.ToString(),
            ["score"] = evaluation.Score.ToString("F2"),
            ["latencyMs"] = evaluation.LatencyMs.ToString("F2"),
            ["cost"] = evaluation.Cost.ToString(),
            ["isFailure"] = evaluation.IsFailure.ToString(),
            ["failurePatterns"] = string.Join('|', evaluation.FailurePatterns.Select(p => p.Pattern))
        };

        var memoryRecord = new MemoryRecord(
            Id: Guid.NewGuid(),
            MemoryType: "evaluation",
            Scope: $"task:{task.Id}",
            Content: result.Summary,
            Metadata: metadata,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: null);

        await _memoryStore.SaveAsync(memoryRecord, cancellationToken);
    }

    private async global::System.Threading.Tasks.Task EnsureEventSubscriptionsAsync(CancellationToken cancellationToken)
    {
        if (_subscriptionsInitialized)
        {
            return;
        }

        await _subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            if (_subscriptionsInitialized)
            {
                return;
            }

            await _eventBus.SubscribeAsync(TaskDistributedEvent, async (evt, ct) =>
            {
                if (!evt.Payload.TryGetValue("task", out string? serializedTask) || string.IsNullOrWhiteSpace(serializedTask))
                {
                    return;
                }

                CoreTask? task = JsonSerializer.Deserialize<CoreTask>(serializedTask);
                if (task is null)
                {
                    return;
                }

                await _taskOrchestrator.EnqueueAsync(task, ct);
            }, cancellationToken);

            _subscriptionsInitialized = true;
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    private async global::System.Threading.Tasks.Task PublishResultEventAsync(CoreTask task, ExecutionResult result, CancellationToken cancellationToken)
    {
        string eventType = result.IsSuccess ? TaskCompletedEvent : TaskFailedEvent;

        using var activity = Telemetry.ActivitySource.StartActivity("runtime.task.result.publish");
        activity?.SetTag("task.id", task.Id.ToString());
        activity?.SetTag("workflow.state", _workflowEngine.GetState(task.Id).ToString());

        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: eventType,
            Source: nameof(AgentRuntime),
            CorrelationId: task.ObjectiveId,
            Payload: new Dictionary<string, string>
            {
                ["taskId"] = task.Id.ToString(),
                ["isSuccess"] = result.IsSuccess.ToString(),
                ["summary"] = result.Summary,
                ["workflowState"] = _workflowEngine.GetState(task.Id).ToString()
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), cancellationToken);
    }

    private void TransitionWorkflow(Guid taskId, WorkflowTrigger trigger)
    {
        WorkflowState newState = _workflowEngine.Transition(taskId, trigger);
        _logger.LogDebug("Workflow {TaskId} transitioned via {Trigger} => {State}", taskId, trigger, newState);
    }

    private async global::System.Threading.Tasks.Task<IAgent?> ResolveAgentAsync(string capability, CancellationToken cancellationToken = default)
    {
        // Try the capability registry for performance-based selection
        var selection = await _capabilityRegistry.SelectBestAgentAsync(capability, taskType: null, cancellationToken);
        if (selection is not null)
        {
            var impl = _agentImplementations.FirstOrDefault(a => a.Describe().Id == selection.AgentId);
            if (impl is not null)
            {
                _logger.LogDebug("Registry selected agent {AgentName} for '{Capability}': {Reason}",
                    selection.AgentName, capability, selection.SelectionReason);
                return impl;
            }
        }

        // Fallback: direct capability match on registered agents
        var enabledAgentIds = _agentRegistry
            .Where(entry => entry.Value.IsEnabled && entry.Value.Capabilities.Any(c => c.Name.Equals(capability, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.Key)
            .ToHashSet();

        return _agentImplementations.FirstOrDefault(impl => enabledAgentIds.Contains(impl.Describe().Id));
    }

    private void ReportResults(IReadOnlyList<ExecutionResult> results)
    {
        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count - successCount;
        _logger.LogInformation("Runtime execution completed. Success={SuccessCount}, Failure={FailureCount}", successCount, failureCount);
    }

    private global::System.Threading.Tasks.Task RecordTraceAsync(
        CoreTask task,
        string category,
        string message,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        return _traceStore.RecordAsync(new TraceEntry(
            Id: Guid.NewGuid(),
            Scope: $"task:{task.Id}",
            Category: category,
            Message: message,
            Metadata: metadata ?? new Dictionary<string, string>(),
            RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
    }

    private global::System.Threading.Tasks.Task RecordTaskTelemetryAsync(
        Guid objectiveId,
        Guid workflowId,
        Guid agentId,
        Guid taskId,
        double executionTimeMs,
        decimal cost,
        bool success,
        string errorType,
        CancellationToken cancellationToken)
    {
        return _taskTelemetryStore.RecordAsync(new TaskExecutionTelemetry(
            Id: Guid.NewGuid(),
            ObjectiveId: objectiveId,
            WorkflowId: workflowId,
            AgentId: agentId,
            TaskId: taskId,
            ExecutionTimeMs: Math.Max(0, executionTimeMs),
            Cost: Math.Max(0, cost),
            Success: success,
            ErrorType: string.IsNullOrWhiteSpace(errorType) ? "none" : errorType,
            RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
    }
}
