using ArchonAI.Core.Models.Identity;
using ArchonAI.Identity.Stores;

namespace ArchonAI.Tests.Mfa;

public sealed class MfaPolicyTests
{
    private readonly InMemoryMfaStore _store;

    public MfaPolicyTests()
    {
        _store = new InMemoryMfaStore();
    }

    [Fact]
    public async Task GetPolicy_NoneSet_ReturnsNull()
    {
        var policy = await _store.GetMfaPolicyAsync(Guid.NewGuid());
        Assert.Null(policy);
    }

    [Fact]
    public async Task UpsertPolicy_CreatesNew()
    {
        var orgId = Guid.NewGuid();
        var policy = new MfaPolicy(orgId, MfaPolicyMode.Required, DateTimeOffset.UtcNow);

        await _store.UpsertMfaPolicyAsync(policy);
        var retrieved = await _store.GetMfaPolicyAsync(orgId);

        Assert.NotNull(retrieved);
        Assert.Equal(MfaPolicyMode.Required, retrieved.Mode);
    }

    [Fact]
    public async Task UpsertPolicy_UpdatesExisting()
    {
        var orgId = Guid.NewGuid();
        await _store.UpsertMfaPolicyAsync(new MfaPolicy(orgId, MfaPolicyMode.Optional, DateTimeOffset.UtcNow));
        await _store.UpsertMfaPolicyAsync(new MfaPolicy(orgId, MfaPolicyMode.Required, DateTimeOffset.UtcNow));

        var policy = await _store.GetMfaPolicyAsync(orgId);
        Assert.Equal(MfaPolicyMode.Required, policy!.Mode);
    }

    [Fact]
    public async Task Policy_IsolatedPerOrg()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        await _store.UpsertMfaPolicyAsync(new MfaPolicy(orgA, MfaPolicyMode.Required, DateTimeOffset.UtcNow));
        await _store.UpsertMfaPolicyAsync(new MfaPolicy(orgB, MfaPolicyMode.Disabled, DateTimeOffset.UtcNow));

        Assert.Equal(MfaPolicyMode.Required, (await _store.GetMfaPolicyAsync(orgA))!.Mode);
        Assert.Equal(MfaPolicyMode.Disabled, (await _store.GetMfaPolicyAsync(orgB))!.Mode);
    }
}
