using ArchonAI.Core.Models.Governance;

namespace ArchonAI.Core.Interfaces;

public interface ITrustTierService
{
    /// <summary>
    /// Evaluate whether an action is allowed at the requested tier, given tenant policies.
    /// </summary>
    Task<TrustTierEvaluation> EvaluateAsync(
        string tenantId, string actionScope, ExecutionTrustTier requestedTier,
        double? confidence = null, decimal? value = null, bool? reversible = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get the effective maximum tier for a given action scope within a tenant.
    /// </summary>
    Task<ExecutionTrustTier> GetEffectiveTierAsync(
        string tenantId, string actionScope, CancellationToken ct = default);

    /// <summary>
    /// List all trust tier policies for a tenant.
    /// </summary>
    Task<IReadOnlyList<TrustTierPolicy>> ListPoliciesAsync(
        string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Create or update a trust tier policy for a tenant.
    /// </summary>
    Task<TrustTierPolicy> SetPolicyAsync(TrustTierPolicy policy, CancellationToken ct = default);

    /// <summary>
    /// Delete a trust tier policy.
    /// </summary>
    Task<bool> DeletePolicyAsync(
        Guid policyId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// List which action scopes are eligible for each tier within a tenant.
    /// </summary>
    Task<IReadOnlyDictionary<string, ExecutionTrustTier>> GetTierMapAsync(
        string tenantId, CancellationToken ct = default);
}
