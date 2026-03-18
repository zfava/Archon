using ArchonAI.Api.Security;
using ArchonAI.Identity;
using ArchonAI.Identity.Stores;
using ArchonAI.MultiTenant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Enterprise.Tests.Security;

/// <summary>
/// Security tests for cross-tenant access prevention:
/// user store isolation, approval gate isolation, memory store isolation,
/// and governance scope leakage prevention.
/// </summary>
public sealed class CrossTenantAccessTests
{
    private AuthenticationService CreateAuth()
    {
        var users = new InMemoryUserStore();
        var orgs = new InMemoryOrganizationStore();
        var memberships = new InMemoryMembershipStore();
        var refreshTokens = new InMemoryRefreshTokenStore();
        var invites = new InMemoryInviteTokenStore();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Jwt:SigningKey"] = "test-signing-key-that-is-at-least-32-bytes-long!!",
                ["Security:Jwt:Issuer"] = "ArchonAI",
                ["Security:Jwt:Audience"] = "ArchonAI.Api",
            })
            .Build();

        return new AuthenticationService(
            users, orgs, memberships, refreshTokens, invites,
            new TokenService(config),
            Options.Create(new AuthenticationOptions()),
            NullLogger<AuthenticationService>.Instance);
    }

    // ── User Registration Isolation ───────────────────────────────────

    [Fact]
    public async Task DifferentOrgs_GetDifferentOrgIds()
    {
        var auth = CreateAuth();
        var orgA = await auth.RegisterAsync("Org A", "a@a.com", "pass123!", "Admin A");
        var orgB = await auth.RegisterAsync("Org B", "b@b.com", "pass123!", "Admin B");

        Assert.NotNull(orgA);
        Assert.NotNull(orgB);
        Assert.NotEqual(orgA.Value.Org.Id, orgB.Value.Org.Id);
    }

    [Fact]
    public async Task Login_YieldsCorrectOrgContext()
    {
        var auth = CreateAuth();
        await auth.RegisterAsync("Acme Corp", "admin@acme.com", "pass123!", "Admin");
        var login = await auth.LoginAsync("admin@acme.com", "pass123!");

        Assert.NotNull(login);
        Assert.Contains("acme", login.Value.Org.Slug, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CrossOrgLogin_WithWrongCredentials_Fails()
    {
        var auth = CreateAuth();
        await auth.RegisterAsync("Org A", "a@a.com", "pass-a-123", "Admin A");
        await auth.RegisterAsync("Org B", "b@b.com", "pass-b-123", "Admin B");

        // Try to login as Org A user with Org B password
        var result = await auth.LoginAsync("a@a.com", "pass-b-123");
        Assert.Null(result);
    }

    // ── Approval Gate Tenant Isolation ─────────────────────────────────

    [Fact]
    public async Task ApprovalGate_CrossTenantRead_ReturnsNull()
    {
        var gov = new GovernanceService();
        var gate = await gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "tenant-A", "user-a", "reason");

        var crossRead = await gov.GetApprovalAsync(gate.Id, "tenant-B");
        Assert.Null(crossRead);

        var sameRead = await gov.GetApprovalAsync(gate.Id, "tenant-A");
        Assert.NotNull(sameRead);
    }

    [Fact]
    public async Task ApprovalGate_CrossTenantReview_Rejected()
    {
        var gov = new GovernanceService();
        var gate = await gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "tenant-A", "user-a", "reason");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            gov.ReviewApprovalAsync(gate.Id, "tenant-B", "attacker", "Admin", true, "hack"));
    }

    [Fact]
    public async Task PendingApprovals_FilterByTenant()
    {
        var gov = new GovernanceService();
        await gov.RequestApprovalAsync("workflow.cancel", "wf-1", "tenant-A", "u1", "r");
        await gov.RequestApprovalAsync("workflow.cancel", "wf-2", "tenant-B", "u2", "r");
        await gov.RequestApprovalAsync("workflow.cancel", "wf-3", "tenant-A", "u3", "r");

        var tenantA = await gov.ListPendingApprovalsAsync("tenant-A");
        var tenantB = await gov.ListPendingApprovalsAsync("tenant-B");

        Assert.Equal(2, tenantA.Count);
        Assert.Single(tenantB);
        Assert.All(tenantA, g => Assert.Equal("tenant-A", g.TenantId));
    }

    // ── Approval History Isolation ────────────────────────────────────

    [Fact]
    public async Task ApprovalHistory_CrossTenant_ReturnsEmpty()
    {
        var gov = new GovernanceService();
        var gate = await gov.RequestApprovalAsync(
            "workflow.cancel", "wf-1", "tenant-X", "user-a", "reason");
        await gov.ReviewApprovalAsync(gate.Id, "tenant-X", "user-b", "Admin", true, "ok");

        var crossHistory = await gov.GetApprovalHistoryAsync("tenant-Y", null, 50);
        Assert.Empty(crossHistory);

        var ownHistory = await gov.GetApprovalHistoryAsync("tenant-X", null, 50);
        Assert.Single(ownHistory);
    }

    // ── Multi-Tenant Context Isolation ────────────────────────────────

    [Fact]
    public async Task TenantContext_ConcurrentTenants_NoLeakage()
    {
        var ctx = new MultiTenantContext(
            Options.Create(new MultiTenantOptions { DefaultTenantId = "default" }));

        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
        {
            var tenantId = $"tenant-{i % 10}";
            using var scope = ctx.BeginTenantScope(tenantId);

            if (ctx.CurrentTenantId != tenantId)
                errors.Add($"Before: expected {tenantId}, got {ctx.CurrentTenantId}");

            await Task.Delay(Random.Shared.Next(1, 5));

            if (ctx.CurrentTenantId != tenantId)
                errors.Add($"After: expected {tenantId}, got {ctx.CurrentTenantId}");
        }));

        await Task.WhenAll(tasks);
        Assert.Empty(errors);
    }

    // ── Resource Governor Cross-Tenant Isolation ──────────────────────

    [Fact]
    public async Task ResourceGovernor_TenantALimit_DoesNotAffectTenantB()
    {
        var gov = new TenantResourceGovernor(
            Options.Create(new MultiTenantOptions { MaxConcurrentPlansPerTenant = 1 }));

        Assert.True(await gov.TryAcquirePlanningSlotAsync("tenant-A", CancellationToken.None));
        Assert.False(await gov.TryAcquirePlanningSlotAsync("tenant-A", CancellationToken.None));

        // Tenant B should be unaffected
        Assert.True(await gov.TryAcquirePlanningSlotAsync("tenant-B", CancellationToken.None));
    }
}
