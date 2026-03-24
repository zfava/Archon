using ArchonAI.Core.Models.Governance;
using ArchonAI.Enterprise.Tests.Infrastructure;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Real PostgreSQL integration tests for <see cref="ArchonAI.Persistence.Stores.PostgresTrustTierStore"/>.
/// Uses Testcontainers to start an ephemeral PostgreSQL instance.
///
/// These tests prove that trust tier state (policies, evaluations, tenant seeding)
/// survives store re-instantiation — the critical persistence guarantee.
/// </summary>
[Collection("PostgresRbacTrustTier")]
[Trait("Category", "Integration")]
[Trait("Database", "PostgreSQL")]
public sealed class PostgresTrustTierPersistenceTests : IAsyncLifetime
{
    private readonly PostgresRbacTrustTierFixture _fixture;

    public PostgresTrustTierPersistenceTests(PostgresRbacTrustTierFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanTablesAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Test 1: Per-tenant auto-seeding ───────────────────────────────────

    [Fact]
    public async Task ListPolicies_AutoSeedsDefaultsForNewTenant()
    {
        var store = _fixture.CreateTrustTierStore();

        var policies = await store.ListPoliciesAsync("tenant-seed-test");

        // 5 default policies are seeded per tenant
        Assert.Equal(5, policies.Count);
        Assert.Contains(policies, p => p.ActionScope == "workflow.execute");
        Assert.Contains(policies, p => p.ActionScope == "decision.execute");
        Assert.Contains(policies, p => p.ActionScope == "connector.send");
        Assert.Contains(policies, p => p.ActionScope == "data.read");
        Assert.Contains(policies, p => p.ActionScope == "notification.send");

        // All seeded by "system"
        Assert.All(policies, p => Assert.Equal("system", p.CreatedBy));
    }

    // ── Test 2: Auto-seeding survives re-instantiation ───────────────────

    [Fact]
    public async Task AutoSeeding_SurvivesStoreReinstantiation()
    {
        var store1 = _fixture.CreateTrustTierStore();
        var policies1 = await store1.ListPoliciesAsync("tenant-seed-restart");
        Assert.Equal(5, policies1.Count);

        // New instance — should find existing policies, not re-seed
        var store2 = _fixture.CreateTrustTierStore();
        var policies2 = await store2.ListPoliciesAsync("tenant-seed-restart");
        Assert.Equal(5, policies2.Count);

        // IDs should match (same policies, not duplicated)
        var ids1 = policies1.Select(p => p.Id).OrderBy(id => id).ToList();
        var ids2 = policies2.Select(p => p.Id).OrderBy(id => id).ToList();
        Assert.Equal(ids1, ids2);
    }

    // ── Test 3: SetPolicy (upsert) persists across instances ─────────────

    [Fact]
    public async Task SetPolicy_PersistsAcrossInstances()
    {
        var store1 = _fixture.CreateTrustTierStore();
        var now = DateTimeOffset.UtcNow;

        var policy = new TrustTierPolicy(
            Guid.NewGuid(), "tenant-set", "custom.action",
            ExecutionTrustTier.AutoExecuteHighConfidence,
            0.85, 5000m, true,
            "Custom high-confidence action",
            true, "admin-alice", now, now);

        await store1.SetPolicyAsync(policy);

        // New instance — policy survives
        var store2 = _fixture.CreateTrustTierStore();
        var policies = await store2.ListPoliciesAsync("tenant-set");

        // 5 auto-seeded + 1 custom
        var custom = policies.FirstOrDefault(p => p.ActionScope == "custom.action");
        Assert.NotNull(custom);
        Assert.Equal(policy.Id, custom.Id);
        Assert.Equal(ExecutionTrustTier.AutoExecuteHighConfidence, custom.MaxTier);
        Assert.Equal(0.85, custom.ConfidenceThreshold);
        Assert.Equal(5000m, custom.ValueCeiling);
        Assert.True(custom.RequireReversible);
        Assert.Equal("admin-alice", custom.CreatedBy);
    }

    // ── Test 4: SetPolicy upsert updates existing policy ─────────────────

    [Fact]
    public async Task SetPolicy_UpsertUpdatesExistingPolicy()
    {
        var store1 = _fixture.CreateTrustTierStore();
        var now = DateTimeOffset.UtcNow;
        var policyId = Guid.NewGuid();

        var v1 = new TrustTierPolicy(
            policyId, "tenant-upsert", "upsert.action",
            ExecutionTrustTier.DraftApprovalRequired,
            null, null, false, "Version 1", true, "admin", now, now);

        await store1.SetPolicyAsync(v1);

        // Update via upsert with same ID
        var v2 = new TrustTierPolicy(
            policyId, "tenant-upsert", "upsert.action",
            ExecutionTrustTier.PolicyEnvelope,
            0.95, 10000m, true, "Version 2", true, "admin", now, DateTimeOffset.UtcNow);

        await store1.SetPolicyAsync(v2);

        // New instance — should see updated values
        var store2 = _fixture.CreateTrustTierStore();
        var policies = await store2.ListPoliciesAsync("tenant-upsert");
        var fetched = policies.First(p => p.Id == policyId);

        Assert.Equal(ExecutionTrustTier.PolicyEnvelope, fetched.MaxTier);
        Assert.Equal(0.95, fetched.ConfidenceThreshold);
        Assert.Equal(10000m, fetched.ValueCeiling);
        Assert.True(fetched.RequireReversible);
        Assert.Equal("Version 2", fetched.Description);
    }

    // ── Test 5: Evaluation with guardrails ───────────────────────────────

    [Fact]
    public async Task Evaluate_ConfidenceGate_DemotesEffectiveTier()
    {
        var store = _fixture.CreateTrustTierStore();
        var now = DateTimeOffset.UtcNow;

        // Set a policy with confidence threshold
        await store.SetPolicyAsync(new TrustTierPolicy(
            Guid.NewGuid(), "tenant-eval-conf", "gated.action",
            ExecutionTrustTier.AutoExecuteReversible,
            0.90, null, false,
            "Requires 90% confidence", true, "admin", now, now));

        // Evaluate with low confidence — should demote
        var result = await store.EvaluateAsync(
            "tenant-eval-conf", "gated.action",
            ExecutionTrustTier.AutoExecuteReversible,
            confidence: 0.50);

        Assert.Equal(ExecutionTrustTier.DraftApprovalRequired, result.EffectiveTier);
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task Evaluate_ValueCeilingGate_DemotesEffectiveTier()
    {
        var store = _fixture.CreateTrustTierStore();
        var now = DateTimeOffset.UtcNow;

        await store.SetPolicyAsync(new TrustTierPolicy(
            Guid.NewGuid(), "tenant-eval-val", "value.action",
            ExecutionTrustTier.AutoExecuteHighConfidence,
            null, 1000m, false,
            "Max $1K", true, "admin", now, now));

        // Value exceeds ceiling — should demote
        var result = await store.EvaluateAsync(
            "tenant-eval-val", "value.action",
            ExecutionTrustTier.AutoExecuteHighConfidence,
            value: 5000m);

        Assert.Equal(ExecutionTrustTier.DraftApprovalRequired, result.EffectiveTier);
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task Evaluate_ReversibilityGate_DemotesEffectiveTier()
    {
        var store = _fixture.CreateTrustTierStore();
        var now = DateTimeOffset.UtcNow;

        await store.SetPolicyAsync(new TrustTierPolicy(
            Guid.NewGuid(), "tenant-eval-rev", "reversible.action",
            ExecutionTrustTier.AutoExecuteReversible,
            null, null, true,
            "Must be reversible", true, "admin", now, now));

        // Irreversible action — should demote
        var result = await store.EvaluateAsync(
            "tenant-eval-rev", "reversible.action",
            ExecutionTrustTier.AutoExecuteReversible,
            reversible: false);

        Assert.Equal(ExecutionTrustTier.DraftApprovalRequired, result.EffectiveTier);
        Assert.False(result.Allowed);
    }

    // ── Test 6: Evaluation survives re-instantiation ─────────────────────

    [Fact]
    public async Task Evaluate_PolicyPersistsAndEvaluatesCorrectlyAfterRestart()
    {
        var store1 = _fixture.CreateTrustTierStore();
        var now = DateTimeOffset.UtcNow;

        await store1.SetPolicyAsync(new TrustTierPolicy(
            Guid.NewGuid(), "tenant-eval-restart", "restart.action",
            ExecutionTrustTier.AutoExecuteReversible,
            0.80, null, false,
            "Persisted policy", true, "admin", now, now));

        // New instance — evaluate using persisted policy
        var store2 = _fixture.CreateTrustTierStore();

        // High confidence — should pass
        var pass = await store2.EvaluateAsync(
            "tenant-eval-restart", "restart.action",
            ExecutionTrustTier.AutoExecuteReversible,
            confidence: 0.95);

        Assert.True(pass.Allowed);
        Assert.Equal(ExecutionTrustTier.AutoExecuteReversible, pass.EffectiveTier);

        // Low confidence — should fail
        var fail = await store2.EvaluateAsync(
            "tenant-eval-restart", "restart.action",
            ExecutionTrustTier.AutoExecuteReversible,
            confidence: 0.50);

        Assert.False(fail.Allowed);
    }

    // ── Test 7: Tenant isolation on delete ────────────────────────────────

    [Fact]
    public async Task DeletePolicy_TenantIsolation_CannotDeleteCrossTenant()
    {
        var store = _fixture.CreateTrustTierStore();
        var now = DateTimeOffset.UtcNow;
        var policyId = Guid.NewGuid();

        // Create policy for tenant-A
        await store.SetPolicyAsync(new TrustTierPolicy(
            policyId, "tenant-iso-a", "isolated.action",
            ExecutionTrustTier.PolicyEnvelope,
            null, null, false,
            "Tenant A policy", true, "admin", now, now));

        // Attempt delete from tenant-B — should fail (return false)
        var deleted = await store.DeletePolicyAsync(policyId, "tenant-iso-b");
        Assert.False(deleted);

        // Verify policy still exists via new instance
        var store2 = _fixture.CreateTrustTierStore();
        var policies = await store2.ListPoliciesAsync("tenant-iso-a");
        Assert.Contains(policies, p => p.Id == policyId);
    }

    // ── Test 8: Tenant isolation — policies are scoped ───────────────────

    [Fact]
    public async Task ListPolicies_TenantIsolation_OnlySeesOwnPolicies()
    {
        var store = _fixture.CreateTrustTierStore();

        // Trigger auto-seeding for two tenants
        var policiesA = await store.ListPoliciesAsync("tenant-list-a");
        var policiesB = await store.ListPoliciesAsync("tenant-list-b");

        // Each tenant should have their own 5 default policies
        Assert.Equal(5, policiesA.Count);
        Assert.Equal(5, policiesB.Count);

        // No overlap in IDs
        var idsA = policiesA.Select(p => p.Id).ToHashSet();
        var idsB = policiesB.Select(p => p.Id).ToHashSet();
        Assert.Empty(idsA.Intersect(idsB));

        // All policies scoped to their tenant
        Assert.All(policiesA, p => Assert.Equal("tenant-list-a", p.TenantId));
        Assert.All(policiesB, p => Assert.Equal("tenant-list-b", p.TenantId));
    }

    // ── Test 9: GetEffectiveTier reflects policy ─────────────────────────

    [Fact]
    public async Task GetEffectiveTier_ReflectsSetPolicy_AcrossInstances()
    {
        var store1 = _fixture.CreateTrustTierStore();
        var now = DateTimeOffset.UtcNow;

        await store1.SetPolicyAsync(new TrustTierPolicy(
            Guid.NewGuid(), "tenant-tier", "tier.action",
            ExecutionTrustTier.RecommendOnly,
            null, null, false,
            "Recommend only", true, "admin", now, now));

        // New instance
        var store2 = _fixture.CreateTrustTierStore();
        var tier = await store2.GetEffectiveTierAsync("tenant-tier", "tier.action");

        Assert.Equal(ExecutionTrustTier.RecommendOnly, tier);
    }

    // ── Test 10: GetTierMap reflects all policies ────────────────────────

    [Fact]
    public async Task GetTierMap_ReflectsAllPolicies_AcrossInstances()
    {
        var store1 = _fixture.CreateTrustTierStore();

        // Trigger auto-seeding
        await store1.ListPoliciesAsync("tenant-map");

        // New instance — get tier map
        var store2 = _fixture.CreateTrustTierStore();
        var map = await store2.GetTierMapAsync("tenant-map");

        Assert.Equal(5, map.Count);
        Assert.Equal(ExecutionTrustTier.AutoExecuteReversible, map["workflow.execute"]);
        Assert.Equal(ExecutionTrustTier.DraftApprovalRequired, map["decision.execute"]);
        Assert.Equal(ExecutionTrustTier.AutoExecuteReversible, map["connector.send"]);
        Assert.Equal(ExecutionTrustTier.PolicyEnvelope, map["data.read"]);
        Assert.Equal(ExecutionTrustTier.AutoExecuteHighConfidence, map["notification.send"]);
    }

    // ── Test 11: Delete policy persists across instances ─────────────────

    [Fact]
    public async Task DeletePolicy_PersistsAcrossInstances()
    {
        var store1 = _fixture.CreateTrustTierStore();
        var now = DateTimeOffset.UtcNow;
        var policyId = Guid.NewGuid();

        await store1.SetPolicyAsync(new TrustTierPolicy(
            policyId, "tenant-del", "delete.action",
            ExecutionTrustTier.ObserveOnly,
            null, null, false,
            "To be deleted", true, "admin", now, now));

        var deleted = await store1.DeletePolicyAsync(policyId, "tenant-del");
        Assert.True(deleted);

        // New instance — policy should be gone
        var store2 = _fixture.CreateTrustTierStore();
        var policies = await store2.ListPoliciesAsync("tenant-del");
        Assert.DoesNotContain(policies, p => p.Id == policyId);
    }
}
