using System.Collections.Concurrent;
using System.Diagnostics;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Workflow;
using ArchonAI.Registry;
using CoreTask = ArchonAI.Core.Models.Task;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using OTel = ArchonAI.Common.Observability.Telemetry;

namespace ArchonAI.Api.Security;

public sealed class WorkflowDesignService : IWorkflowDesignService
{
    private readonly ConcurrentDictionary<Guid, DesignedWorkflow> _workflows = new();
    private readonly IAgentCapabilityRegistry _capabilityRegistry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkflowDesignService> _logger;

    private long _totalWorkflows;
    private long _validatedWorkflows;
    private long _executedWorkflows;
    private long _failedExecutions;

    public WorkflowDesignService(
        IAgentCapabilityRegistry capabilityRegistry,
        IServiceScopeFactory scopeFactory,
        IEventBus eventBus,
        ILogger<WorkflowDesignService> logger)
    {
        _capabilityRegistry = capabilityRegistry;
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
        _logger = logger;
    }

    public global::System.Threading.Tasks.Task<DesignedWorkflow> CreateWorkflowAsync(
        string name,
        string description,
        string strategy,
        IReadOnlyList<WorkflowStepDefinition> steps,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("WorkflowDesign.Create");

        var workflowId = Guid.NewGuid();
        var workflow = new DesignedWorkflow(
            workflowId, name, description, strategy, steps,
            metadata ?? new Dictionary<string, string>(),
            WorkflowDesignStatus.Draft,
            Array.Empty<string>(),
            DateTimeOffset.UtcNow,
            null);

        _workflows[workflowId] = workflow;
        Interlocked.Increment(ref _totalWorkflows);
        OTel.WorkflowDesignsCreated.Add(1);

        _logger.LogInformation("Workflow design created: {WorkflowId} '{Name}' with {StepCount} steps",
            workflowId, name, steps.Count);

        _ = EmitAuditEventAsync("workflow.design.created", workflowId.ToString(), name);

        return global::System.Threading.Tasks.Task.FromResult(workflow);
    }

    public global::System.Threading.Tasks.Task<DesignedWorkflow?> GetWorkflowAsync(
        Guid workflowId, CancellationToken ct = default)
    {
        _workflows.TryGetValue(workflowId, out var workflow);
        return global::System.Threading.Tasks.Task.FromResult(workflow);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<DesignedWorkflow>> ListWorkflowsAsync(
        WorkflowDesignStatus? status = null, int offset = 0, int limit = 50, CancellationToken ct = default)
    {
        IEnumerable<DesignedWorkflow> query = _workflows.Values
            .OrderByDescending(w => w.CreatedAtUtc);

        if (status.HasValue)
            query = query.Where(w => w.Status == status.Value);

        IReadOnlyList<DesignedWorkflow> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public async global::System.Threading.Tasks.Task<WorkflowValidationResult> ValidateWorkflowAsync(
        Guid workflowId, CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("WorkflowDesign.Validate");
        OTel.WorkflowDesignsValidated.Add(1);

        if (!_workflows.TryGetValue(workflowId, out var workflow))
        {
            return new WorkflowValidationResult(workflowId, false,
                new[] { "Workflow not found" }, Array.Empty<string>(), DateTimeOffset.UtcNow);
        }

        var errors = new List<string>();
        var warnings = new List<string>();

        // Validate basic structure
        if (string.IsNullOrWhiteSpace(workflow.Name))
            errors.Add("Workflow name is required");

        if (workflow.Steps.Count == 0)
            errors.Add("Workflow must have at least one step");

        if (string.IsNullOrWhiteSpace(workflow.Strategy))
            errors.Add("Workflow strategy is required");

        // Validate step ordering
        var orders = workflow.Steps.Select(s => s.Order).ToList();
        if (orders.Distinct().Count() != orders.Count)
            errors.Add("Step order values must be unique");

        // Validate agent types against registered capabilities
        var allAgents = await _capabilityRegistry.GetAllAsync(ct);
        var knownCapabilities = allAgents
            .SelectMany(a => a.Capabilities)
            .Select(c => c.ToLowerInvariant())
            .ToHashSet();

        foreach (var step in workflow.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name))
                errors.Add($"Step at order {step.Order} has no name");

            if (string.IsNullOrWhiteSpace(step.AgentType))
                errors.Add($"Step '{step.Name}' has no agent type");
            else if (knownCapabilities.Count > 0 && !knownCapabilities.Contains(step.AgentType.ToLowerInvariant()))
                warnings.Add($"Step '{step.Name}' references agent type '{step.AgentType}' which is not currently registered");
        }

        // Validate step dependencies (inputs referencing prior steps)
        var stepNames = workflow.Steps.Select(s => s.Name?.ToLowerInvariant()).ToHashSet();
        foreach (var step in workflow.Steps)
        {
            if (step.Inputs.TryGetValue("dependsOn", out var dep)
                && !string.IsNullOrWhiteSpace(dep)
                && !stepNames.Contains(dep.ToLowerInvariant()))
            {
                errors.Add($"Step '{step.Name}' depends on unknown step '{dep}'");
            }
        }

        bool isValid = errors.Count == 0;
        var now = DateTimeOffset.UtcNow;

        var updatedWorkflow = workflow with
        {
            Status = isValid ? WorkflowDesignStatus.Validated : WorkflowDesignStatus.Invalid,
            ValidationErrors = errors,
            ValidatedAtUtc = now
        };
        _workflows[workflowId] = updatedWorkflow;

        if (isValid)
            Interlocked.Increment(ref _validatedWorkflows);

        _logger.LogInformation("Workflow {WorkflowId} validated: IsValid={IsValid}, Errors={ErrorCount}, Warnings={WarningCount}",
            workflowId, isValid, errors.Count, warnings.Count);

        return new WorkflowValidationResult(workflowId, isValid, errors, warnings, now);
    }

    public async global::System.Threading.Tasks.Task<WorkflowExecutionSummary> ExecuteWorkflowAsync(
        Guid workflowId,
        string tenantId,
        IReadOnlyDictionary<string, string>? executionMetadata = null,
        CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("WorkflowDesign.Execute");
        OTel.WorkflowDesignsExecuted.Add(1);

        if (!_workflows.TryGetValue(workflowId, out var workflow))
            throw new InvalidOperationException($"Workflow {workflowId} not found");

        if (workflow.Status != WorkflowDesignStatus.Validated)
        {
            // Auto-validate before execution
            var validation = await ValidateWorkflowAsync(workflowId, ct);
            if (!validation.IsValid)
                throw new InvalidOperationException(
                    $"Workflow {workflowId} failed validation: {string.Join("; ", validation.Errors)}");

            _workflows.TryGetValue(workflowId, out workflow);
        }

        _workflows[workflowId] = workflow! with { Status = WorkflowDesignStatus.Executing };

        var objectiveId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        // Convert workflow steps to executable tasks
        var tasks = workflow!.Steps
            .OrderBy(s => s.Order)
            .Select(step => new CoreTask(
                Guid.NewGuid(),
                objectiveId,
                step.Order,
                step.Name,
                step.Description,
                step.AgentType,
                step.Inputs,
                DateTimeOffset.UtcNow,
                null,
                null))
            .ToList();

        // Schedule and execute tasks via a scoped IRuntime
        var allResults = new List<ExecutionResult>();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var runtime = scope.ServiceProvider.GetRequiredService<IRuntime>();

        foreach (var task in tasks)
        {
            await runtime.ScheduleTaskAsync(task, ct);
        }

        var context = new CoreExecutionContext(
            Guid.NewGuid(), objectiveId, tasks[0].Id, tenantId,
            executionMetadata ?? new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);

        var results = await runtime.ExecuteScheduledTasksAsync(context, ct);
        allResults.AddRange(results);

        sw.Stop();

        int succeeded = allResults.Count(r => r.IsSuccess);
        int failed = allResults.Count(r => !r.IsSuccess);
        bool isSuccess = failed == 0 && succeeded > 0;

        _workflows[workflowId] = workflow with
        {
            Status = isSuccess ? WorkflowDesignStatus.Completed : WorkflowDesignStatus.Failed
        };

        Interlocked.Increment(ref _executedWorkflows);
        if (!isSuccess)
            Interlocked.Increment(ref _failedExecutions);

        _ = EmitAuditEventAsync(
            isSuccess ? "workflow.design.executed" : "workflow.design.failed",
            workflowId.ToString(), workflow.Name);

        _logger.LogInformation(
            "Workflow {WorkflowId} execution completed: Success={IsSuccess}, Tasks={Total}, Succeeded={Succeeded}, Failed={Failed}, Duration={DurationMs}ms",
            workflowId, isSuccess, tasks.Count, succeeded, failed, sw.Elapsed.TotalMilliseconds);

        return new WorkflowExecutionSummary(
            workflowId, objectiveId, workflow.Name, isSuccess,
            tasks.Count, succeeded, failed,
            allResults, sw.Elapsed.TotalMilliseconds,
            startedAt, DateTimeOffset.UtcNow);
    }

    public global::System.Threading.Tasks.Task<bool> DeleteWorkflowAsync(
        Guid workflowId, CancellationToken ct = default)
    {
        bool removed = _workflows.TryRemove(workflowId, out _);
        return global::System.Threading.Tasks.Task.FromResult(removed);
    }

    public WorkflowDesignServiceStatus GetStatus()
    {
        return new WorkflowDesignServiceStatus(
            IsActive: true,
            TotalWorkflows: Interlocked.Read(ref _totalWorkflows),
            ValidatedWorkflows: Interlocked.Read(ref _validatedWorkflows),
            ExecutedWorkflows: Interlocked.Read(ref _executedWorkflows),
            FailedExecutions: Interlocked.Read(ref _failedExecutions),
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task EmitAuditEventAsync(string eventType, string resourceId, string description)
    {
        try
        {
            await _eventBus.PublishAsync(new SystemEvent(
                Guid.NewGuid(), eventType, "WorkflowDesignService",
                Guid.NewGuid(),
                new Dictionary<string, string>
                {
                    ["resourceId"] = resourceId,
                    ["description"] = description
                },
                DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit audit event {EventType}", eventType);
        }
    }
}
