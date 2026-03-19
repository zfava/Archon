using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.HeroWorkflow;
using ArchonAI.Core.Models.PolicySimulation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresPolicySimulationStore : IPolicySimulationService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresPolicySimulationStore> _logger;
    private readonly IDecisionService _decisionService;
    private readonly ITrustTierService _trustTierService;
    private readonly IGovernanceService _governanceService;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── Static workflow catalog for simulation previews ──────────
    private static readonly IReadOnlyList<HeroWorkflowDefinition> WorkflowCatalog = new List<HeroWorkflowDefinition>
    {
        new("vendor-selection", "Vendor Selection",
            "End-to-end governed vendor evaluation, approval, and onboarding.", "procurement",
            new List<HeroStepDefinition>
            {
                new("vs-decision", "Create Vendor Decision", "Formalize vendor selection as a decision record.", "decision", true),
                new("vs-financial", "Attach Financial Consequences", "Model revenue/cost impact.", "financial-consequence", true),
                new("vs-trust", "Evaluate Trust Tier", "Determine execution autonomy.", "trust-tier", false),
                new("vs-approval", "Request Approval", "Route to governance.", "approval", false),
                new("vs-execution", "Record Expected Outcome", "Capture success criteria.", "execution", true),
                new("vs-outcome", "Record Actual Outcome", "Capture results.", "outcome", true),
                new("vs-memory", "Store in Enterprise Memory", "Persist learnings.", "memory", false),
            },
            HeroWorkflowCategory.Strategic),

        new("revenue-forecast-override", "Revenue Forecast Override",
            "Override an AI-generated revenue forecast with human judgment.", "finance",
            new List<HeroStepDefinition>
            {
                new("rfo-decision", "Create Override Decision", "Formalize override rationale.", "decision", true),
                new("rfo-financial", "Attach Financial Consequences", "Model economic impact.", "financial-consequence", true),
                new("rfo-trust", "Evaluate Trust Tier", "Assess autonomy level.", "trust-tier", false),
                new("rfo-approval", "Request Approval", "Route to finance leadership.", "approval", false),
                new("rfo-execution", "Record Expected Outcome", "Set target metrics.", "execution", true),
                new("rfo-outcome", "Record Actual Outcome", "Capture actual revenue.", "outcome", true),
                new("rfo-memory", "Store in Enterprise Memory", "Persist override learnings.", "memory", false),
            },
            HeroWorkflowCategory.Strategic),

        new("compliance-exception-resolution", "Compliance Exception Resolution",
            "Raise, triage, and resolve a compliance exception.", "compliance",
            new List<HeroStepDefinition>
            {
                new("cer-exception", "Raise Exception", "Create structured exception.", "exception", true),
                new("cer-decision", "Create Resolution Decision", "Formalize resolution.", "decision", true),
                new("cer-trust", "Evaluate Trust Tier", "Determine permissions.", "trust-tier", false),
                new("cer-approval", "Request Approval", "Route to compliance officer.", "approval", false),
                new("cer-execution", "Record Expected Outcome", "Define success criteria.", "execution", true),
                new("cer-outcome", "Record Actual Outcome", "Capture resolution results.", "outcome", true),
                new("cer-memory", "Store in Enterprise Memory", "Persist compliance learnings.", "memory", false),
            },
            HeroWorkflowCategory.Compliance),
    };

    public PostgresPolicySimulationStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresPolicySimulationStore> logger,
        IDecisionService decisionService,
        ITrustTierService trustTierService,
        IGovernanceService governanceService)
    {
        _connectionString = options.Value.ConnectionString
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
        _decisionService = decisionService;
        _trustTierService = trustTierService;
        _governanceService = governanceService;
    }

    private string SimulationsTable => $"{_schema}.policy_simulations";

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

                CREATE TABLE IF NOT EXISTS {SimulationsTable} (
                    id               uuid PRIMARY KEY,
                    tenant_id        uuid NOT NULL,
                    action_type      text NOT NULL,
                    title            text NOT NULL,
                    verdict          int NOT NULL,
                    simulation_data  jsonb NOT NULL,
                    simulated_by     text NOT NULL,
                    simulated_at_utc timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_policy_sim_tenant_id    ON {SimulationsTable} (tenant_id);
                CREATE INDEX IF NOT EXISTS idx_policy_sim_action_type   ON {SimulationsTable} (action_type);
                CREATE INDEX IF NOT EXISTS idx_policy_sim_simulated_at  ON {SimulationsTable} (simulated_at_utc DESC);
            ";
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
            _logger.LogInformation("PostgresPolicySimulationStore initialized (schema={Schema}).", _schema);
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ── SimulateAsync ───────────────────────────────────────────

    public async Task<SimulationResult> SimulateAsync(
        SimulationRequest request, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var simulationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var reasons = new List<string>();

        // ── Build simulated decision ────────────────────────────
        var riskLevel = Enum.TryParse<DecisionRiskLevel>(request.RiskLevel, true, out var rl)
            ? rl : DecisionRiskLevel.Medium;
        var reversibility = Enum.TryParse<DecisionReversibility>(request.Reversibility, true, out var rv)
            ? rv : DecisionReversibility.PartiallyReversible;
        var confidence = request.Confidence ?? 0.7;
        var expectedValue = request.ExpectedValue;

        var simulatedDecision = new SimulatedDecision(
            request.Title,
            request.Domain ?? "general",
            riskLevel,
            reversibility,
            confidence,
            expectedValue,
            WouldRequireApproval: false,
            ProposedStatus: DecisionStatus.Proposed);

        // ── Evaluate trust tier (read-only) ─────────────────────
        var requestedTier = Enum.TryParse<ExecutionTrustTier>(request.RequestedTier, true, out var rt)
            ? rt : ExecutionTrustTier.DraftApprovalRequired;

        TrustTierEvaluation? trustTierOutcome = null;
        try
        {
            trustTierOutcome = await _trustTierService.EvaluateAsync(
                request.TenantId.ToString(),
                request.ActionScope,
                requestedTier,
                confidence,
                expectedValue,
                reversibility != DecisionReversibility.Irreversible,
                ct);
            reasons.Add($"Trust tier evaluation: {trustTierOutcome.EffectiveTier} ({trustTierOutcome.Disposition}).");
        }
        catch (Exception ex)
        {
            reasons.Add($"Trust tier evaluation failed: {ex.Message}");
        }

        // ── Check approval requirements (read-only) ─────────────
        SimulatedApproval? approvalRequirement = null;
        try
        {
            var requiresApproval = await _governanceService.RequiresApprovalAsync(request.ActionType, ct);
            var policies = await _governanceService.ListApprovalPoliciesAsync(ct);
            var matchedPolicy = policies.FirstOrDefault(p => p.ActionType == request.ActionType);

            approvalRequirement = new SimulatedApproval(
                requiresApproval,
                matchedPolicy?.RequiredApproverRole,
                matchedPolicy?.RequireSeparationOfDuties ?? false,
                matchedPolicy?.ActionType,
                requiresApproval
                    ? $"Action type '{request.ActionType}' requires approval from {matchedPolicy?.RequiredApproverRole ?? "admin"}."
                    : "No approval required for this action type.");

            if (requiresApproval)
                reasons.Add($"Approval required from role '{matchedPolicy?.RequiredApproverRole ?? "admin"}'.");

            simulatedDecision = simulatedDecision with { WouldRequireApproval = requiresApproval };
        }
        catch (Exception ex)
        {
            reasons.Add($"Approval check failed: {ex.Message}");
        }

        // ── Build economic effect model ─────────────────────────
        var netLow = (request.RevenueImpactLow ?? 0) - (request.CostImpactHigh ?? 0);
        var netHigh = (request.RevenueImpactHigh ?? 0) - (request.CostImpactLow ?? 0);
        string? economicSummary = null;
        if (request.RevenueImpactLow.HasValue || request.CostImpactLow.HasValue)
            economicSummary = $"Net impact range: {netLow:C0} to {netHigh:C0}.";

        var economicEffect = new SimulatedEconomicEffect(
            request.RevenueImpactLow, request.RevenueImpactHigh,
            request.CostImpactLow, request.CostImpactHigh,
            netLow, netHigh,
            request.DownsideRisk, request.UpsidePotential,
            economicSummary);

        // ── Policy outcomes ─────────────────────────────────────
        var policyOutcomes = new List<PolicyOutcome>();

        if (riskLevel >= DecisionRiskLevel.High)
        {
            policyOutcomes.Add(new PolicyOutcome("HighRiskPolicy", "risk", false,
                "High/Critical risk actions require additional scrutiny."));
            reasons.Add("Risk level is high or critical — additional governance required.");
        }
        else
        {
            policyOutcomes.Add(new PolicyOutcome("HighRiskPolicy", "risk", true,
                "Risk level within acceptable bounds."));
        }

        if (confidence < 0.5)
        {
            policyOutcomes.Add(new PolicyOutcome("ConfidenceThreshold", "confidence", false,
                "Confidence below minimum threshold (0.5)."));
            reasons.Add("Confidence below minimum threshold.");
        }
        else
        {
            policyOutcomes.Add(new PolicyOutcome("ConfidenceThreshold", "confidence", true,
                $"Confidence {confidence:P0} meets threshold."));
        }

        if (reversibility == DecisionReversibility.Irreversible)
        {
            policyOutcomes.Add(new PolicyOutcome("IrreversibilityPolicy", "safety", false,
                "Irreversible actions require human approval."));
            reasons.Add("Action is irreversible — human approval required.");
        }
        else
        {
            policyOutcomes.Add(new PolicyOutcome("IrreversibilityPolicy", "safety", true,
                "Action is reversible or compensatable."));
        }

        // ── Preview workflow steps ──────────────────────────────
        SimulatedWorkflowPreview? workflowPreview = null;
        if (request.WorkflowType is not null)
        {
            var def = WorkflowCatalog.FirstOrDefault(d => d.WorkflowType == request.WorkflowType);
            if (def is not null)
            {
                var previewSteps = def.Steps.Select(s => new SimulatedWorkflowStep(
                    s.StepId, s.Name, s.Subsystem, "Projected: nominal")).ToList();
                workflowPreview = new SimulatedWorkflowPreview(
                    def.WorkflowType, def.DisplayName, def.Steps.Count, previewSteps);
                reasons.Add($"Workflow '{def.DisplayName}' would execute {def.Steps.Count} steps.");
            }
        }

        // ── Determine verdict ───────────────────────────────────
        var anyBlocked = policyOutcomes.Any(p => !p.Passed && p.PolicyType == "risk"
            && riskLevel == DecisionRiskLevel.Critical);
        var anyRequiresApproval = approvalRequirement?.Required == true
            || policyOutcomes.Any(p => !p.Passed);

        var effectiveTier = trustTierOutcome?.EffectiveTier ?? ExecutionTrustTier.DraftApprovalRequired;

        SimulationVerdict verdict;
        if (anyBlocked || (effectiveTier == ExecutionTrustTier.ObserveOnly && riskLevel == DecisionRiskLevel.Critical))
        {
            verdict = SimulationVerdict.Blocked;
            reasons.Add("Verdict: BLOCKED — critical risk with insufficient trust tier.");
        }
        else if (effectiveTier == ExecutionTrustTier.ObserveOnly)
        {
            verdict = SimulationVerdict.ObserveOnly;
            reasons.Add("Verdict: OBSERVE ONLY — trust tier is observe-only.");
        }
        else if (effectiveTier == ExecutionTrustTier.RecommendOnly)
        {
            verdict = SimulationVerdict.RecommendOnly;
            reasons.Add("Verdict: RECOMMEND ONLY — trust tier limits to recommendation.");
        }
        else if (anyRequiresApproval)
        {
            verdict = SimulationVerdict.RequiresApproval;
            reasons.Add("Verdict: REQUIRES APPROVAL — governance gates apply.");
        }
        else
        {
            verdict = SimulationVerdict.Allowed;
            reasons.Add("Verdict: ALLOWED — all policies passed and trust tier permits execution.");
        }

        var result = new SimulationResult(
            simulationId, request.TenantId, request.ActionType, request.Title,
            verdict, simulatedDecision, trustTierOutcome, approvalRequirement,
            policyOutcomes, economicEffect, workflowPreview,
            reasons, request.RequestedBy, now);

        // ── Persist ─────────────────────────────────────────────
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var insertCmd = new NpgsqlCommand($@"
            INSERT INTO {SimulationsTable}
                (id, tenant_id, action_type, title, verdict, simulation_data, simulated_by, simulated_at_utc)
            VALUES
                (@id, @tenantId, @actionType, @title, @verdict, @simulationData::jsonb, @simulatedBy, @simulatedAtUtc)
        ", conn);

        insertCmd.Parameters.AddWithValue("id", result.Id);
        insertCmd.Parameters.AddWithValue("tenantId", result.TenantId);
        insertCmd.Parameters.AddWithValue("actionType", result.ActionType);
        insertCmd.Parameters.AddWithValue("title", result.Title);
        insertCmd.Parameters.AddWithValue("verdict", (int)result.Verdict);
        insertCmd.Parameters.AddWithValue("simulationData", JsonSerializer.Serialize(result, JsonOpts));
        insertCmd.Parameters.AddWithValue("simulatedBy", result.SimulatedBy);
        insertCmd.Parameters.AddWithValue("simulatedAtUtc", result.SimulatedAtUtc);

        await insertCmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Policy simulation {SimulationId} completed with verdict {Verdict}.", simulationId, verdict);
        return result;
    }

    // ── GetAsync ────────────────────────────────────────────────

    public async Task<SimulationResult?> GetAsync(
        Guid simulationId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT simulation_data FROM {SimulationsTable} WHERE id = @id AND tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("id", simulationId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        return json is null ? null : JsonSerializer.Deserialize<SimulationResult>(json, JsonOpts);
    }

    // ── ListAsync ───────────────────────────────────────────────

    public async Task<IReadOnlyList<SimulationSummary>> ListAsync(
        Guid tenantId, int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT id, action_type, title, verdict, simulated_by, simulated_at_utc
            FROM {SimulationsTable}
            WHERE tenant_id = @tenantId
            ORDER BY simulated_at_utc DESC
            LIMIT @limit
        ", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<SimulationSummary>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SimulationSummary(
                reader.GetGuid(reader.GetOrdinal("id")),
                reader.GetString(reader.GetOrdinal("action_type")),
                reader.GetString(reader.GetOrdinal("title")),
                (SimulationVerdict)reader.GetInt32(reader.GetOrdinal("verdict")),
                reader.GetString(reader.GetOrdinal("simulated_by")),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("simulated_at_utc"))));
        }
        return results;
    }
}
