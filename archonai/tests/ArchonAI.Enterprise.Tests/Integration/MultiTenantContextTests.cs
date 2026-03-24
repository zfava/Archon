using ArchonAI.MultiTenant;
using Microsoft.Extensions.Options;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Integration tests for multi-tenant context: scope isolation, nesting behavior,
/// async flow preservation, and default tenant fallback.
/// </summary>
public sealed class MultiTenantContextTests
{
    private MultiTenantContext CreateContext(string defaultTenant = "default-org") =>
        new(Options.Create(new MultiTenantOptions { DefaultTenantId = defaultTenant }));

    // ── Scope Lifecycle ───────────────────────────────────────────────

    [Fact]
    public void DefaultTenant_IsReturned_WhenNoScope()
    {
        var ctx = CreateContext("acme-default");
        Assert.Equal("acme-default", ctx.CurrentTenantId);
    }

    [Fact]
    public void BeginScope_ChangesTenantId()
    {
        var ctx = CreateContext();
        using var scope = ctx.BeginTenantScope("tenant-123");
        Assert.Equal("tenant-123", ctx.CurrentTenantId);
    }

    [Fact]
    public void ScopeDispose_RestoresPreviousTenant()
    {
        var ctx = CreateContext("original");
        using (ctx.BeginTenantScope("temporary"))
        {
            Assert.Equal("temporary", ctx.CurrentTenantId);
        }
        Assert.Equal("original", ctx.CurrentTenantId);
    }

    // ── Nested Scopes ─────────────────────────────────────────────────

    [Fact]
    public void NestedScopes_RestoreCorrectly()
    {
        var ctx = CreateContext("root");

        using (ctx.BeginTenantScope("level-1"))
        {
            Assert.Equal("level-1", ctx.CurrentTenantId);

            using (ctx.BeginTenantScope("level-2"))
            {
                Assert.Equal("level-2", ctx.CurrentTenantId);
            }

            Assert.Equal("level-1", ctx.CurrentTenantId);
        }

        Assert.Equal("root", ctx.CurrentTenantId);
    }

    // ── Empty/Whitespace Tenant ───────────────────────────────────────

    [Fact]
    public void EmptyTenantId_FallsBackToDefault()
    {
        var ctx = CreateContext("fallback-org");
        using var scope = ctx.BeginTenantScope("");
        Assert.Equal("fallback-org", ctx.CurrentTenantId);
    }

    [Fact]
    public void WhitespaceTenantId_FallsBackToDefault()
    {
        var ctx = CreateContext("fallback-org");
        using var scope = ctx.BeginTenantScope("   ");
        Assert.Equal("fallback-org", ctx.CurrentTenantId);
    }

    // ── Async Flow Isolation ──────────────────────────────────────────

    [Fact]
    public async Task AsyncScope_PreservesAcrossAwaits()
    {
        var ctx = CreateContext();
        using var scope = ctx.BeginTenantScope("async-tenant");

        Assert.Equal("async-tenant", ctx.CurrentTenantId);
        await Task.Delay(10);
        Assert.Equal("async-tenant", ctx.CurrentTenantId);
    }

    [Fact]
    public async Task ConcurrentScopes_AreIsolated()
    {
        var ctx = CreateContext();
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
        {
            var tenantId = $"tenant-{i}";
            using var scope = ctx.BeginTenantScope(tenantId);

            // Verify isolation across async yield
            Assert.Equal(tenantId, ctx.CurrentTenantId);
            await Task.Delay(Random.Shared.Next(1, 10));

            if (ctx.CurrentTenantId != tenantId)
                errors.Add($"Expected {tenantId}, got {ctx.CurrentTenantId}");
        }));

        await Task.WhenAll(tasks);
        Assert.Empty(errors);
    }

    // ── Trimming ──────────────────────────────────────────────────────

    [Fact]
    public void TenantId_IsTrimmed()
    {
        var ctx = CreateContext();
        using var scope = ctx.BeginTenantScope("  spaced-tenant  ");
        Assert.Equal("spaced-tenant", ctx.CurrentTenantId);
    }
}
