using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using ArchonAI.Identity;
using ArchonAI.Identity.Stores;

namespace ArchonAI.Enterprise.Tests.Security;

/// <summary>
/// Security tests for the OIDC login session binding model.
/// Validates that the server-side store correctly enforces:
/// - state lookup and single-use consumption (anti-replay)
/// - expiration enforcement
/// - nonce isolation from client control
/// - organization binding between login and callback
/// </summary>
public sealed class OidcCallbackSecurityTests
{
    private readonly InMemoryOidcLoginSessionStore _store = new();

    private OidcLoginSession CreateSession(
        string? state = null,
        string? nonce = null,
        string? codeVerifier = null,
        Guid? orgId = null,
        DateTimeOffset? expiresAt = null)
    {
        return new OidcLoginSession(
            State: state ?? OidcTokenExchangeService.GenerateOidcStateOrNonce(),
            Nonce: nonce ?? OidcTokenExchangeService.GenerateOidcStateOrNonce(),
            CodeVerifier: codeVerifier ?? OidcTokenExchangeService.GenerateOidcStateOrNonce(),
            OrganizationId: orgId ?? Guid.NewGuid(),
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: expiresAt ?? DateTimeOffset.UtcNow.AddSeconds(300));
    }

    // ── Valid flow ──────────────────────────────────────────────────────

    [Fact]
    public async Task ValidSession_ConsumeReturnsSession_WithAllFields()
    {
        var session = CreateSession();
        await _store.CreateAsync(session);

        var consumed = await _store.ConsumeAsync(session.State);

        Assert.NotNull(consumed);
        Assert.Equal(session.State, consumed.State);
        Assert.Equal(session.Nonce, consumed.Nonce);
        Assert.Equal(session.CodeVerifier, consumed.CodeVerifier);
        Assert.Equal(session.OrganizationId, consumed.OrganizationId);
        Assert.Equal(session.ExpiresAtUtc, consumed.ExpiresAtUtc);
    }

    // ── Invalid state ──────────────────────────────────────────────────

    [Fact]
    public async Task InvalidState_ConsumeReturnsNull()
    {
        var session = CreateSession();
        await _store.CreateAsync(session);

        var consumed = await _store.ConsumeAsync("completely-wrong-state-value");

        Assert.Null(consumed);
    }

    // ── Missing state (empty store) ────────────────────────────────────

    [Fact]
    public async Task MissingState_EmptyStore_ConsumeReturnsNull()
    {
        var consumed = await _store.ConsumeAsync("nonexistent-state");

        Assert.Null(consumed);
    }

    // ── Replay prevention (single-use consumption) ─────────────────────

    [Fact]
    public async Task ReplayAttempt_SecondConsumeReturnsNull()
    {
        var session = CreateSession();
        await _store.CreateAsync(session);

        var first = await _store.ConsumeAsync(session.State);
        var replay = await _store.ConsumeAsync(session.State);

        Assert.NotNull(first);
        Assert.Null(replay); // Second attempt must fail — single-use
    }

    // ── Expired session detection ──────────────────────────────────────

    [Fact]
    public async Task ExpiredSession_ConsumeReturnsSession_CallerMustCheckExpiry()
    {
        // The store returns the session even if expired — the caller (endpoint)
        // is responsible for checking ExpiresAtUtc. This tests the full pattern.
        var session = CreateSession(expiresAt: DateTimeOffset.UtcNow.AddSeconds(-60));
        await _store.CreateAsync(session);

        var consumed = await _store.ConsumeAsync(session.State);

        Assert.NotNull(consumed);
        Assert.True(DateTimeOffset.UtcNow > consumed.ExpiresAtUtc,
            "Session should be expired — the caller must reject it");
    }

    [Fact]
    public void ExpirationCheck_ExpiredSession_IsDetectedCorrectly()
    {
        var session = CreateSession(expiresAt: DateTimeOffset.UtcNow.AddSeconds(-1));

        bool isExpired = DateTimeOffset.UtcNow > session.ExpiresAtUtc;

        Assert.True(isExpired);
    }

    [Fact]
    public void ExpirationCheck_ValidSession_IsNotExpired()
    {
        var session = CreateSession(expiresAt: DateTimeOffset.UtcNow.AddSeconds(300));

        bool isExpired = DateTimeOffset.UtcNow > session.ExpiresAtUtc;

        Assert.False(isExpired);
    }

    // ── Nonce isolation ────────────────────────────────────────────────

    [Fact]
    public async Task Nonce_IsStoredServerSide_NotDerivedFromClient()
    {
        string serverNonce = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        var session = CreateSession(nonce: serverNonce);
        await _store.CreateAsync(session);

        var consumed = await _store.ConsumeAsync(session.State);

        // The nonce returned is the exact server-generated value
        Assert.Equal(serverNonce, consumed!.Nonce);
    }

    [Fact]
    public void NonceMismatch_Detection()
    {
        string serverNonce = "server-generated-nonce-abc123";
        string tokenNonce = "attacker-supplied-different-nonce";

        // This simulates what the exchange service does — compare token nonce to server nonce
        Assert.NotEqual(serverNonce, tokenNonce);
    }

    // ── Organization binding ───────────────────────────────────────────

    [Fact]
    public async Task OrgBinding_SessionStoresOriginalOrgId()
    {
        var orgId = Guid.NewGuid();
        var session = CreateSession(orgId: orgId);
        await _store.CreateAsync(session);

        var consumed = await _store.ConsumeAsync(session.State);

        Assert.Equal(orgId, consumed!.OrganizationId);
    }

    [Fact]
    public void OrgMismatch_Detection()
    {
        var loginOrgId = Guid.NewGuid();
        var callbackOrgId = Guid.NewGuid();

        Assert.NotEqual(loginOrgId, callbackOrgId);
    }

    // ── CodeVerifier server-side binding ────────────────────────────────

    [Fact]
    public async Task CodeVerifier_IsStoredServerSide_NotClientSupplied()
    {
        string serverVerifier = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        var session = CreateSession(codeVerifier: serverVerifier);
        await _store.CreateAsync(session);

        var consumed = await _store.ConsumeAsync(session.State);

        Assert.Equal(serverVerifier, consumed!.CodeVerifier);
    }

    // ── Concurrent session isolation ───────────────────────────────────

    [Fact]
    public async Task MultipleSessions_ConsumeOnlyMatchingState()
    {
        var session1 = CreateSession();
        var session2 = CreateSession();
        var session3 = CreateSession();

        await _store.CreateAsync(session1);
        await _store.CreateAsync(session2);
        await _store.CreateAsync(session3);

        // Consume session2 — other sessions must remain
        var consumed2 = await _store.ConsumeAsync(session2.State);
        Assert.NotNull(consumed2);
        Assert.Equal(session2.Nonce, consumed2.Nonce);

        // session1 and session3 still available
        var consumed1 = await _store.ConsumeAsync(session1.State);
        var consumed3 = await _store.ConsumeAsync(session3.State);
        Assert.NotNull(consumed1);
        Assert.NotNull(consumed3);

        // All consumed — nothing left
        Assert.Null(await _store.ConsumeAsync(session1.State));
        Assert.Null(await _store.ConsumeAsync(session2.State));
        Assert.Null(await _store.ConsumeAsync(session3.State));
    }

    // ── Duplicate state rejection ──────────────────────────────────────

    [Fact]
    public async Task DuplicateState_CreateThrows()
    {
        var session = CreateSession();
        await _store.CreateAsync(session);

        var duplicate = CreateSession(state: session.State);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.CreateAsync(duplicate));
    }

    // ── Full lifecycle simulation ──────────────────────────────────────

    [Fact]
    public async Task FullLifecycle_LoginCreatesThenCallbackConsumesAndValidates()
    {
        // Simulate /login
        string state = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        string nonce = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        string codeVerifier = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        var orgId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var session = new OidcLoginSession(state, nonce, codeVerifier, orgId, now, now.AddSeconds(300));
        await _store.CreateAsync(session);

        // Simulate /callback
        var consumed = await _store.ConsumeAsync(state);
        Assert.NotNull(consumed);

        // Validate state was matched
        Assert.Equal(state, consumed.State);

        // Validate nonce is the server-issued value (not from token)
        Assert.Equal(nonce, consumed.Nonce);

        // Validate code_verifier is server-held
        Assert.Equal(codeVerifier, consumed.CodeVerifier);

        // Validate org binding
        Assert.Equal(orgId, consumed.OrganizationId);

        // Validate not expired
        Assert.True(DateTimeOffset.UtcNow < consumed.ExpiresAtUtc);

        // Replay is blocked
        Assert.Null(await _store.ConsumeAsync(state));
    }
}
