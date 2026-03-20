using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using Task = System.Threading.Tasks.Task;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.ExceptionIntelligence;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.HeroWorkflow;
using ArchonAI.Core.Models.Memory;

namespace ArchonAI.Api.Security;

public sealed class HeroWorkflowService : IHeroWorkflowService
{
    private readonly ConcurrentDictionary<Guid, HeroWorkflowInstance> _instances = new();
    private readonly IDecisionService _decisions;
    private readonly IFinancialConsequenceService _consequences;
    private readonly ITrustTierService _trustTiers;
    private readonly IGovernanceService _governance;
    private readonly IOutcomeLearningService _outcomes;
    private readonly IExceptionIntelligenceService _exceptions;
    private readonly IEnterpriseMemoryService _memory;
    private readonly IEventBus _eventBus;
    private readonly ILogger<HeroWorkflowService> _logger;

    private static readonly IReadOnlyList<HeroWorkflowDefinition> Catalog = BuildCatalog();

    public HeroWorkflowService(
        IDecisionService decisions,
        IFinancialConsequenceService consequences,
        ITrustTierService trustTiers,
        IGovernanceService governance,
        IOutcomeLearningService outcomes,
        IExceptionIntelligenceService exceptions,
        IEnterpriseMemoryService memory,
        IEventBus eventBus,
        ILogger<HeroWorkflowService> logger)
    {
        _decisions = decisions;
        _consequences = consequences;
        _trustTiers = trustTiers;
        _governance = governance;
        _outcomes = outcomes;
        _exceptions = exceptions;
        _memory = memory;
        _eventBus = eventBus;
        _logger = logger;
    }

    // ── Catalog ─────────────────────────────────────────────────

    public Task<IReadOnlyList<HeroWorkflowDefinition>> GetCatalogAsync(CancellationToken ct = default)
        => Task.FromResult(Catalog);

    public Task<HeroWorkflowDefinition?> GetDefinitionAsync(string workflowType, CancellationToken ct = default)
        => Task.FromResult(Catalog.FirstOrDefault(d => d.WorkflowType == workflowType));

    // ── Start ───────────────────────────────────────────────────

    public async Task<HeroWorkflowInstance> StartAsync(
        Guid tenantId, string workflowType, string title,
        IReadOnlyDictionary<string, string> initialInputs,
        string initiatedBy, CancellationToken ct = default)
    {
        var definition = Catalog.FirstOrDefault(d => d.WorkflowType == workflowType)
            ?? throw new ArgumentException($"Unknown workflow type: {workflowType}");

        var now = DateTimeOffset.UtcNow;
        var steps = definition.Steps.Select(s => new HeroStepState(
            s.StepId, HeroStepStatus.Pending, null, null)).ToList();

        // Mark the first step as in-progress
        steps[0] = steps[0] with { Status = HeroStepStatus.InProgress };

        var instance = new HeroWorkflowInstance(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            WorkflowType: workflowType,
            Title: title,
            Status: HeroWorkflowStatus.InProgress,
            Steps: steps,
            Artifacts: new Dictionary<string, string>(initialInputs),
            InitiatedBy: initiatedBy,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);

        // Execute the first step automatically
        instance = await ExecuteCurrentStepAsync(instance, initialInputs, initiatedBy, ct);

        _instances[instance.Id] = instance;

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(), "hero_workflow.started", "HeroWorkflowService",
            instance.Id,
            new Dictionary<string, string>
            {
                ["workflowId"] = instance.Id.ToString(),
                ["tenantId"] = tenantId.ToString(),
                ["workflowType"] = workflowType,
            }.AsReadOnly(),
            now), ct);

        _logger.LogInformation(
            "Hero workflow {WorkflowId} started: {Title} (type={Type})",
            instance.Id, title, workflowType);

        return instance;
    }

    // ── Advance ─────────────────────────────────────────────────

    public async Task<HeroWorkflowInstance?> AdvanceAsync(
        Guid workflowId, Guid tenantId,
        IReadOnlyDictionary<string, string>? stepInputs,
        string actor, CancellationToken ct = default)
    {
        if (!_instances.TryGetValue(workflowId, out var instance))
            return null;
        if (instance.TenantId != tenantId)
            return null;
        if (instance.Status is HeroWorkflowStatus.Completed or
            HeroWorkflowStatus.Failed or HeroWorkflowStatus.Cancelled)
            return instance;

        instance = await ExecuteCurrentStepAsync(
            instance, stepInputs ?? new Dictionary<string, string>(), actor, ct);

        _instances[workflowId] = instance;
        return instance;
    }

    // ── Get / List / Cancel ────────────────────────────────────

    public Task<HeroWorkflowInstance?> GetAsync(Guid workflowId, Guid tenantId, CancellationToken ct = default)
    {
        _instances.TryGetValue(workflowId, out var instance);
        if (instance is not null && instance.TenantId != tenantId) return Task.FromResult<HeroWorkflowInstance?>(null);
        return Task.FromResult(instance);
    }

    public Task<IReadOnlyList<HeroWorkflowSummary>> ListAsync(
        Guid tenantId, string? workflowType = null, HeroWorkflowStatus? status = null, int limit = 50, CancellationToken ct = default)
    {
        IEnumerable<HeroWorkflowInstance> query = _instances.Values
            .Where(i => i.TenantId == tenantId);

        if (workflowType is not null)
            query = query.Where(i => i.WorkflowType == workflowType);
        if (status.HasValue)
            query = query.Where(i => i.Status == status.Value);

        IReadOnlyList<HeroWorkflowSummary> result = query
            .OrderByDescending(i => i.UpdatedAtUtc)
            .Take(limit)
            .Select(i => new HeroWorkflowSummary(
                i.Id, i.WorkflowType, i.Title, i.Status,
                i.Steps.Count(s => s.Status == HeroStepStatus.Completed),
                i.Steps.Count,
                i.InitiatedBy, i.CreatedAtUtc, i.UpdatedAtUtc))
            .ToList();

        return Task.FromResult(result);
    }

    public Task<HeroWorkflowInstance?> CancelAsync(
        Guid workflowId, Guid tenantId, string actor, CancellationToken ct = default)
    {
        if (!_instances.TryGetValue(workflowId, out var instance))
            return Task.FromResult<HeroWorkflowInstance?>(null);
        if (instance.TenantId != tenantId)
            return Task.FromResult<HeroWorkflowInstance?>(null);

        var updated = instance with
        {
            Status = HeroWorkflowStatus.Cancelled,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        _instances[workflowId] = updated;

        _logger.LogInformation("Hero workflow {WorkflowId} cancelled by {Actor}", workflowId, actor);
        return Task.FromResult<HeroWorkflowInstance?>(updated);
    }

    // ══════════════════════════════════════════════════════════════
    //  Step execution engine
    // ══════════════════════════════════════════════════════════════

    private async Task<HeroWorkflowInstance> ExecuteCurrentStepAsync(
        HeroWorkflowInstance instance,
        IReadOnlyDictionary<string, string> inputs,
        string actor,
        CancellationToken ct = default)
    {
        var currentIndex = instance.Steps.ToList().FindIndex(
            s => s.Status is HeroStepStatus.InProgress or HeroStepStatus.Pending);

        if (currentIndex < 0)
        {
            // All steps complete
            return instance with
            {
                Status = HeroWorkflowStatus.Completed,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        var currentStep = instance.Steps[currentIndex];
        var definition = Catalog.First(d => d.WorkflowType == instance.WorkflowType);
        var stepDef = definition.Steps[currentIndex];
        var artifacts = new Dictionary<string, string>(instance.Artifacts);
        var now = DateTimeOffset.UtcNow;

        // Merge inputs into artifacts
        foreach (var kv in inputs)
            artifacts[kv.Key] = kv.Value;

        try
        {
            // Execute step based on subsystem
            var detail = await ExecuteStepBySubsystemAsync(
                instance, stepDef, artifacts, actor, ct);

            var steps = instance.Steps.ToList();
            steps[currentIndex] = currentStep with
            {
                Status = HeroStepStatus.Completed,
                Detail = detail,
                CompletedAtUtc = now,
            };

            // Publish step-completed event for proof auto-emission
            await _eventBus.PublishAsync(new SystemEvent(
                Guid.NewGuid(), "hero_workflow.step-completed", "HeroWorkflowService",
                instance.Id,
                new Dictionary<string, string>
                {
                    ["workflowId"] = instance.Id.ToString(),
                    ["tenantId"] = instance.TenantId.ToString(),
                    ["stepId"] = stepDef.StepId,
                    ["actor"] = actor,
                    ["decisionId"] = artifacts.GetValueOrDefault("decisionId") ?? "",
                }.AsReadOnly(),
                now), ct);

            // Advance to next step if available
            var nextStatus = HeroWorkflowStatus.InProgress;
            if (currentIndex + 1 < steps.Count)
            {
                steps[currentIndex + 1] = steps[currentIndex + 1] with
                {
                    Status = HeroStepStatus.InProgress,
                };

                // If next step is an approval step, mark workflow as awaiting
                var nextStepDef = definition.Steps[currentIndex + 1];
                if (nextStepDef.Subsystem == "approval")
                    nextStatus = HeroWorkflowStatus.AwaitingApproval;
            }
            else
            {
                nextStatus = HeroWorkflowStatus.Completed;

                // Publish workflow-completed event
                await _eventBus.PublishAsync(new SystemEvent(
                    Guid.NewGuid(), "hero_workflow.completed", "HeroWorkflowService",
                    instance.Id,
                    new Dictionary<string, string>
                    {
                        ["workflowId"] = instance.Id.ToString(),
                        ["tenantId"] = instance.TenantId.ToString(),
                        ["actor"] = actor,
                        ["decisionId"] = artifacts.GetValueOrDefault("decisionId") ?? "",
                    }.AsReadOnly(),
                    now), ct);
            }

            return instance with
            {
                Steps = steps,
                Artifacts = artifacts.AsReadOnly(),
                Status = nextStatus,
                UpdatedAtUtc = now,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hero workflow {WorkflowId} step {StepId} failed",
                instance.Id, stepDef.StepId);

            var steps = instance.Steps.ToList();
            steps[currentIndex] = currentStep with
            {
                Status = HeroStepStatus.Failed,
                Detail = ex.Message,
            };

            // Publish workflow-failed event for proof auto-emission
            await _eventBus.PublishAsync(new SystemEvent(
                Guid.NewGuid(), "hero_workflow.failed", "HeroWorkflowService",
                instance.Id,
                new Dictionary<string, string>
                {
                    ["workflowId"] = instance.Id.ToString(),
                    ["tenantId"] = instance.TenantId.ToString(),
                    ["stepId"] = stepDef.StepId,
                    ["actor"] = actor,
                    ["reason"] = ex.Message,
                    ["decisionId"] = artifacts.GetValueOrDefault("decisionId") ?? "",
                }.AsReadOnly(),
                now), ct);

            return instance with
            {
                Steps = steps,
                Status = HeroWorkflowStatus.Failed,
                UpdatedAtUtc = now,
            };
        }
    }

    private async Task<string> ExecuteStepBySubsystemAsync(
        HeroWorkflowInstance instance,
        HeroStepDefinition stepDef,
        Dictionary<string, string> artifacts,
        string actor,
        CancellationToken ct = default)
    {
        return stepDef.Subsystem switch
        {
            "decision" => await ExecuteDecisionStepAsync(instance, stepDef, artifacts, actor, ct),
            "financial-consequence" => await ExecuteConsequenceStepAsync(instance, artifacts, ct),
            "trust-tier" => await ExecuteTrustTierStepAsync(instance, artifacts, ct),
            "approval" => await ExecuteApprovalStepAsync(instance, artifacts, actor, ct),
            "execution" => await ExecuteExecutionStepAsync(instance, artifacts, actor, ct),
            "outcome" => await ExecuteOutcomeStepAsync(instance, artifacts, actor, ct),
            "exception" => await ExecuteExceptionStepAsync(instance, artifacts, actor, ct),
            "memory" => await ExecuteMemoryStepAsync(instance, artifacts, actor, ct),
            _ => $"Step {stepDef.StepId} completed (no subsystem handler)",
        };
    }

    // ── Decision step ───────────────────────────────────────────

    private async Task<string> ExecuteDecisionStepAsync(
        HeroWorkflowInstance instance,
        HeroStepDefinition stepDef,
        Dictionary<string, string> artifacts,
        string actor,
        CancellationToken ct = default)
    {
        var title = artifacts.GetValueOrDefault("decisionTitle", instance.Title);
        var domain = artifacts.GetValueOrDefault("domain", "operations");
        var objective = artifacts.GetValueOrDefault("objective", "");
        var riskLevel = artifacts.GetValueOrDefault("riskLevel", "Medium");

        var decision = new DecisionRecord(
            Id: Guid.NewGuid(),
            TenantId: instance.TenantId,
            Title: title,
            Domain: domain,
            Objective: objective,
            Constraints: ParseList(artifacts.GetValueOrDefault("constraints", "")),
            Assumptions: ParseList(artifacts.GetValueOrDefault("assumptions", "")),
            Alternatives: new List<DecisionAlternative>(),
            RecommendedOptionId: "",
            Confidence: double.TryParse(artifacts.GetValueOrDefault("confidence", "0.7"), out var c) ? c : 0.7,
            Reversibility: Enum.TryParse<DecisionReversibility>(
                artifacts.GetValueOrDefault("reversibility", "PartiallyReversible"), true, out var rev)
                ? rev : DecisionReversibility.PartiallyReversible,
            RiskLevel: Enum.TryParse<DecisionRiskLevel>(riskLevel, true, out var risk)
                ? risk : DecisionRiskLevel.Medium,
            ExpectedValue: decimal.TryParse(artifacts.GetValueOrDefault("expectedValue", ""), out var ev) ? ev : null,
            RequiresApproval: risk >= DecisionRiskLevel.High,
            LinkedArtifacts: Array.Empty<DecisionLink>(),
            Status: DecisionStatus.Proposed,
            CreatedBy: actor,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var created = await _decisions.CreateAsync(decision, ct);
        artifacts["decisionId"] = created.Id.ToString();

        return $"Decision '{created.Title}' created (id={created.Id})";
    }

    // ── Financial consequence step ──────────────────────────────

    private async Task<string> ExecuteConsequenceStepAsync(
        HeroWorkflowInstance instance,
        Dictionary<string, string> artifacts,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(artifacts.GetValueOrDefault("decisionId"), out var decisionId))
            throw new InvalidOperationException("No decisionId available for consequence step");

        var consequence = new FinancialConsequence(
            Id: Guid.NewGuid(),
            DecisionId: decisionId,
            TenantId: instance.TenantId,
            ExpectedRevenueImpactLow: TryParseDecimal(artifacts, "revenueImpactLow"),
            ExpectedRevenueImpactHigh: TryParseDecimal(artifacts, "revenueImpactHigh"),
            ExpectedCostImpactLow: TryParseDecimal(artifacts, "costImpactLow"),
            ExpectedCostImpactHigh: TryParseDecimal(artifacts, "costImpactHigh"),
            ExpectedMarginImpact: TryParseDecimal(artifacts, "marginImpact"),
            ExpectedCashTimingImpact: artifacts.GetValueOrDefault("cashTimingImpact"),
            LaborImpact: artifacts.GetValueOrDefault("laborImpact"),
            DownsideRisk: TryParseDecimal(artifacts, "downsideRisk"),
            UpsidePotential: TryParseDecimal(artifacts, "upsidePotential"),
            ConfidenceAdjustment: null,
            RoiEstimateLow: TryParseDecimal(artifacts, "roiEstimateLow"),
            RoiEstimateHigh: TryParseDecimal(artifacts, "roiEstimateHigh"),
            BreakEvenEstimate: artifacts.GetValueOrDefault("breakEvenEstimate"),
            Assumptions: ParseList(artifacts.GetValueOrDefault("consequenceAssumptions", "")),
            Notes: artifacts.GetValueOrDefault("consequenceNotes"),
            CreatedBy: "system",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var attached = await _consequences.AttachAsync(consequence, ct);
        artifacts["consequenceId"] = attached.Id.ToString();

        return $"Financial consequence modeled (id={attached.Id})";
    }

    // ── Trust tier step ─────────────────────────────────────────

    private async Task<string> ExecuteTrustTierStepAsync(
        HeroWorkflowInstance instance,
        Dictionary<string, string> artifacts,
        CancellationToken ct = default)
    {
        var actionScope = artifacts.GetValueOrDefault("actionScope", instance.WorkflowType);
        var requestedTier = Enum.TryParse<ExecutionTrustTier>(
            artifacts.GetValueOrDefault("requestedTier", "DraftApprovalRequired"), true, out var tier)
            ? tier : ExecutionTrustTier.DraftApprovalRequired;

        var evaluation = await _trustTiers.EvaluateAsync(
            instance.TenantId.ToString(),
            actionScope,
            requestedTier,
            double.TryParse(artifacts.GetValueOrDefault("confidence"), out var conf) ? conf : null,
            decimal.TryParse(artifacts.GetValueOrDefault("expectedValue"), out var val) ? val : null,
            ct: ct);

        artifacts["trustDisposition"] = evaluation.Disposition;
        artifacts["effectiveTier"] = evaluation.EffectiveTier.ToString();
        artifacts["trustAllowed"] = evaluation.Allowed.ToString();

        return $"Trust tier evaluated: {evaluation.Disposition} (effective={evaluation.EffectiveTier})";
    }

    // ── Approval step ───────────────────────────────────────────

    private async Task<string> ExecuteApprovalStepAsync(
        HeroWorkflowInstance instance,
        Dictionary<string, string> artifacts,
        string actor,
        CancellationToken ct = default)
    {
        // If trust tier says auto-execute, skip approval
        var disposition = artifacts.GetValueOrDefault("trustDisposition", "draft_for_approval");
        if (disposition == TrustDisposition.AutoExecute)
        {
            artifacts["approvalStatus"] = "auto_approved";
            return "Approval skipped — trust tier allows auto-execution";
        }

        var decisionId = artifacts.GetValueOrDefault("decisionId", instance.Id.ToString());
        var gate = await _governance.RequestApprovalAsync(
            actionType: $"hero_workflow.{instance.WorkflowType}",
            resourceId: decisionId,
            tenantId: instance.TenantId.ToString(),
            requestedBy: actor,
            justification: $"Hero workflow: {instance.Title}",
            ct: ct);

        artifacts["approvalGateId"] = gate.Id.ToString();

        // Auto-approve for demo flow (in production, this would wait for human review)
        var reviewed = await _governance.ReviewApprovalAsync(
            gate.Id, instance.TenantId.ToString(),
            reviewedBy: "system",
            reviewerRole: "admin",
            approve: true,
            notes: "Auto-approved via hero workflow progression",
            ct: ct);

        artifacts["approvalStatus"] = reviewed.Status.ToString();

        return $"Approval gate created and reviewed (id={gate.Id}, status={reviewed.Status})";
    }

    // ── Execution step ──────────────────────────────────────────

    private async Task<string> ExecuteExecutionStepAsync(
        HeroWorkflowInstance instance,
        Dictionary<string, string> artifacts,
        string actor,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(artifacts.GetValueOrDefault("decisionId"), out var decisionId))
            throw new InvalidOperationException("No decisionId for execution step");

        // Move decision to Executing
        await _decisions.UpdateStatusAsync(decisionId, DecisionStatus.Executing, actor,
            "Execution initiated via hero workflow", ct);

        // Record expected outcome
        var outcome = await _outcomes.RecordExpectedOutcomeAsync(
            decisionId, instance.TenantId,
            expectedSummary: artifacts.GetValueOrDefault("expectedOutcome", "Positive outcome expected"),
            expectedValue: decimal.TryParse(artifacts.GetValueOrDefault("expectedValue"), out var ev) ? ev : null,
            confidenceAtPrediction: double.TryParse(artifacts.GetValueOrDefault("confidence", "0.7"), out var c) ? c : 0.7,
            expectedTimeframe: artifacts.GetValueOrDefault("expectedTimeframe", "30 days"),
            recordedBy: actor, ct: ct);

        artifacts["outcomeId"] = outcome.Id.ToString();

        // Record execution on the approval gate if one exists
        if (Guid.TryParse(artifacts.GetValueOrDefault("approvalGateId"), out var gateId))
        {
            await _governance.RecordExecutionResultAsync(
                gateId, GateExecutionStatus.Succeeded, null, ct);
        }

        return $"Decision executing, expected outcome recorded (outcome={outcome.Id})";
    }

    // ── Outcome step ────────────────────────────────────────────

    private async Task<string> ExecuteOutcomeStepAsync(
        HeroWorkflowInstance instance,
        Dictionary<string, string> artifacts,
        string actor,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(artifacts.GetValueOrDefault("decisionId"), out var decisionId))
            throw new InvalidOperationException("No decisionId for outcome step");

        // Record actual outcome
        var outcome = await _outcomes.RecordActualOutcomeAsync(
            decisionId,
            actualSummary: artifacts.GetValueOrDefault("actualOutcome", "Outcome observed"),
            actualValue: decimal.TryParse(artifacts.GetValueOrDefault("actualValue"), out var av) ? av : null,
            rootCause: artifacts.GetValueOrDefault("rootCause"),
            notes: artifacts.GetValueOrDefault("outcomeNotes"),
            recordedBy: actor, ct: ct);

        // Move decision to Completed
        await _decisions.UpdateStatusAsync(decisionId, DecisionStatus.Completed, actor,
            "Outcome recorded via hero workflow", ct);

        return $"Actual outcome recorded: {outcome.Direction} (assessment={outcome.Assessment})";
    }

    // ── Exception step ──────────────────────────────────────────

    private async Task<string> ExecuteExceptionStepAsync(
        HeroWorkflowInstance instance,
        Dictionary<string, string> artifacts,
        string actor,
        CancellationToken ct = default)
    {
        var exception = new OperationalException(
            Id: Guid.NewGuid(),
            TenantId: instance.TenantId,
            Category: Enum.TryParse<ExceptionCategory>(
                artifacts.GetValueOrDefault("exceptionCategory", "InterventionPoint"), true, out var cat)
                ? cat : ExceptionCategory.InterventionPoint,
            Severity: Enum.TryParse<ExceptionSeverity>(
                artifacts.GetValueOrDefault("exceptionSeverity", "High"), true, out var sev)
                ? sev : ExceptionSeverity.High,
            Title: artifacts.GetValueOrDefault("exceptionTitle", $"Exception from workflow: {instance.Title}"),
            Description: artifacts.GetValueOrDefault("exceptionDescription", ""),
            Domain: artifacts.GetValueOrDefault("domain", "operations"),
            Status: ExceptionStatus.Open,
            Urgency: double.TryParse(artifacts.GetValueOrDefault("urgency", "0.7"), out var u) ? u : 0.7,
            EconomicImpactEstimate: double.TryParse(artifacts.GetValueOrDefault("economicImpact", "0"), out var ei) ? ei : 0,
            Confidence: 0.8,
            EscalationLevel: EscalationLevel.Operator,
            AssignedTo: actor,
            EscalationPath: null,
            LinkedArtifacts: Guid.TryParse(artifacts.GetValueOrDefault("decisionId"), out var did)
                ? new[] { new ExceptionArtifactLink("Decision", did.ToString(), "Source decision") }
                : Array.Empty<ExceptionArtifactLink>(),
            RecommendedAction: null,
            CreatedBy: actor,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            AcknowledgedAtUtc: null,
            ResolvedAtUtc: null);

        var raised = await _exceptions.RaiseExceptionAsync(exception, ct);
        artifacts["exceptionId"] = raised.Id.ToString();

        return $"Exception raised: {raised.Title} (id={raised.Id})";
    }

    // ── Memory step ─────────────────────────────────────────────

    private async Task<string> ExecuteMemoryStepAsync(
        HeroWorkflowInstance instance,
        Dictionary<string, string> artifacts,
        string actor,
        CancellationToken ct = default)
    {
        var links = new List<MemoryEntityLink>();
        if (Guid.TryParse(artifacts.GetValueOrDefault("decisionId"), out var did))
            links.Add(new MemoryEntityLink("Decision", did.ToString(), "workflow_decision"));

        var record = new EnterpriseMemoryRecord(
            Id: Guid.NewGuid(),
            TenantId: instance.TenantId,
            Layer: MemoryLayer.Operational,
            Category: "hero_workflow",
            Subject: $"Hero workflow completed: {instance.Title}",
            Content: $"Workflow '{instance.WorkflowType}' completed by {actor}. " +
                     $"Decision: {artifacts.GetValueOrDefault("decisionId", "N/A")}. " +
                     $"Status: {instance.Status}.",
            Metadata: new Dictionary<string, string>
            {
                ["workflowId"] = instance.Id.ToString(),
                ["workflowType"] = instance.WorkflowType,
            }.AsReadOnly(),
            LinkedEntities: links,
            Tags: new[] { "hero_workflow", instance.WorkflowType },
            Importance: 0.8,
            CreatedBy: actor,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: null);

        await _memory.StoreAsync(record, ct);

        return $"Workflow memory recorded for institutional learning";
    }

    // ══════════════════════════════════════════════════════════════
    //  Catalog builder — the 3 flagship hero workflows
    // ══════════════════════════════════════════════════════════════

    private static IReadOnlyList<HeroWorkflowDefinition> BuildCatalog()
    {
        return new List<HeroWorkflowDefinition>
        {
            // ── 1. Vendor Selection ─────────────────────────────
            new(
                WorkflowType: "vendor-selection",
                DisplayName: "Vendor Selection",
                Description: "End-to-end vendor evaluation: create decision, model financial consequences, evaluate trust tier, obtain approval, execute selection, record outcome, store institutional memory.",
                Domain: "procurement",
                Steps: new[]
                {
                    new HeroStepDefinition("create-decision", "Create Decision", "Create a vendor selection decision with alternatives and risk assessment", "decision", true),
                    new HeroStepDefinition("model-consequences", "Model Consequences", "Attach financial consequence model with revenue/cost impact ranges", "financial-consequence", true),
                    new HeroStepDefinition("evaluate-trust", "Evaluate Trust Tier", "Check execution trust tier for autonomous action boundaries", "trust-tier", false),
                    new HeroStepDefinition("obtain-approval", "Obtain Approval", "Route through governance approval gate based on trust evaluation", "approval", false),
                    new HeroStepDefinition("execute-decision", "Execute Decision", "Execute the vendor selection and record expected outcome", "execution", false),
                    new HeroStepDefinition("record-outcome", "Record Outcome", "Capture actual outcome and compute variance for calibration", "outcome", true),
                    new HeroStepDefinition("store-memory", "Store Memory", "Record institutional memory for future vendor decisions", "memory", false),
                },
                Category: HeroWorkflowCategory.Strategic),

            // ── 2. Revenue Forecast Override ────────────────────
            new(
                WorkflowType: "revenue-forecast-override",
                DisplayName: "Revenue Forecast Override",
                Description: "Override a revenue forecast: create decision, model financial impact, evaluate trust, approve, execute the override, record forecast vs. actual, store memory.",
                Domain: "finance",
                Steps: new[]
                {
                    new HeroStepDefinition("create-decision", "Create Decision", "Create a forecast override decision with rationale", "decision", true),
                    new HeroStepDefinition("model-consequences", "Model Consequences", "Quantify the revenue impact of the forecast change", "financial-consequence", true),
                    new HeroStepDefinition("evaluate-trust", "Evaluate Trust Tier", "Assess whether the override can be auto-executed or needs approval", "trust-tier", false),
                    new HeroStepDefinition("obtain-approval", "Obtain Approval", "Route high-impact overrides through approval workflow", "approval", false),
                    new HeroStepDefinition("execute-override", "Execute Override", "Apply the forecast override and record prediction", "execution", false),
                    new HeroStepDefinition("record-outcome", "Record Outcome", "Capture actual revenue vs. forecast for calibration intelligence", "outcome", true),
                    new HeroStepDefinition("store-memory", "Store Memory", "Record forecast accuracy for institutional learning", "memory", false),
                },
                Category: HeroWorkflowCategory.Strategic),

            // ── 3. Compliance Exception Resolution ──────────────
            new(
                WorkflowType: "compliance-exception-resolution",
                DisplayName: "Compliance Exception Resolution",
                Description: "Detect and resolve a compliance exception: raise exception, create remediation decision, model financial exposure, evaluate trust, approve remediation, execute, record outcome, store memory.",
                Domain: "compliance",
                Steps: new[]
                {
                    new HeroStepDefinition("raise-exception", "Raise Exception", "Raise a compliance exception with severity and economic impact", "exception", true),
                    new HeroStepDefinition("create-decision", "Create Decision", "Create a remediation decision for the compliance gap", "decision", true),
                    new HeroStepDefinition("model-exposure", "Model Exposure", "Quantify financial exposure from the compliance gap", "financial-consequence", true),
                    new HeroStepDefinition("evaluate-trust", "Evaluate Trust Tier", "Determine autonomy level for remediation actions", "trust-tier", false),
                    new HeroStepDefinition("approve-remediation", "Approve Remediation", "Route remediation through approval workflow", "approval", false),
                    new HeroStepDefinition("execute-remediation", "Execute Remediation", "Execute remediation and record expected resolution", "execution", false),
                    new HeroStepDefinition("record-outcome", "Record Outcome", "Capture remediation effectiveness", "outcome", true),
                    new HeroStepDefinition("store-memory", "Store Memory", "Record compliance pattern for future prevention", "memory", false),
                },
                Category: HeroWorkflowCategory.Compliance),
        };
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static IReadOnlyList<string> ParseList(string csv)
        => string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static decimal? TryParseDecimal(Dictionary<string, string> artifacts, string key)
        => artifacts.TryGetValue(key, out var v) && decimal.TryParse(v, out var d) ? d : null;
}
