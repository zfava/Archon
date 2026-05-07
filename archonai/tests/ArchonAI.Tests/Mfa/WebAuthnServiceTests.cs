using ArchonAI.Identity.Mfa;
using ArchonAI.Identity.Stores;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArchonAI.Tests.Mfa;

public sealed class WebAuthnServiceTests
{
    private readonly WebAuthnService _webAuthn;
    private readonly InMemoryMfaStore _store;

    public WebAuthnServiceTests()
    {
        _store = new InMemoryMfaStore();
        _webAuthn = new WebAuthnService(_store, NullLogger<WebAuthnService>.Instance);
    }

    [Fact]
    public async Task BeginRegistration_ReturnsValidOptions()
    {
        var userId = Guid.NewGuid();
        var options = await _webAuthn.BeginRegistrationAsync(userId, "user@test.com", "Test User");

        Assert.NotNull(options);
        Assert.NotEmpty(options.Challenge);
        Assert.Equal("archonai.io", options.RpId);
        Assert.Equal("ArchonAI", options.RpName);
        Assert.Equal("user@test.com", options.UserName);
        Assert.Equal("Test User", options.UserDisplayName);
        Assert.Empty(options.ExcludeCredentials);
        Assert.Equal(60000, options.Timeout);
    }

    [Fact]
    public async Task CompleteRegistration_StoresCredential()
    {
        var userId = Guid.NewGuid();
        byte[] credentialId = Guid.NewGuid().ToByteArray();
        byte[] publicKey = new byte[65]; // typical EC key length

        bool result = await _webAuthn.CompleteRegistrationAsync(
            userId, "My Security Key", credentialId, publicKey, 0);

        Assert.True(result);
        Assert.True(await _webAuthn.IsEnabledAsync(userId));
    }

    [Fact]
    public async Task CompleteRegistration_DuplicateCredentialId_Rejected()
    {
        var userId = Guid.NewGuid();
        byte[] credentialId = Guid.NewGuid().ToByteArray();
        byte[] publicKey = new byte[65];

        Assert.True(await _webAuthn.CompleteRegistrationAsync(
            userId, "Key 1", credentialId, publicKey, 0));

        // Same credential ID for different user should be rejected
        Assert.False(await _webAuthn.CompleteRegistrationAsync(
            Guid.NewGuid(), "Key 2", credentialId, publicKey, 0));
    }

    [Fact]
    public async Task BeginRegistration_ExcludesExistingCredentials()
    {
        var userId = Guid.NewGuid();
        byte[] credentialId = Guid.NewGuid().ToByteArray();

        await _webAuthn.CompleteRegistrationAsync(
            userId, "Key 1", credentialId, new byte[65], 0);

        var options = await _webAuthn.BeginRegistrationAsync(userId, "user@test.com", "Test");
        Assert.Single(options.ExcludeCredentials);
        Assert.Equal(Convert.ToBase64String(credentialId), options.ExcludeCredentials[0]);
    }

    [Fact]
    public async Task MultipleCredentials_PerUser()
    {
        var userId = Guid.NewGuid();

        for (int i = 0; i < 3; i++)
        {
            await _webAuthn.CompleteRegistrationAsync(
                userId, $"Key {i}", Guid.NewGuid().ToByteArray(), new byte[65], 0);
        }

        var creds = await _store.GetWebAuthnCredentialsAsync(userId);
        Assert.Equal(3, creds.Count);
    }

    [Fact]
    public async Task BeginAuthentication_NoCredentials_ReturnsNull()
    {
        var result = await _webAuthn.BeginAuthenticationAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task BeginAuthentication_WithCredentials_ReturnsOptions()
    {
        var userId = Guid.NewGuid();
        byte[] credentialId = Guid.NewGuid().ToByteArray();
        await _webAuthn.CompleteRegistrationAsync(
            userId, "Key 1", credentialId, new byte[65], 0);

        var options = await _webAuthn.BeginAuthenticationAsync(userId);
        Assert.NotNull(options);
        Assert.NotEmpty(options.Challenge);
        Assert.Single(options.AllowCredentials);
        Assert.Equal("archonai.io", options.RpId);
    }

    [Fact]
    public async Task CompleteAuthentication_ValidCredential_Succeeds()
    {
        var userId = Guid.NewGuid();
        byte[] credentialId = Guid.NewGuid().ToByteArray();
        await _webAuthn.CompleteRegistrationAsync(
            userId, "Key 1", credentialId, new byte[65], 0);

        bool result = await _webAuthn.CompleteAuthenticationAsync(userId, credentialId, 1);
        Assert.True(result);
    }

    [Fact]
    public async Task CompleteAuthentication_WrongUser_Fails()
    {
        var userId = Guid.NewGuid();
        byte[] credentialId = Guid.NewGuid().ToByteArray();
        await _webAuthn.CompleteRegistrationAsync(
            userId, "Key 1", credentialId, new byte[65], 0);

        bool result = await _webAuthn.CompleteAuthenticationAsync(Guid.NewGuid(), credentialId, 1);
        Assert.False(result);
    }

    [Fact]
    public async Task CompleteAuthentication_UnknownCredential_Fails()
    {
        var userId = Guid.NewGuid();
        bool result = await _webAuthn.CompleteAuthenticationAsync(
            userId, Guid.NewGuid().ToByteArray(), 1);
        Assert.False(result);
    }

    [Fact]
    public async Task CompleteAuthentication_CloneDetection_RejectsLowerSignCount()
    {
        var userId = Guid.NewGuid();
        byte[] credentialId = Guid.NewGuid().ToByteArray();
        await _webAuthn.CompleteRegistrationAsync(
            userId, "Key 1", credentialId, new byte[65], 0);

        // First auth with sign count 5
        Assert.True(await _webAuthn.CompleteAuthenticationAsync(userId, credentialId, 5));

        // Replay with sign count 3 (possible cloned key)
        Assert.False(await _webAuthn.CompleteAuthenticationAsync(userId, credentialId, 3));

        // Equal sign count also rejected
        Assert.False(await _webAuthn.CompleteAuthenticationAsync(userId, credentialId, 5));
    }

    [Fact]
    public async Task CompleteAuthentication_UpdatesSignCount()
    {
        var userId = Guid.NewGuid();
        byte[] credentialId = Guid.NewGuid().ToByteArray();
        await _webAuthn.CompleteRegistrationAsync(
            userId, "Key 1", credentialId, new byte[65], 0);

        Assert.True(await _webAuthn.CompleteAuthenticationAsync(userId, credentialId, 5));

        // Next auth must be > 5
        Assert.True(await _webAuthn.CompleteAuthenticationAsync(userId, credentialId, 6));
        Assert.False(await _webAuthn.CompleteAuthenticationAsync(userId, credentialId, 6));
    }

    [Fact]
    public async Task DeleteCredential_RemovesSpecificCredential()
    {
        var userId = Guid.NewGuid();
        byte[] cred1 = Guid.NewGuid().ToByteArray();
        byte[] cred2 = Guid.NewGuid().ToByteArray();

        await _webAuthn.CompleteRegistrationAsync(userId, "Key 1", cred1, new byte[65], 0);
        await _webAuthn.CompleteRegistrationAsync(userId, "Key 2", cred2, new byte[65], 0);

        var creds = await _store.GetWebAuthnCredentialsAsync(userId);
        Assert.Equal(2, creds.Count);

        Assert.True(await _webAuthn.DeleteCredentialAsync(userId, creds[0].Id));

        var remaining = await _store.GetWebAuthnCredentialsAsync(userId);
        Assert.Single(remaining);
    }

    [Fact]
    public async Task DeleteCredential_WrongUser_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        await _webAuthn.CompleteRegistrationAsync(
            userId, "Key 1", Guid.NewGuid().ToByteArray(), new byte[65], 0);

        var creds = await _store.GetWebAuthnCredentialsAsync(userId);
        Assert.False(await _webAuthn.DeleteCredentialAsync(Guid.NewGuid(), creds[0].Id));
    }

    [Fact]
    public async Task IsEnabled_NoCredentials_ReturnsFalse()
    {
        Assert.False(await _webAuthn.IsEnabledAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task IsEnabled_WithCredentials_ReturnsTrue()
    {
        var userId = Guid.NewGuid();
        await _webAuthn.CompleteRegistrationAsync(
            userId, "Key", Guid.NewGuid().ToByteArray(), new byte[65], 0);
        Assert.True(await _webAuthn.IsEnabledAsync(userId));
    }
}
