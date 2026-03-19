using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.ExceptionIntelligence;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.HeroWorkflow;
using ArchonAI.Core.Models.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresHeroWorkflowStore : IHeroWorkflowService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresHeroWorkflowStore> _logger;
    private readonly IDecisionService _decisionService;
    private readonly IFinancialConsequenceService _financialConsequenceService;
    private readonly ITrustTierService _trustTierService;
    private readonly IGovernanceService _governanceService;
    private readonly IOutcomeLearningService _outcomeLearningService;
    private readonly IExceptionIntelligenceService _exceptionIntelligenceService;
    private readonly IEnterpriseMemoryService _enterpriseMemoryService;
    private readonly IEventBus _eventBus;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PostgresHeroWorkflowStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresHeroWorkflowStore> logger,
        IDecisionService decisionService,
        IFinancialConsequenceService financialConsequenceService,
        ITrustTierService trustTierService,
        IGovernanceService governanceService,
        IOutcomeLearningService outcomeLearningService,
        IExceptionIntelligenceService exceptionIntelligenceService,
        IEnterpriseMemoryService enterpriseMemoryService,
        IEventBus eventBus)
    {
        _connectionString = options.Value.ConnectionStringHardened
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
        _decisionService = decisionService;
        _financialConsequenceService = financialConsequenceService;
        _trustTierService = trustTierService;
        _governanceService = governanceService;
        _outcomeLearningService = outcomeLearningService;
        _exceptionIntelligenceService = exceptionIntelligenceService;
        _enterpriseMemoryService = enterpriseMemoryService;
        _eventBus = eventBus;
    }

    private string InstancesTable => $"{_schema}.hero_workflow_instances";

    // ── Static Catalog ──────────────────────────────────────────

    private static readonly IReadOnlyList<HeroWorkflowDefinition> Catalog = BuildCatalog();

    private static IReadOnlyList<HeroWorkflowDefinition> BuildCatalog()
    {
        return new List<HeroWorkflowDefinition>
        {
            new("vendor-selection",
                "Vendor Selection",
                "End-to-end governed vendor evaluation, approval, and onboarding.",
                "procurement",
                new List<HeroStepDefinition>
                {
                    new("vs-decision", "Create Vendor Decision", "Formalize vendor selection as a decision record.", "decision", true),
                    new("vs-financial", "Attach Financial Consequences", "Model revenue/cost impact of vendor choice.", "financial-consequence", true),
                    new("vs-trust", "Evaluate Trust Tier", "Determine execution autonomy for this action.", "trust-tier", false),
                    new("vs-approval", "Request Approval", "Route to governance for human approval.", "approval", false),
                    new("vs-execution", "Record Expected Outcome", "Capture what success looks like.", "execution", true),
                    new("vs-outcome", "Record Actual Outcome", "Capture post-execution results.", "outcome", true),
                    new("vs-memory", "Store in Enterprise Memory", "Persist learnings for future vendor decisions.", "memory", false),
                },
                HeroWorkflowCategory.Strategic),

            new("revenue-forecast-override",
                "Revenue Forecast Override",
                "Override an AI-generated revenue forecast with human judgment, governed end-to-end.",
                "finance",
                new List<HeroStepDefinition>
                {
                    new("rfo-decision", "Create Override Decision", "Formalize the override rationale.", "decision", true),
                    new("rfo-financial", "Attach Financial Consequences", "Model economic impact of the override.", "financial-consequence", true),
                    new("rfo-trust", "Evaluate Trust Tier", "Assess autonomy level for forecast changes.", "trust-tier", false),
                    new("rfo-approval", "Request Approval", "Route override to finance leadership.", "approval", false),
                    new("rfo-execution", "Record Expected Outcome", "Set target metrics for the override.", "execution", true),
                    new("rfo-outcome", "Record Actual Outcome", "Capture actual revenue vs override.", "outcome", true),
                    new("rfo-memory", "Store in Enterprise Memory", "Persist override learnings.", "memory", false),
                },
                HeroWorkflowCategory.Strategic),

            new("compliance-exception-resolution",
                "Compliance Exception Resolution",
                "Raise, triage, and resolve a compliance exception with full audit trail.",
                "compliance",
                new List<HeroStepDefinition>
                {
                    new("cer-exception", "Raise Exception", "Create a structured operational exception.", "exception", true),
                    new("cer-decision", "Create Resolution Decision", "Formalize the resolution approach.", "decision", true),
                    new("cer-trust", "Evaluate Trust Tier", "Determine execution permissions.", "trust-tier", false),
                    new("cer-approval", "Request Approval", "Route to compliance officer.", "approval", false),
                    new("cer-execution", "Record Expected Outcome", "Define resolution success criteria.", "execution", true),
                    new("cer-outcome", "Record Actual Outcome", "Capture resolution results.", "outcome", true),
                    new("cer-memory", "Store in Enterprise Memory", "Persist compliance learnings.", "memory", false),
                },
                HeroWorkflowCategory.Compliance),
        };
    }

    // ── Initialization ──────────────────────────────────────────

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                CREATE SCHEMA IF NOT EXISTS {_schema};

                CREATE TABLE IF NOT EXISTS {InstancesTable} (
                    id              uuid PRIMARY KEY,
                    tenant_id       uuid NOT NULL,
                    workflow_type   text NOT NULL,
                    title           text NOT NULL,
                    status          int NOT NULL,
                    steps           jsonb NOT NULL DEFAULT '[]',
                    artifacts       jsonb NOT NULL DEFAULT '{{}}',
                    initiated_by    text NOT NULL,
                    created_at_utc  timestamptz NOT NULL,
                    updated_at_utc  timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_hero_wf_tenant_id     ON {InstancesTable} (tenant_id);
                CREATE INDEX IF NOT EXISTS idx_hero_wf_workflow_type  ON {InstancesTable} (workflow_type);
                CREATE INDEX IF NOT EXISTS idx_hero_wf_status         ON {InstancesTable} (status);
                CREATE INDEX IF NOT EXISTS idx_hero_wf_created_at     ON {InstancesTable} (created_at_utc DESC);
            ";
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
            _logger.LogInformation("PostgresHeroWorkflowStore initialized (schema={Schema}).", _schema);
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ── Catalog (in-memory, static) ─────────────────────────────

    public Task<IReadOnlyList<HeroWorkflowDefinition>> GetCatalogAsync(CancellationToken ct = default)
        => Task.FromResult(Catalog);

    public Task<HeroWorkflowDefinition?> GetDefinitionAsync(string workflowType, CancellationToken ct = default)
        => Task.FromResult(Catalog.FirstOrDefault(d => d.WorkflowType == workflowType));

    // ── StartAsync ──────────────────────────────────────────────

    public async Task<HeroWorkflowInstance> StartAsync(
        Guid tenantId, string workflowType, string title,
        IReadOnlyDictionary<string, string> initialInputs,
        string initiatedBy, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var definition = Catalog.FirstOrDefault(d => d.WorkflowType == workflowType)
            ?? throw new InvalidOperationException($"Unknown workflow type '{workflowType}'.");

        var now = DateTimeOffset.UtcNow;
        var instanceId = Guid.NewGuid();

        var steps = definition.Steps.Select(s => new HeroStepState(
            s.StepId, HeroStepStatus.Pending, null, null)).ToList();

        var artifacts = new Dictionary<string, string>(initialInputs);

        // Auto-execute the first step
        var firstStepDef = definition.Steps[0];
        string? detail;
        try
        {
            detail = await ExecuteStepAsync(tenantId, instanceId, firstStepDef, artifacts, initiatedBy, ct);
            steps[0] = new HeroStepState(firstStepDef.StepId, HeroStepStatus.Completed, detail, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            detail = $"Step failed: {ex.Message}";
            steps[0] = new HeroStepState(firstStepDef.StepId, HeroStepStatus.Failed, detail, DateTimeOffset.UtcNow);
        }

        var status = steps[0].Status == HeroStepStatus.Failed
            ? HeroWorkflowStatus.Failed
            : HeroWorkflowStatus.InProgress;

        var instance = new HeroWorkflowInstance(
            instanceId, tenantId, workflowType, title, status,
            steps, artifacts, initiatedBy, now, DateTimeOffset.UtcNow);

        await InsertInstanceAsync(instance, ct);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(), "HeroWorkflow.Started", nameof(PostgresHeroWorkflowStore),
            instanceId,
            new Dictionary<string, string>
            {
                ["workflowId"] = instanceId.ToString(),
                ["workflowType"] = workflowType,
                ["tenantId"] = tenantId.ToString(),
            },
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Hero workflow {WorkflowId} ({Type}) started.", instanceId, workflowType);
        return instance;
    }

    // ── AdvanceAsync ────────────────────────────────────────────

    public async Task<HeroWorkflowInstance?> AdvanceAsync(
        Guid workflowId, Guid tenantId,
        IReadOnlyDictionary<string, string>? stepInputs,
        string actor, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var instance = await GetAsync(workflowId, tenantId, ct);
        if (instance is null) return null;

        if (instance.Status is HeroWorkflowStatus.Completed
            or HeroWorkflowStatus.Failed
            or HeroWorkflowStatus.Cancelled)
            return instance;

        var definition = Catalog.FirstOrDefault(d => d.WorkflowType == instance.WorkflowType);
        if (definition is null) return instance;

        // Find the next pending step
        var nextIndex = instance.Steps.ToList().FindIndex(s => s.Status == HeroStepStatus.Pending);
        if (nextIndex < 0)
        {
            // All steps done
            var completed = instance with
            {
                Status = HeroWorkflowStatus.Completed,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            await UpdateInstanceAsync(completed, ct);
            return completed;
        }

        var stepDef = definition.Steps[nextIndex];
        var artifacts = new Dictionary<string, string>(instance.Artifacts);
        if (stepInputs is not null)
        {
            foreach (var kv in stepInputs)
                artifacts[kv.Key] = kv.Value;
        }

        var steps = instance.Steps.ToList();
        string? detail;
        try
        {
            detail = await ExecuteStepAsync(tenantId, workflowId, stepDef, artifacts, actor, ct);
            steps[nextIndex] = new HeroStepState(stepDef.StepId, HeroStepStatus.Completed, detail, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            detail = $"Step failed: {ex.Message}";
            steps[nextIndex] = new HeroStepState(stepDef.StepId, HeroStepStatus.Failed, detail, DateTimeOffset.UtcNow);
        }

        var allDone = steps.All(s => s.Status is HeroStepStatus.Completed or HeroStepStatus.Skipped);
        var anyFailed = steps.Any(s => s.Status == HeroStepStatus.Failed);

        var newStatus = anyFailed
            ? HeroWorkflowStatus.Failed
            : allDone
                ? HeroWorkflowStatus.Completed
                : stepDef.Subsystem == "approval"
                    ? HeroWorkflowStatus.AwaitingApproval
                    : HeroWorkflowStatus.InProgress;

        var updated = instance with
        {
            Status = newStatus,
            Steps = steps,
            Artifacts = artifacts,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        await UpdateInstanceAsync(updated, ct);

        _logger.LogInformation("Hero workflow {WorkflowId} advanced to step {StepId}.", workflowId, stepDef.StepId);
        return updated;
    }

    // ── GetAsync ────────────────────────────────────────────────

    public async Task<HeroWorkflowInstance?> GetAsync(
        Guid workflowId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {InstancesTable} WHERE id = @id AND tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("id", workflowId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapInstance(reader) : null;
    }

    // ── ListAsync ───────────────────────────────────────────────

    public async Task<IReadOnlyList<HeroWorkflowSummary>> ListAsync(
        Guid tenantId, string? workflowType = null,
        HeroWorkflowStatus? status = null,
        int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM {InstancesTable} WHERE tenant_id = @tenantId";
        if (workflowType is not null) sql += " AND workflow_type = @workflowType";
        if (status.HasValue) sql += " AND status = @status";
        sql += " ORDER BY created_at_utc DESC LIMIT @limit";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        if (workflowType is not null) cmd.Parameters.AddWithValue("workflowType", workflowType);
        if (status.HasValue) cmd.Parameters.AddWithValue("status", (int)status.Value);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<HeroWorkflowSummary>();
        while (await reader.ReadAsync(ct))
        {
            var inst = MapInstance(reader);
            var completedSteps = inst.Steps.Count(s => s.Status is HeroStepStatus.Completed or HeroStepStatus.Skipped);
            results.Add(new HeroWorkflowSummary(
                inst.Id, inst.WorkflowType, inst.Title, inst.Status,
                completedSteps, inst.Steps.Count, inst.InitiatedBy,
                inst.CreatedAtUtc, inst.UpdatedAtUtc));
        }
        return results;
    }

    // ── CancelAsync ─────────────────────────────────────────────

    public async Task<HeroWorkflowInstance?> CancelAsync(
        Guid workflowId, Guid tenantId, string actor,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var instance = await GetAsync(workflowId, tenantId, ct);
        if (instance is null) return null;

        if (instance.Status is HeroWorkflowStatus.Completed
            or HeroWorkflowStatus.Cancelled)
            return instance;

        var cancelled = instance with
        {
            Status = HeroWorkflowStatus.Cancelled,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        await UpdateInstanceAsync(cancelled, ct);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(), "HeroWorkflow.Cancelled", nameof(PostgresHeroWorkflowStore),
            workflowId,
            new Dictionary<string, string>
            {
                ["workflowId"] = workflowId.ToString(),
                ["actor"] = actor,
            },
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Hero workflow {WorkflowId} cancelled by {Actor}.", workflowId, actor);
        return cancelled;
    }

    // ── Step Execution Engine ───────────────────────────────────

    private async Task<string?> ExecuteStepAsync(
        Guid tenantId, Guid workflowId, HeroStepDefinition stepDef,
        Dictionary<string, string> artifacts, string actor,
        CancellationToken ct)
    {
        switch (stepDef.Subsystem)
        {
            case "decision":
            {
                var decision = await _decisionService.CreateAsync(new DecisionRecord(
                    Guid.NewGuid(), tenantId,
                    artifacts.GetValueOrDefault("title", "Workflow Decision"),
                    artifacts.GetValueOrDefault("domain", "general"),
                    artifacts.GetValueOrDefault("objective", "Workflow-generated decision"),
                    new List<string>(), new List<string>(),
                    new List<DecisionAlternative>(),
                    "default",
                    double.TryParse(artifacts.GetValueOrDefault("confidence", "0.8"), out var conf) ? conf : 0.8,
                    DecisionReversibility.PartiallyReversible,
                    DecisionRiskLevel.Medium,
                    decimal.TryParse(artifacts.GetValueOrDefault("expectedValue"), out var ev) ? ev : null,
                    false,
                    new List<DecisionLink>(),
                    DecisionStatus.Proposed,
                    actor,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow), ct);
                artifacts["decisionId"] = decision.Id.ToString();
                return $"Decision {decision.Id} created.";
            }

            case "financial-consequence":
            {
                if (!Guid.TryParse(artifacts.GetValueOrDefault("decisionId"), out var decId))
                    return "Skipped: no decisionId available.";
                var fc = await _financialConsequenceService.AttachAsync(new FinancialConsequence(
                    Guid.NewGuid(), decId, tenantId,
                    decimal.TryParse(artifacts.GetValueOrDefault("revenueImpactLow"), out var rl) ? rl : null,
                    decimal.TryParse(artifacts.GetValueOrDefault("revenueImpactHigh"), out var rh) ? rh : null,
                    decimal.TryParse(artifacts.GetValueOrDefault("costImpactLow"), out var cl) ? cl : null,
                    decimal.TryParse(artifacts.GetValueOrDefault("costImpactHigh"), out var ch) ? ch : null,
                    null, null, null,
                    decimal.TryParse(artifacts.GetValueOrDefault("downsideRisk"), out var dr) ? dr : null,
                    decimal.TryParse(artifacts.GetValueOrDefault("upsidePotential"), out var up) ? up : null,
                    null, null, null, null,
                    new List<string>(), null,
                    actor, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), ct);
                artifacts["financialConsequenceId"] = fc.Id.ToString();
                return $"Financial consequence {fc.Id} attached.";
            }

            case "trust-tier":
            {
                var eval = await _trustTierService.EvaluateAsync(
                    tenantId.ToString(),
                    artifacts.GetValueOrDefault("actionScope", artifacts.GetValueOrDefault("domain", "general")),
                    ExecutionTrustTier.DraftApprovalRequired,
                    double.TryParse(artifacts.GetValueOrDefault("confidence"), out var c) ? c : null,
                    decimal.TryParse(artifacts.GetValueOrDefault("expectedValue"), out var v) ? v : null,
                    null, ct);
                artifacts["trustTier"] = eval.EffectiveTier.ToString();
                artifacts["trustDisposition"] = eval.Disposition;
                return $"Trust tier: {eval.EffectiveTier} ({eval.Disposition}).";
            }

            case "approval":
            {
                var gate = await _governanceService.RequestApprovalAsync(
                    artifacts.GetValueOrDefault("actionType", "workflow.execute"),
                    workflowId.ToString(),
                    tenantId.ToString(),
                    actor,
                    artifacts.GetValueOrDefault("justification", "Hero workflow approval request."),
                    null, ct);
                artifacts["approvalGateId"] = gate.Id.ToString();
                return $"Approval gate {gate.Id} created (status: {gate.Status}).";
            }

            case "execution":
            {
                if (!Guid.TryParse(artifacts.GetValueOrDefault("decisionId"), out var decId2))
                    return "Skipped: no decisionId available.";
                var outcome = await _outcomeLearningService.RecordExpectedOutcomeAsync(
                    decId2, tenantId,
                    artifacts.GetValueOrDefault("expectedSummary"),
                    decimal.TryParse(artifacts.GetValueOrDefault("expectedValue"), out var ev2) ? ev2 : null,
                    double.TryParse(artifacts.GetValueOrDefault("confidence", "0.8"), out var c2) ? c2 : 0.8,
                    artifacts.GetValueOrDefault("expectedTimeframe"),
                    actor, ct);
                artifacts["outcomeRecordId"] = outcome.Id.ToString();
                return $"Expected outcome recorded ({outcome.Id}).";
            }

            case "outcome":
            {
                if (!Guid.TryParse(artifacts.GetValueOrDefault("decisionId"), out var decId3))
                    return "Skipped: no decisionId available.";
                var actual = await _outcomeLearningService.RecordActualOutcomeAsync(
                    decId3,
                    artifacts.GetValueOrDefault("actualSummary"),
                    decimal.TryParse(artifacts.GetValueOrDefault("actualValue"), out var av) ? av : null,
                    artifacts.GetValueOrDefault("rootCause"),
                    artifacts.GetValueOrDefault("notes"),
                    actor, ct);
                return $"Actual outcome recorded (direction: {actual.Direction}).";
            }

            case "exception":
            {
                var exc = await _exceptionIntelligenceService.RaiseExceptionAsync(new OperationalException(
                    Guid.NewGuid(), tenantId,
                    ExceptionCategory.InterventionPoint,
                    ExceptionSeverity.High,
                    artifacts.GetValueOrDefault("exceptionTitle", "Workflow Exception"),
                    artifacts.GetValueOrDefault("exceptionDescription", "Exception raised during hero workflow."),
                    artifacts.GetValueOrDefault("domain", "general"),
                    ExceptionStatus.Open,
                    0.8, 0.0, 0.7,
                    EscalationLevel.Operator,
                    null, null,
                    new List<ExceptionArtifactLink>
                    {
                        new("workflow", workflowId.ToString(), "Source workflow"),
                    },
                    null,
                    actor,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    null, null), ct);
                artifacts["exceptionId"] = exc.Id.ToString();
                return $"Exception {exc.Id} raised.";
            }

            case "memory":
            {
                var mem = await _enterpriseMemoryService.StoreAsync(new EnterpriseMemoryRecord(
                    Guid.NewGuid(), tenantId,
                    MemoryLayer.Operational,
                    "hero-workflow",
                    artifacts.GetValueOrDefault("title", "Workflow Memory"),
                    artifacts.GetValueOrDefault("memorySummary", $"Hero workflow {workflowId} completed."),
                    new Dictionary<string, string>
                    {
                        ["workflowId"] = workflowId.ToString(),
                        ["workflowType"] = artifacts.GetValueOrDefault("workflowType", "unknown"),
                    },
                    new List<MemoryEntityLink>
                    {
                        new("workflow", workflowId.ToString(), "source"),
                    },
                    new List<string> { "hero-workflow" },
                    0.8,
                    actor,
                    DateTimeOffset.UtcNow,
                    null), ct);
                artifacts["memoryRecordId"] = mem.Id.ToString();
                return $"Memory record {mem.Id} stored.";
            }

            default:
                return $"Unknown subsystem '{stepDef.Subsystem}' — step skipped.";
        }
    }

    // ── DB Helpers ──────────────────────────────────────────────

    private async Task InsertInstanceAsync(HeroWorkflowInstance instance, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {InstancesTable}
                (id, tenant_id, workflow_type, title, status, steps, artifacts, initiated_by, created_at_utc, updated_at_utc)
            VALUES
                (@id, @tenantId, @workflowType, @title, @status, @steps::jsonb, @artifacts::jsonb, @initiatedBy, @createdAtUtc, @updatedAtUtc)
        ", conn);

        cmd.Parameters.AddWithValue("id", instance.Id);
        cmd.Parameters.AddWithValue("tenantId", instance.TenantId);
        cmd.Parameters.AddWithValue("workflowType", instance.WorkflowType);
        cmd.Parameters.AddWithValue("title", instance.Title);
        cmd.Parameters.AddWithValue("status", (int)instance.Status);
        cmd.Parameters.AddWithValue("steps", JsonSerializer.Serialize(instance.Steps, JsonOpts));
        cmd.Parameters.AddWithValue("artifacts", JsonSerializer.Serialize(instance.Artifacts, JsonOpts));
        cmd.Parameters.AddWithValue("initiatedBy", instance.InitiatedBy);
        cmd.Parameters.AddWithValue("createdAtUtc", instance.CreatedAtUtc);
        cmd.Parameters.AddWithValue("updatedAtUtc", instance.UpdatedAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task UpdateInstanceAsync(HeroWorkflowInstance instance, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            UPDATE {InstancesTable}
            SET status = @status, steps = @steps::jsonb, artifacts = @artifacts::jsonb, updated_at_utc = @updatedAtUtc
            WHERE id = @id AND tenant_id = @tenantId
        ", conn);

        cmd.Parameters.AddWithValue("status", (int)instance.Status);
        cmd.Parameters.AddWithValue("steps", JsonSerializer.Serialize(instance.Steps, JsonOpts));
        cmd.Parameters.AddWithValue("artifacts", JsonSerializer.Serialize(instance.Artifacts, JsonOpts));
        cmd.Parameters.AddWithValue("updatedAtUtc", instance.UpdatedAtUtc);
        cmd.Parameters.AddWithValue("id", instance.Id);
        cmd.Parameters.AddWithValue("tenantId", instance.TenantId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Row Mapper ──────────────────────────────────────────────

    private static HeroWorkflowInstance MapInstance(NpgsqlDataReader reader)
    {
        return new HeroWorkflowInstance(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetGuid(reader.GetOrdinal("tenant_id")),
            reader.GetString(reader.GetOrdinal("workflow_type")),
            reader.GetString(reader.GetOrdinal("title")),
            (HeroWorkflowStatus)reader.GetInt32(reader.GetOrdinal("status")),
            JsonSerializer.Deserialize<List<HeroStepState>>(reader.GetString(reader.GetOrdinal("steps")), JsonOpts) ?? new(),
            JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(reader.GetOrdinal("artifacts")), JsonOpts) ?? new(),
            reader.GetString(reader.GetOrdinal("initiated_by")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at_utc")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at_utc")));
    }
}
