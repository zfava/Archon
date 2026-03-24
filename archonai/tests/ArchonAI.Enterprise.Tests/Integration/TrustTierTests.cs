using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Governance;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

public sealed class TrustTierTests
{
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<TrustTierService> _logger = Substitute.For<ILogger<TrustTierService>>();

    private TrustTierService CreateService() => new(_eventBus, _logger);

    // ── Tier Enforcement ──────────────────────────────────────

    [Fact]
    public async Task Evaluate_BlocksWhenRequestedTierExceedsMax()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        // connector.send has default max tier of RecommendOnly (Tier 1)
        var eval = await svc.EvaluateAsync(
            tenantId, "connector.send", ExecutionTrustTier.AutoExecuteReversible);

        Assert.False(eval.Allowed);
        Assert.Equal(ExecutionTrustTier.RecommendOnly, eval.EffectiveTier);
        Assert.Equal(TrustDisposition.Recommend, eval.Disposition);
        Assert.NotNull(eval.Reason);
    }

    [Fact]
    public async Task Evaluate_AllowsWhenRequestedTierWithinMax()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        // data.read has default max tier of AutoExecuteReversible (Tier 3)
        var eval = await svc.EvaluateAsync(
            tenantId, "data.read", ExecutionTrustTier.RecommendOnly);

        Assert.True(eval.Allowed);
        Assert.Equal(ExecutionTrustTier.RecommendOnly, eval.EffectiveTier);
        Assert.Equal(TrustDisposition.Recommend, eval.Disposition);
    }

    [Fact]
    public async Task Evaluate_ExactMatchIsAllowed()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        var eval = await svc.EvaluateAsync(
            tenantId, "data.read", ExecutionTrustTier.AutoExecuteReversible);

        Assert.True(eval.Allowed);
        Assert.Equal(TrustDisposition.AutoExecute, eval.Disposition);
    }

    // ── Approval Required Flow ────────────────────────────────

    [Fact]
    public async Task Evaluate_DraftApprovalRequiredDisposition()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        // workflow.execute has default DraftApprovalRequired (Tier 2)
        var eval = await svc.EvaluateAsync(
            tenantId, "workflow.execute", ExecutionTrustTier.DraftApprovalRequired);

        Assert.True(eval.Allowed);
        Assert.Equal(ExecutionTrustTier.DraftApprovalRequired, eval.EffectiveTier);
        Assert.Equal(TrustDisposition.DraftForApproval, eval.Disposition);
    }

    [Fact]
    public async Task Evaluate_ObserveOnlyForUnknownScope()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        // Unknown scope falls back to ObserveOnly default
        var eval = await svc.EvaluateAsync(
            tenantId, "unknown.action", ExecutionTrustTier.AutoExecuteReversible);

        Assert.False(eval.Allowed);
        Assert.Equal(ExecutionTrustTier.ObserveOnly, eval.EffectiveTier);
        Assert.Equal(TrustDisposition.Observe, eval.Disposition);
    }

    // ── Confidence Gate ───────────────────────────────────────

    [Fact]
    public async Task Evaluate_DowngradeWhenConfidenceBelowThreshold()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        // Set a policy with a confidence threshold
        await svc.SetPolicyAsync(new TrustTierPolicy(
            Guid.NewGuid(), tenantId, "trade.execute",
            ExecutionTrustTier.AutoExecuteHighConfidence,
            ConfidenceThreshold: 0.85, ValueCeiling: null,
            RequireReversible: false, Description: "Trade execution",
            IsEnabled: true, CreatedBy: "admin", CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        // Low confidence — should downgrade from auto-execute to draft+approval
        var eval = await svc.EvaluateAsync(
            tenantId, "trade.execute", ExecutionTrustTier.AutoExecuteHighConfidence,
            confidence: 0.60);

        Assert.False(eval.Allowed);
        Assert.Equal(ExecutionTrustTier.DraftApprovalRequired, eval.EffectiveTier);
        Assert.Equal(TrustDisposition.DraftForApproval, eval.Disposition);
    }

    [Fact]
    public async Task Evaluate_AllowsWhenConfidenceAboveThreshold()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        await svc.SetPolicyAsync(new TrustTierPolicy(
            Guid.NewGuid(), tenantId, "trade.execute",
            ExecutionTrustTier.AutoExecuteHighConfidence,
            ConfidenceThreshold: 0.85, ValueCeiling: null,
            RequireReversible: false, Description: "Trade execution",
            IsEnabled: true, CreatedBy: "admin", CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var eval = await svc.EvaluateAsync(
            tenantId, "trade.execute", ExecutionTrustTier.AutoExecuteHighConfidence,
            confidence: 0.92);

        Assert.True(eval.Allowed);
        Assert.Equal(ExecutionTrustTier.AutoExecuteHighConfidence, eval.EffectiveTier);
        Assert.Equal(TrustDisposition.AutoExecute, eval.Disposition);
    }

    // ── Value Ceiling Gate ────────────────────────────────────

    [Fact]
    public async Task Evaluate_DowngradeWhenValueExceedsCeiling()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        // notification.send has default ceiling of $1000
        var eval = await svc.EvaluateAsync(
            tenantId, "notification.send", ExecutionTrustTier.AutoExecuteReversible,
            value: 5000m);

        Assert.False(eval.Allowed);
        Assert.Equal(ExecutionTrustTier.DraftApprovalRequired, eval.EffectiveTier);
    }

    [Fact]
    public async Task Evaluate_AllowsWhenValueBelowCeiling()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        var eval = await svc.EvaluateAsync(
            tenantId, "notification.send", ExecutionTrustTier.AutoExecuteReversible,
            value: 500m);

        Assert.True(eval.Allowed);
        Assert.Equal(TrustDisposition.AutoExecute, eval.Disposition);
    }

    // ── Reversibility Gate ────────────────────────────────────

    [Fact]
    public async Task Evaluate_DowngradeWhenIrreversibleAndPolicyRequiresReversible()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        // data.read has RequireReversible: true by default
        var eval = await svc.EvaluateAsync(
            tenantId, "data.read", ExecutionTrustTier.AutoExecuteReversible,
            reversible: false);

        Assert.False(eval.Allowed);
        Assert.Equal(ExecutionTrustTier.DraftApprovalRequired, eval.EffectiveTier);
    }

    // ── Tenant Isolation ──────────────────────────────────────

    [Fact]
    public async Task TenantPolicy_OverridesDefaultPolicy()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        // Default for connector.send is RecommendOnly.
        // Override to AutoExecuteReversible for this tenant.
        await svc.SetPolicyAsync(new TrustTierPolicy(
            Guid.NewGuid(), tenantId, "connector.send",
            ExecutionTrustTier.AutoExecuteReversible,
            null, null, false, "Tenant override", true, "admin",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var eval = await svc.EvaluateAsync(
            tenantId, "connector.send", ExecutionTrustTier.AutoExecuteReversible);

        Assert.True(eval.Allowed);
        Assert.Equal(ExecutionTrustTier.AutoExecuteReversible, eval.EffectiveTier);
    }

    [Fact]
    public async Task TenantPolicy_DoesNotAffectOtherTenants()
    {
        var svc = CreateService();
        var tenant1 = Guid.NewGuid().ToString();
        var tenant2 = Guid.NewGuid().ToString();

        await svc.SetPolicyAsync(new TrustTierPolicy(
            Guid.NewGuid(), tenant1, "connector.send",
            ExecutionTrustTier.PolicyEnvelope,
            null, null, false, "Tenant1 override", true, "admin",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        // Tenant 2 should still see the default RecommendOnly
        var eval = await svc.EvaluateAsync(
            tenant2, "connector.send", ExecutionTrustTier.AutoExecuteReversible);

        Assert.False(eval.Allowed);
        Assert.Equal(ExecutionTrustTier.RecommendOnly, eval.EffectiveTier);
    }

    [Fact]
    public async Task DeletePolicy_CannotDeleteOtherTenantPolicy()
    {
        var svc = CreateService();
        var tenant1 = Guid.NewGuid().ToString();
        var tenant2 = Guid.NewGuid().ToString();

        var policy = await svc.SetPolicyAsync(new TrustTierPolicy(
            Guid.NewGuid(), tenant1, "custom.action",
            ExecutionTrustTier.AutoExecuteReversible,
            null, null, false, null, true, "admin",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        // Tenant 2 tries to delete tenant 1's policy
        var deleted = await svc.DeletePolicyAsync(policy.Id, tenant2);
        Assert.False(deleted);

        // Tenant 1 can delete it
        var deletedByOwner = await svc.DeletePolicyAsync(policy.Id, tenant1);
        Assert.True(deletedByOwner);
    }

    // ── Policy CRUD ───────────────────────────────────────────

    [Fact]
    public async Task SetPolicy_CreatesAndRetrievesPolicy()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        var policy = await svc.SetPolicyAsync(new TrustTierPolicy(
            Guid.NewGuid(), tenantId, "invoice.approve",
            ExecutionTrustTier.DraftApprovalRequired,
            0.9, 50_000m, true, "Invoice approvals",
            true, "admin", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var policies = await svc.ListPoliciesAsync(tenantId);
        Assert.Contains(policies, p => p.Id == policy.Id);
        Assert.Contains(policies, p => p.ActionScope == "invoice.approve");
    }

    [Fact]
    public async Task SetPolicy_EmitsEvent()
    {
        var svc = CreateService();
        _eventBus.ClearReceivedCalls();

        await svc.SetPolicyAsync(new TrustTierPolicy(
            Guid.NewGuid(), "test-tenant", "test.action",
            ExecutionTrustTier.RecommendOnly,
            null, null, false, null, true, "admin",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "trust_tier.policy.set"),
            Arg.Any<CancellationToken>());
    }

    // ── Tier Map ──────────────────────────────────────────────

    [Fact]
    public async Task GetTierMap_ReturnsAllScopesWithEffectiveTiers()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        var map = await svc.GetTierMapAsync(tenantId);

        Assert.True(map.Count >= 5); // At least the 5 defaults
        Assert.Equal(ExecutionTrustTier.DraftApprovalRequired, map["workflow.execute"]);
        Assert.Equal(ExecutionTrustTier.RecommendOnly, map["connector.send"]);
        Assert.Equal(ExecutionTrustTier.AutoExecuteReversible, map["data.read"]);
    }

    [Fact]
    public async Task GetTierMap_TenantOverrideAppearsInMap()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid().ToString();

        await svc.SetPolicyAsync(new TrustTierPolicy(
            Guid.NewGuid(), tenantId, "connector.send",
            ExecutionTrustTier.PolicyEnvelope,
            null, null, false, null, true, "admin",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var map = await svc.GetTierMapAsync(tenantId);
        Assert.Equal(ExecutionTrustTier.PolicyEnvelope, map["connector.send"]);
    }

    // ── Effective Tier ────────────────────────────────────────

    [Fact]
    public async Task GetEffectiveTier_ReturnsDefaultForUnknownScope()
    {
        var svc = CreateService();
        var tier = await svc.GetEffectiveTierAsync("any-tenant", "nonexistent.scope");
        Assert.Equal(ExecutionTrustTier.ObserveOnly, tier);
    }

    [Fact]
    public async Task GetEffectiveTier_ReturnsPolicyTier()
    {
        var svc = CreateService();
        var tier = await svc.GetEffectiveTierAsync("any-tenant", "workflow.execute");
        Assert.Equal(ExecutionTrustTier.DraftApprovalRequired, tier);
    }
}
