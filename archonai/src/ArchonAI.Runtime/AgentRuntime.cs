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
    private readonly ITaskExecutionManager _taskExecutionManager;
    private readonly IEventBus _eventBus;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly IAgentCapabilityRegistry _capabilityRegistry;
    private readonly IEvaluationEngine _evaluationEngine;
    private readonly IGovernanceKernel _governanceKernel;
    private readonly IAgentSupervisor _agentSupervisor;
    private readonly IMemoryStore _memoryStore;
    private readonly ITraceStore _traceStore;
    private readonly ITaskTelemetryStore _taskTelemetryStore;
    private readonly ILogger<AgentRuntime> _logger;

    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
    private bool _subscriptionsInitialized;

    public AgentRuntime(
        IEnumerable<IAgent> agentImplementations,
        ITaskExecutionManager taskExecutionManager,
        IEventBus eventBus,
        IWorkflowEngine workflowEngine,
        IAgentCapabilityRegistry capabilityRegistry,
        IEvaluationEngine evaluationEngine,
        IGovernanceKernel governanceKernel,
        IAgentSupervisor agentSupervisor,
        IMemoryStore memoryStore,
        ITraceStore traceStore,
        ITaskTelemetryStore taskTelemetryStore,
        ILogger<AgentRuntime> logger)
    {
        _agentImplementations = agentImplementations;
        _taskExecutionManager = taskExecutionManager;
        _eventBus = eventBus;
        _workflowEngine = workflowEngine;
        _capabilityRegistry = capabilityRegistry;
        _evaluationEngine = evaluationEngine;
        _governanceKernel = governanceKernel;
        _agentSupervisor = agentSupervisor;
        _memoryStore = memoryStore;
        _traceStore = traceStore;
        _taskTelemetryStore = taskTelemetryStore;
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

        await _capabilityRegistry.RegisterOrUpdateAgentAsync(
            agent,
            tools: agent.Capabilities.Select(c => c.Name).ToArray(),
            permissions: new[] { "execute:tasks", "read:memory" },
            cancellationToken);

        _logger.LogInformation("Registered agent {AgentName} ({AgentId})", agent.Name, agent.Id);
    }

    public async global::System.Threading.Tasks.Task ScheduleTaskAsync(CoreTask task, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureEventSubscriptionsAsync(cancellationToken);

        _workflowEngine.Initialize(task.Id);
        TransitionWorkflow(task.Id, WorkflowTrigger.StartPlanning);
        TransitionWorkflow(task.Id, WorkflowTrigger.Schedule);

        await _taskExecutionManager.EnqueueAsync(task, cancellationToken);
        await RecordTraceAsync(task, "decision", "Task was scheduled for execution.", new Dictionary<string, string>
        {
            ["requiredCapability"] = task.RequiredCapability
        }, cancellationToken);

        using var scheduleActivity = Telemetry.ActivitySource.StartActivity("runtime.task.schedule");
        scheduleActivity?.SetTag("task.id", task.Id.ToString());
        scheduleActivity?.SetTag("workflow.state", _workflowEngine.GetState(task.Id).ToString());

        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: TaskScheduledEvent,
            Source: nameof(AgentRuntime),
            CorrelationId: task.ObjectiveId,
            Payload: new Dictionary<string, string>
            {
                ["taskId"] = task.Id.ToString(),
                ["requiredCapability"] = task.RequiredCapability,
                ["workflowState"] = _workflowEngine.GetState(task.Id).ToString(),
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

        IReadOnlyList<ExecutionResult> results = await _taskExecutionManager.ExecuteAllAsync(async (task, ct) =>
        {
            var taskStopwatch = Stopwatch.StartNew();
            await RecordTraceAsync(task, "reasoning-step", "Starting task execution lifecycle.", new Dictionary<string, string>
            {
                ["phase"] = "execute"
            }, ct);

            TransitionWorkflow(task.Id, WorkflowTrigger.StartExecuting);

            IReadOnlyList<IAgent> candidateAgents = _agentImplementations
                .Where(a =>
                {
                    Agent descriptor = a.Describe();
                    return descriptor.IsEnabled
                        && descriptor.Capabilities.Any(capability =>
                            capability.Name.Equals(task.RequiredCapability, StringComparison.OrdinalIgnoreCase));
                })
                .ToArray();

            IAgent? agent = null;
            if (candidateAgents.Count > 0)
            {
                Agent? selectedDescriptor = await _agentSupervisor.SelectAgentAsync(
                    candidateAgents.Select(candidate => candidate.Describe()).ToArray(),
                    task,
                    ct);

                if (selectedDescriptor is not null)
                {
                    agent = candidateAgents.FirstOrDefault(candidate => candidate.Describe().Id == selectedDescriptor.Id)
                        ?? candidateAgents[0];
                }
            }

            if (agent is null)
            {
                await RecordTraceAsync(task, "decision", "No matching agent found for required capability.", new Dictionary<string, string>
                {
                    ["requiredCapability"] = task.RequiredCapability
                }, ct);

                TransitionWorkflow(task.Id, WorkflowTrigger.StartEvaluating);

                var failedResult = new ExecutionResult(
                    task.Id,
                    IsSuccess: false,
                    Summary: $"No registered agent for capability '{task.RequiredCapability}'.",
                    Outputs: new Dictionary<string, string>(),
                    Warnings: Array.Empty<string>(),
                    Errors: new[] { "AgentNotFound" },
                    CompletedAtUtc: DateTimeOffset.UtcNow);

                await RecordTaskTelemetryAsync(
                    objectiveId: task.ObjectiveId,
                    workflowId: context.CorrelationId,
                    agentId: Guid.Empty,
                    taskId: task.Id,
                    executionTimeMs: taskStopwatch.Elapsed.TotalMilliseconds,
                    cost: 0,
                    success: false,
                    errorType: "AgentNotFound",
                    cancellationToken: ct);

                TransitionWorkflow(task.Id, WorkflowTrigger.Fail);
                await PublishResultEventAsync(task, failedResult, cancellationToken);
                return failedResult;
            }

            Agent agentSnapshot = agent.Describe();
            var taskContext = context with
            {
                Metadata = new Dictionary<string, string>(context.Metadata)
                {
                    ["requiredCapability"] = task.RequiredCapability,
                    ["scheduledOrder"] = task.Order.ToString()
                }
            };

            var supervisionDecision = await _agentSupervisor.ValidateExecutionAsync(agentSnapshot, task, taskContext, ct);
            await RecordTraceAsync(task, "decision", "Supervisor decision generated.", new Dictionary<string, string>
            {
                ["isAllowed"] = supervisionDecision.IsAllowed.ToString(),
                ["supervisorReason"] = supervisionDecision.Reason,
                ["action"] = supervisionDecision.Action,
                ["signals"] = string.Join('|', supervisionDecision.Signals)
            }, ct);

            if (!supervisionDecision.IsAllowed)
            {
                TransitionWorkflow(task.Id, WorkflowTrigger.StartEvaluating);

                var deniedBySupervisorResult = new ExecutionResult(
                    task.Id,
                    IsSuccess: false,
                    Summary: $"Supervisor denied task execution: {supervisionDecision.Reason}",
                    Outputs: new Dictionary<string, string>(),
                    Warnings: supervisionDecision.Signals,
                    Errors: new[] { "SupervisorDenied" },
                    CompletedAtUtc: DateTimeOffset.UtcNow);

                await RecordTaskTelemetryAsync(
                    objectiveId: task.ObjectiveId,
                    workflowId: context.CorrelationId,
                    agentId: agentSnapshot.Id,
                    taskId: task.Id,
                    executionTimeMs: taskStopwatch.Elapsed.TotalMilliseconds,
                    cost: 0,
                    success: false,
                    errorType: "SupervisorDenied",
                    cancellationToken: ct);

                TransitionWorkflow(task.Id, WorkflowTrigger.Fail);
                await PublishResultEventAsync(task, deniedBySupervisorResult, ct);
                await _agentSupervisor.RecordExecutionCompletedAsync(agentSnapshot, deniedBySupervisorResult, taskStopwatch.Elapsed.TotalMilliseconds, ct);
                return deniedBySupervisorResult;
            }

            GovernanceDecision governanceDecision = await _governanceKernel.ValidateExecutionAsync(agentSnapshot, task, taskContext, ct);
            PolicyDecision policyDecision = governanceDecision.PolicyDecision;
            await RecordPolicyDecisionAsync(task, policyDecision, ct);
            await RecordTraceAsync(task, "decision", "Governance decision generated.", new Dictionary<string, string>
            {
                ["isAllowed"] = governanceDecision.IsAllowed.ToString(),
                ["governanceReason"] = governanceDecision.Reason,
                ["riskScore"] = policyDecision.RiskScore.ToString("F2"),
                ["requiresApproval"] = policyDecision.RequiresApproval.ToString(),
                ["confidenceScore"] = policyDecision.ConfidenceScore.ToString("F3"),
                ["manualOverrideState"] = policyDecision.ManualOverrideState,
                ["approvalCheckpoint"] = policyDecision.ApprovalCheckpoint
            }, ct);

            if (!governanceDecision.IsAllowed)
            {
                TransitionWorkflow(task.Id, WorkflowTrigger.StartEvaluating);

                var deniedResult = new ExecutionResult(
                    task.Id,
                    IsSuccess: false,
                    Summary: $"Governance denied task execution: {governanceDecision.Reason}",
                    Outputs: new Dictionary<string, string>(),
                    Warnings: governanceDecision.Violations,
                    Errors: new[] { "GovernanceDenied" },
                    CompletedAtUtc: DateTimeOffset.UtcNow);

                await RecordTaskTelemetryAsync(
                    objectiveId: task.ObjectiveId,
                    workflowId: context.CorrelationId,
                    agentId: agentSnapshot.Id,
                    taskId: task.Id,
                    executionTimeMs: taskStopwatch.Elapsed.TotalMilliseconds,
                    cost: 0,
                    success: false,
                    errorType: "GovernanceDenied",
                    cancellationToken: ct);

                TransitionWorkflow(task.Id, WorkflowTrigger.Fail);
                await PublishResultEventAsync(task, deniedResult, ct);
                await _agentSupervisor.RecordExecutionCompletedAsync(agentSnapshot, deniedResult, taskStopwatch.Elapsed.TotalMilliseconds, ct);
                return deniedResult;
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                ExecutionResult result = await agent.ExecuteAsync(task, taskContext, ct);
                stopwatch.Stop();

                decimal executionCost = result.IsSuccess ? 0.01m : 0.02m;
                await _capabilityRegistry.ReportExecutionAsync(agentSnapshot.Id, stopwatch.Elapsed.TotalMilliseconds, executionCost, ct);

                EvaluationReport evaluation = await _evaluationEngine.EvaluateAsync(
                    agentSnapshot,
                    task,
                    result,
                    stopwatch.Elapsed.TotalMilliseconds,
                    executionCost,
                    ct);

                await RecordTraceAsync(task, "reasoning-step", "Evaluation completed.", new Dictionary<string, string>
                {
                    ["score"] = evaluation.Score.ToString("F2"),
                    ["isFailure"] = evaluation.IsFailure.ToString()
                }, ct);

                await RecordEvaluationAsync(task, result, evaluation, ct);

                await RecordTaskTelemetryAsync(
                    objectiveId: task.ObjectiveId,
                    workflowId: context.CorrelationId,
                    agentId: agentSnapshot.Id,
                    taskId: task.Id,
                    executionTimeMs: taskStopwatch.Elapsed.TotalMilliseconds,
                    cost: executionCost,
                    success: result.IsSuccess,
                    errorType: result.IsSuccess
                        ? "none"
                        : (result.Errors.FirstOrDefault() ?? "ExecutionFailed"),
                    cancellationToken: ct);

                TransitionWorkflow(task.Id, WorkflowTrigger.StartEvaluating);
                TransitionWorkflow(task.Id, result.IsSuccess ? WorkflowTrigger.Complete : WorkflowTrigger.Fail);
                await PublishResultEventAsync(task, result, ct);
                await _agentSupervisor.RecordExecutionCompletedAsync(agentSnapshot, result, stopwatch.Elapsed.TotalMilliseconds, ct);
                return result;
            }
            finally
            {
                await _governanceKernel.MarkExecutionCompletedAsync(agentSnapshot.Id, ct);
            }
        }, cancellationToken);

        ReportResults(results);
        return results;
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

                await _taskExecutionManager.EnqueueAsync(task, ct);
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

    private IAgent? ResolveAgent(string capability)
    {
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
