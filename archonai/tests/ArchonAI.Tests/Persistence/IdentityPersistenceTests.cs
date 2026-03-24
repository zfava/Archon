using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using ArchonAI.Identity;
using ArchonAI.Identity.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Tests.Persistence;

public sealed class IdentityPersistenceTests : IDisposable
{
    private readonly string _tempDir;

    public IdentityPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"archon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private DurableIdentityStore CreateStore() =>
        new(Options.Create(new IdentityOptions { PersistencePath = Path.Combine(_tempDir, "identity.json") }),
            NullLogger<DurableIdentityStore>.Instance);

    [Fact]
    public async global::System.Threading.Tasks.Task Users_SurviveRestart()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var user = new UserIdentity(userId, "alice@acme.com", "Alice", "hash", orgId, "Admin", true,
            DateTimeOffset.UtcNow, null);

        using (var store1 = CreateStore())
        {
            await ((IUserStore)store1).CreateAsync(user);
            await store1.FlushAsync();
        }

        using var store2 = CreateStore();
        var loaded = await ((IUserStore)store2).GetByIdAsync(userId);

        Assert.NotNull(loaded);
        Assert.Equal("alice@acme.com", loaded!.Email);
        Assert.Equal("Admin", loaded.Role);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Organizations_SurviveRestart()
    {
        var orgId = Guid.NewGuid();
        var org = new Organization(orgId, "Acme Corp", "acme-corp", true, DateTimeOffset.UtcNow);

        using (var store1 = CreateStore())
        {
            await ((IOrganizationStore)store1).CreateAsync(org);
            await store1.FlushAsync();
        }

        using var store2 = CreateStore();
        var loaded = await ((IOrganizationStore)store2).GetByIdAsync(orgId);

        Assert.NotNull(loaded);
        Assert.Equal("Acme Corp", loaded!.Name);
        Assert.Equal("acme-corp", loaded.Slug);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Memberships_SurviveRestart()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var membership = new Membership(Guid.NewGuid(), userId, orgId, "Member", DateTimeOffset.UtcNow);

        using (var store1 = CreateStore())
        {
            await ((IMembershipStore)store1).CreateAsync(membership);
            await store1.FlushAsync();
        }

        using var store2 = CreateStore();
        var loaded = await ((IMembershipStore)store2).GetAsync(userId, orgId);

        Assert.NotNull(loaded);
        Assert.Equal("Member", loaded!.Role);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshTokens_SurviveRestart()
    {
        var token = new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "hash-abc",
            DateTimeOffset.UtcNow.AddDays(7), DateTimeOffset.UtcNow, false);

        using (var store1 = CreateStore())
        {
            await ((IRefreshTokenStore)store1).CreateAsync(token);
            await store1.FlushAsync();
        }

        using var store2 = CreateStore();
        var loaded = await ((IRefreshTokenStore)store2).GetByHashAsync("hash-abc");

        Assert.NotNull(loaded);
        Assert.False(loaded!.IsRevoked);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task InviteTokens_SurviveRestart()
    {
        var invite = new InviteToken(Guid.NewGuid(), Guid.NewGuid(), "bob@acme.com", "Viewer",
            "invite-hash", DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow, false);

        using (var store1 = CreateStore())
        {
            await ((IInviteTokenStore)store1).CreateAsync(invite);
            await store1.FlushAsync();
        }

        using var store2 = CreateStore();
        var loaded = await ((IInviteTokenStore)store2).GetByHashAsync("invite-hash");

        Assert.NotNull(loaded);
        Assert.Equal("bob@acme.com", loaded!.Email);
    }
}
