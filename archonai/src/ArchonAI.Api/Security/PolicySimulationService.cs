using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.HeroWorkflow;
using ArchonAI.Core.Models.PolicySimulation;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Api.Security;

/// <summary>
/// Simulates governed actions against the full policy stack without executing side effects.
/// All evaluations are read-only projections: no decisions are created, no approvals are
/// requested, no events are published, no state is mutated.
/// </summary>
public sealed class PolicySimulationService : IPolicySimulationService
{
    private readonly ConcurrentDictionary<Guid, SimulationResult> _results = new();
    private readonly ITrustTierService _trustTiers;
    private readonly IGovernanceService _governance;
    private readonly IHeroWorkflowService _heroWorkflows;
    private readonly ILogger<PolicySimulationService> _logger;

    public PolicySimulationService(
        ITrustTierService trustTiers,
        IGovernanceService governance,
        IHeroWorkflowService heroWorkflows,
        ILogger<PolicySimulationService> logger)
    {
        _trustTiers = trustTiers;
        _governance = governance;
        _heroWorkflows = heroWorkflows;
        _logger = logger;
    }

    public async Task<SimulationResult> SimulateAsync(
        SimulationRequest request, CancellationToken ct = default)
    {
        var reasons = new List<string>();
        var policyOutcomes = new List<PolicyOutcome>();
        var now = DateTimeOffset.UtcNow;

        // ── 1. Simulate decision ────────────────────────────────
        var riskLevel = Enum.TryParse<DecisionRiskLevel>(request.RiskLevel, true, out var rl)
            ? rl : DecisionRiskLevel.Medium;
        var reversibility = Enum.TryParse<DecisionReversibility>(request.Reversibility, true, out var rev)
            ? rev : DecisionReversibility.PartiallyReversible;
        var confidence = Math.Clamp(request.Confidence ?? 0.7, 0.0, 1.0);
        var wouldRequireApproval = riskLevel >= DecisionRiskLevel.High;

        var simulatedDecision = new SimulatedDecision(
            Title: request.Title,
            Domain: request.Domain ?? "operations",
            RiskLevel: riskLevel,
            Reversibility: reversibility,
            Confidence: confidence,
            ExpectedValue: request.ExpectedValue,
            WouldRequireApproval: wouldRequireApproval,
            ProposedStatus: DecisionStatus.Proposed);

        reasons.Add($"Decision would be created with risk level '{riskLevel}' and confidence {confidence:P0}.");
        if (wouldRequireApproval)
            reasons.Add("High/Critical risk level triggers mandatory approval requirement.");

        policyOutcomes.Add(new PolicyOutcome(
            "RiskLevelPolicy", "DecisionRisk",
            riskLevel < DecisionRiskLevel.High,
            riskLevel < DecisionRiskLevel.High
                ? "Risk level is within auto-approval threshold."
                : $"Risk level '{riskLevel}' exceeds auto-approval threshold."));

        // ── 2. Simulate trust tier evaluation ───────────────────
        var requestedTier = Enum.TryParse<ExecutionTrustTier>(
            request.RequestedTier ?? "DraftApprovalRequired", true, out var tier)
            ? tier : ExecutionTrustTier.DraftApprovalRequired;

        // This is a READ-ONLY call — EvaluateAsync does not mutate state
        var trustEval = await _trustTiers.EvaluateAsync(
            request.TenantId.ToString(),
            request.ActionScope,
            requestedTier,
            confidence,
            request.ExpectedValue,
            reversibility == DecisionReversibility.FullyReversible,
            ct);

        reasons.Add($"Trust tier evaluation: disposition='{trustEval.Disposition}', effective tier='{trustEval.EffectiveTier}'.");
        if (trustEval.Reason is not null)
            reasons.Add($"Trust tier reason: {trustEval.Reason}");

        policyOutcomes.Add(new PolicyOutcome(
            "TrustTierPolicy", "TrustTier",
            trustEval.Allowed,
            trustEval.Allowed
                ? $"Action allowed at tier '{trustEval.EffectiveTier}' ({trustEval.Disposition})."
                : $"Action blocked: effective tier '{trustEval.EffectiveTier}' insufficient for requested '{trustEval.RequestedTier}'."));

        // ── 3. Simulate approval requirement ────────────────────
        // RequiresApprovalAsync is a READ-ONLY check against existing policies
        var approvalRequired = await _governance.RequiresApprovalAsync(request.ActionType, ct);
        ApprovalPolicy? matchedPolicy = null;

        if (approvalRequired)
        {
            var policies = await _governance.ListApprovalPoliciesAsync(ct);
            matchedPolicy = policies.FirstOrDefault(p =>
                p.IsEnabled && p.ActionType.Equals(request.ActionType, StringComparison.OrdinalIgnoreCase));
        }

        // Also check if trust tier disposition forces approval
        var trustRequiresApproval = trustEval.Disposition == TrustDisposition.DraftForApproval;
        var effectiveApprovalRequired = approvalRequired || trustRequiresApproval || wouldRequireApproval;

        var simulatedApproval = new SimulatedApproval(
            Required: effectiveApprovalRequired,
            RequiredApproverRole: matchedPolicy?.RequiredApproverRole,
            RequiresSeparationOfDuties: matchedPolicy?.RequireSeparationOfDuties ?? false,
            MatchedPolicyActionType: matchedPolicy?.ActionType,
            Explanation: effectiveApprovalRequired
                ? BuildApprovalExplanation(approvalRequired, trustRequiresApproval, wouldRequireApproval, matchedPolicy)
                : "No approval required. Action can proceed autonomously.");

        if (effectiveApprovalRequired)
            reasons.Add($"Approval required: {simulatedApproval.Explanation}");

        policyOutcomes.Add(new PolicyOutcome(
            "ApprovalPolicy", "Approval",
            !effectiveApprovalRequired,
            simulatedApproval.Explanation));

        // ── 4. Simulate economic effect ─────────────────────────
        SimulatedEconomicEffect? economicEffect = null;
        if (request.RevenueImpactLow.HasValue || request.RevenueImpactHigh.HasValue ||
            request.CostImpactLow.HasValue || request.CostImpactHigh.HasValue)
        {
            var netLow = (request.RevenueImpactLow ?? 0) - (request.CostImpactHigh ?? 0);
            var netHigh = (request.RevenueImpactHigh ?? 0) - (request.CostImpactLow ?? 0);

            economicEffect = new SimulatedEconomicEffect(
                RevenueImpactLow: request.RevenueImpactLow,
                RevenueImpactHigh: request.RevenueImpactHigh,
                CostImpactLow: request.CostImpactLow,
                CostImpactHigh: request.CostImpactHigh,
                NetImpactLow: netLow,
                NetImpactHigh: netHigh,
                DownsideRisk: request.DownsideRisk,
                UpsidePotential: request.UpsidePotential,
                Summary: $"Net impact range: {netLow:C0} to {netHigh:C0}");

            reasons.Add($"Projected economic impact: net {netLow:C0} to {netHigh:C0}.");

            if (request.DownsideRisk.HasValue)
                reasons.Add($"Downside risk exposure: {request.DownsideRisk:C0}.");
        }

        // ── 5. Simulate workflow preview ────────────────────────
        SimulatedWorkflowPreview? workflowPreview = null;
        if (request.WorkflowType is not null)
        {
            var def = await _heroWorkflows.GetDefinitionAsync(request.WorkflowType, ct);
            if (def is not null)
            {
                var steps = def.Steps.Select(s => new SimulatedWorkflowStep(
                    s.StepId, s.Name, s.Subsystem,
                    ProjectStepOutcome(s, trustEval, effectiveApprovalRequired))).ToList();

                workflowPreview = new SimulatedWorkflowPreview(
                    def.WorkflowType, def.DisplayName, def.Steps.Count, steps);

                reasons.Add($"Workflow '{def.DisplayName}' would execute {def.Steps.Count} steps.");
            }
        }

        // ── 6. Determine verdict ────────────────────────────────
        var verdict = DetermineVerdict(trustEval, effectiveApprovalRequired);

        var result = new SimulationResult(
            Id: Guid.NewGuid(),
            TenantId: request.TenantId,
            ActionType: request.ActionType,
            Title: request.Title,
            Verdict: verdict,
            Decision: simulatedDecision,
            TrustTierOutcome: trustEval,
            ApprovalRequirement: simulatedApproval,
            PolicyOutcomes: policyOutcomes,
            EconomicEffect: economicEffect,
            WorkflowPreview: workflowPreview,
            Reasons: reasons,
            SimulatedBy: request.RequestedBy,
            SimulatedAtUtc: now);

        _results[result.Id] = result;

        _logger.LogInformation(
            "Policy simulation {SimulationId}: {Title} → {Verdict} (tenant={TenantId})",
            result.Id, request.Title, verdict, request.TenantId);

        return result;
    }

    public Task<SimulationResult?> GetAsync(Guid simulationId, Guid tenantId, CancellationToken ct = default)
    {
        _results.TryGetValue(simulationId, out var result);
        if (result is not null && result.TenantId != tenantId) return Task.FromResult<SimulationResult?>(null);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<SimulationSummary>> ListAsync(
        Guid tenantId, int limit = 50, CancellationToken ct = default)
    {
        IReadOnlyList<SimulationSummary> summaries = _results.Values
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.SimulatedAtUtc)
            .Take(limit)
            .Select(r => new SimulationSummary(
                r.Id, r.ActionType, r.Title, r.Verdict,
                r.SimulatedBy, r.SimulatedAtUtc))
            .ToList();

        return Task.FromResult(summaries);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static SimulationVerdict DetermineVerdict(
        TrustTierEvaluation trustEval, bool approvalRequired)
    {
        if (!trustEval.Allowed)
            return SimulationVerdict.Blocked;

        return trustEval.Disposition switch
        {
            TrustDisposition.Observe => SimulationVerdict.ObserveOnly,
            TrustDisposition.Recommend => SimulationVerdict.RecommendOnly,
            TrustDisposition.DraftForApproval => SimulationVerdict.RequiresApproval,
            TrustDisposition.AutoExecute when approvalRequired => SimulationVerdict.RequiresApproval,
            TrustDisposition.AutoExecute => SimulationVerdict.Allowed,
            _ => approvalRequired ? SimulationVerdict.RequiresApproval : SimulationVerdict.Allowed,
        };
    }

    private static string BuildApprovalExplanation(
        bool policyApproval, bool trustApproval, bool riskApproval,
        ApprovalPolicy? matchedPolicy)
    {
        var parts = new List<string>();
        if (policyApproval && matchedPolicy is not null)
            parts.Add($"Governance policy '{matchedPolicy.ActionType}' requires approval by '{matchedPolicy.RequiredApproverRole}'");
        if (trustApproval)
            parts.Add("Trust tier disposition is 'draft_for_approval'");
        if (riskApproval)
            parts.Add("Decision risk level (High/Critical) requires approval");

        return string.Join("; ", parts) + ".";
    }

    private static string ProjectStepOutcome(
        HeroStepDefinition step, TrustTierEvaluation trustEval, bool approvalRequired)
    {
        return step.Subsystem switch
        {
            "decision" => "Decision record would be created in Proposed status.",
            "financial-consequence" => "Financial consequence model would be attached.",
            "trust-tier" => $"Trust evaluation: {trustEval.Disposition} (tier={trustEval.EffectiveTier}).",
            "approval" => approvalRequired
                ? "Approval gate would be created and routed for review."
                : "Approval step would be skipped (auto-execution allowed).",
            "execution" => "Decision would transition to Executing; expected outcome recorded.",
            "outcome" => "Actual outcome would be captured for calibration.",
            "exception" => "Operational exception would be raised.",
            "memory" => "Institutional memory would be stored.",
            _ => $"Step '{step.Name}' would execute.",
        };
    }
}
