using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Governance;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Api.Security;

public sealed class TrustTierService : ITrustTierService
{
    private readonly ConcurrentDictionary<Guid, TrustTierPolicy> _policies = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<TrustTierService> _logger;

    /// <summary>
    /// Default tier when no policy exists for a scope — observe only.
    /// </summary>
    private const ExecutionTrustTier DefaultTier = ExecutionTrustTier.ObserveOnly;

    public TrustTierService(IEventBus eventBus, ILogger<TrustTierService> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
        SeedDefaults();
    }

    private void SeedDefaults()
    {
        var now = DateTimeOffset.UtcNow;
        var defaults = new[]
        {
            new TrustTierPolicy(Guid.NewGuid(), "__default__", "workflow.execute",
                ExecutionTrustTier.DraftApprovalRequired, null, null, false,
                "Workflow execution requires approval by default", true, "system", now, now),
            new TrustTierPolicy(Guid.NewGuid(), "__default__", "decision.execute",
                ExecutionTrustTier.DraftApprovalRequired, null, null, false,
                "Decision execution requires approval by default", true, "system", now, now),
            new TrustTierPolicy(Guid.NewGuid(), "__default__", "connector.send",
                ExecutionTrustTier.RecommendOnly, null, null, false,
                "External system writes are recommend-only by default", true, "system", now, now),
            new TrustTierPolicy(Guid.NewGuid(), "__default__", "data.read",
                ExecutionTrustTier.AutoExecuteReversible, null, null, true,
                "Data reads are auto-executable (reversible only)", true, "system", now, now),
            new TrustTierPolicy(Guid.NewGuid(), "__default__", "notification.send",
                ExecutionTrustTier.AutoExecuteReversible, null, 1000m, false,
                "Notifications auto-execute under $1K impact", true, "system", now, now),
        };

        foreach (var p in defaults)
            _policies[p.Id] = p;
    }

    public Task<TrustTierEvaluation> EvaluateAsync(
        string tenantId, string actionScope, ExecutionTrustTier requestedTier,
        double? confidence = null, decimal? value = null, bool? reversible = null,
        CancellationToken ct = default)
    {
        var policy = FindPolicy(tenantId, actionScope);
        var maxTier = policy?.MaxTier ?? DefaultTier;
        var effectiveTier = (ExecutionTrustTier)Math.Min((int)requestedTier, (int)maxTier);

        // Apply guardrails from the policy
        if (policy is not null)
        {
            // Confidence gate: if policy requires a confidence threshold and action doesn't meet it, downgrade
            if (policy.ConfidenceThreshold.HasValue && confidence.HasValue
                && confidence.Value < policy.ConfidenceThreshold.Value
                && effectiveTier >= ExecutionTrustTier.AutoExecuteReversible)
            {
                effectiveTier = ExecutionTrustTier.DraftApprovalRequired;
            }

            // Value ceiling: if action value exceeds ceiling, require approval
            if (policy.ValueCeiling.HasValue && value.HasValue
                && value.Value > policy.ValueCeiling.Value
                && effectiveTier >= ExecutionTrustTier.AutoExecuteReversible)
            {
                effectiveTier = ExecutionTrustTier.DraftApprovalRequired;
            }

            // Reversibility gate: if policy requires reversible and action isn't, cap at DraftApprovalRequired
            if (policy.RequireReversible && reversible == false
                && effectiveTier >= ExecutionTrustTier.AutoExecuteReversible)
            {
                effectiveTier = ExecutionTrustTier.DraftApprovalRequired;
            }
        }

        var allowed = effectiveTier >= requestedTier;
        var disposition = DetermineDisposition(effectiveTier);
        string? reason = null;

        if (!allowed)
        {
            reason = $"Requested tier {requestedTier} exceeds maximum allowed tier {maxTier} for scope '{actionScope}'.";
            if (policy is not null)
            {
                if (policy.ConfidenceThreshold.HasValue && confidence.HasValue && confidence.Value < policy.ConfidenceThreshold.Value)
                    reason += $" Confidence {confidence:F2} below threshold {policy.ConfidenceThreshold:F2}.";
                if (policy.ValueCeiling.HasValue && value.HasValue && value.Value > policy.ValueCeiling.Value)
                    reason += $" Value ${value} exceeds ceiling ${policy.ValueCeiling}.";
                if (policy.RequireReversible && reversible == false)
                    reason += " Action is irreversible but policy requires reversibility.";
            }
        }

        var evaluation = new TrustTierEvaluation(
            actionScope, requestedTier, effectiveTier, allowed, disposition, reason);

        _logger.LogInformation(
            "Trust tier evaluation: scope={Scope} requested={Requested} effective={Effective} allowed={Allowed} disposition={Disposition}",
            actionScope, requestedTier, effectiveTier, allowed, disposition);

        return Task.FromResult(evaluation);
    }

    public Task<ExecutionTrustTier> GetEffectiveTierAsync(
        string tenantId, string actionScope, CancellationToken ct = default)
    {
        var policy = FindPolicy(tenantId, actionScope);
        return Task.FromResult(policy?.MaxTier ?? DefaultTier);
    }

    public Task<IReadOnlyList<TrustTierPolicy>> ListPoliciesAsync(
        string tenantId, CancellationToken ct = default)
    {
        IReadOnlyList<TrustTierPolicy> result = _policies.Values
            .Where(p => p.TenantId == tenantId || p.TenantId == "__default__")
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.ActionScope)
            .ToList();
        return Task.FromResult(result);
    }

    public async Task<TrustTierPolicy> SetPolicyAsync(TrustTierPolicy policy, CancellationToken ct = default)
    {
        _policies[policy.Id] = policy;

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "trust_tier.policy.set",
            "TrustTierService",
            policy.Id,
            new Dictionary<string, string>
            {
                ["policyId"] = policy.Id.ToString(),
                ["tenantId"] = policy.TenantId,
                ["actionScope"] = policy.ActionScope,
                ["maxTier"] = policy.MaxTier.ToString(),
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation(
            "Trust tier policy set: {PolicyId} scope={Scope} maxTier={MaxTier} tenant={TenantId}",
            policy.Id, policy.ActionScope, policy.MaxTier, policy.TenantId);

        return policy;
    }

    public Task<bool> DeletePolicyAsync(Guid policyId, string tenantId, CancellationToken ct = default)
    {
        if (!_policies.TryGetValue(policyId, out var existing))
            return Task.FromResult(false);

        // Enforce tenant isolation — cannot delete another tenant's policy or system defaults
        if (existing.TenantId != tenantId)
            return Task.FromResult(false);

        var removed = _policies.TryRemove(policyId, out _);
        return Task.FromResult(removed);
    }

    public Task<IReadOnlyDictionary<string, ExecutionTrustTier>> GetTierMapAsync(
        string tenantId, CancellationToken ct = default)
    {
        IReadOnlyDictionary<string, ExecutionTrustTier> map = _policies.Values
            .Where(p => (p.TenantId == tenantId || p.TenantId == "__default__") && p.IsEnabled)
            .GroupBy(p => p.ActionScope)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    // Tenant-specific policy overrides default
                    var tenantPolicy = g.FirstOrDefault(p => p.TenantId == tenantId);
                    return tenantPolicy?.MaxTier ?? g.First().MaxTier;
                });
        return Task.FromResult(map);
    }

    private TrustTierPolicy? FindPolicy(string tenantId, string actionScope)
    {
        // Tenant-specific policy takes precedence over defaults
        var tenantPolicy = _policies.Values.FirstOrDefault(p =>
            p.TenantId == tenantId && p.ActionScope == actionScope && p.IsEnabled);

        if (tenantPolicy is not null)
            return tenantPolicy;

        // Fall back to default policy
        return _policies.Values.FirstOrDefault(p =>
            p.TenantId == "__default__" && p.ActionScope == actionScope && p.IsEnabled);
    }

    private static string DetermineDisposition(ExecutionTrustTier tier) => tier switch
    {
        ExecutionTrustTier.ObserveOnly => TrustDisposition.Observe,
        ExecutionTrustTier.RecommendOnly => TrustDisposition.Recommend,
        ExecutionTrustTier.DraftApprovalRequired => TrustDisposition.DraftForApproval,
        >= ExecutionTrustTier.AutoExecuteReversible => TrustDisposition.AutoExecute,
        _ => TrustDisposition.Blocked,
    };
}
