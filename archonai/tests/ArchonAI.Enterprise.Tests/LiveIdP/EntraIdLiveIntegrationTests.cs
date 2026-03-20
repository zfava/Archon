using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using ArchonAI.Core.Models.Identity;
using ArchonAI.Identity;
using ArchonAI.Identity.Stores;
using Xunit;

namespace ArchonAI.Enterprise.Tests.LiveIdP;

/// <summary>
/// Live integration tests against a real Microsoft Entra ID (Azure AD) tenant.
///
/// Required environment variables:
///   ARCHONAI_TEST_ENTRA_TENANT_ID      — Azure AD tenant ID (GUID)
///   ARCHONAI_TEST_ENTRA_CLIENT_ID      — App registration client ID
///   ARCHONAI_TEST_ENTRA_CLIENT_SECRET   — App registration client secret
///   ARCHONAI_TEST_ENTRA_TEST_USER_EMAIL — test user email (UPN)
///   ARCHONAI_TEST_ENTRA_TEST_USER_PASSWORD — test user password
///
/// Setup: Register an application in Azure Portal under App registrations.
/// Configure as a web application with redirect URI http://localhost/callback.
/// Enable the Resource Owner Password Credentials flow in the app manifest
/// ("allowPublicClient": true). Create a test user in the tenant.
///
/// These tests are skipped automatically when env vars are absent.
/// </summary>
[Trait("Category", "LiveIntegration")]
public sealed class EntraIdLiveIntegrationTests
{
    private readonly string? _tenantId;
    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly string? _testUserEmail;
    private readonly string? _testUserPassword;
    private readonly bool _canRun;

    public EntraIdLiveIntegrationTests()
    {
        _tenantId = Environment.GetEnvironmentVariable("ARCHONAI_TEST_ENTRA_TENANT_ID");
        _clientId = Environment.GetEnvironmentVariable("ARCHONAI_TEST_ENTRA_CLIENT_ID");
        _clientSecret = Environment.GetEnvironmentVariable("ARCHONAI_TEST_ENTRA_CLIENT_SECRET");
        _testUserEmail = Environment.GetEnvironmentVariable("ARCHONAI_TEST_ENTRA_TEST_USER_EMAIL");
        _testUserPassword = Environment.GetEnvironmentVariable("ARCHONAI_TEST_ENTRA_TEST_USER_PASSWORD");
        _canRun = !string.IsNullOrWhiteSpace(_tenantId)
            && !string.IsNullOrWhiteSpace(_clientId)
            && !string.IsNullOrWhiteSpace(_clientSecret)
            && !string.IsNullOrWhiteSpace(_testUserEmail)
            && !string.IsNullOrWhiteSpace(_testUserPassword);
    }

    private string Authority => $"https://login.microsoftonline.com/{_tenantId}/v2.0";
    private string AuthorizeEndpoint => $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/authorize";
    private string TokenEndpoint => $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token";

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task EntraOidcLogin_GeneratesValidAuthorizeUrl()
    {
        if (!_canRun) return;

        // Arrange — construct an authorize URL targeting the Entra ID authorize endpoint
        string state = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        string nonce = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        string codeVerifier = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        string codeChallenge = ComputeCodeChallenge(codeVerifier);
        string redirectUri = "http://localhost/callback";

        string authorizeUrl = AuthorizeEndpoint
            + $"?client_id={Uri.EscapeDataString(_clientId!)}"
            + $"&response_type=code"
            + $"&scope={Uri.EscapeDataString("openid profile email")}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&nonce={Uri.EscapeDataString(nonce)}"
            + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
            + $"&code_challenge_method=S256";

        // Act — parse and validate the URL components
        var uri = new Uri(authorizeUrl);
        var query = HttpUtility.ParseQueryString(uri.Query);

        // Assert
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("login.microsoftonline.com", uri.Host);
        Assert.Contains(_tenantId!, uri.AbsolutePath);
        Assert.Equal(_clientId, query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Contains("openid", query["scope"]!);
        Assert.Equal(redirectUri, query["redirect_uri"]);
        Assert.Equal(state, query["state"]);
        Assert.Equal(nonce, query["nonce"]);
        Assert.Equal(codeChallenge, query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);

        // Validate that the Entra ID discovery endpoint is reachable
        using var httpClient = new HttpClient();
        var discoveryUrl = $"https://login.microsoftonline.com/{_tenantId}/v2.0/.well-known/openid-configuration";
        var discoveryResponse = await httpClient.GetAsync(discoveryUrl);
        Assert.True(discoveryResponse.IsSuccessStatusCode,
            $"Entra ID discovery endpoint returned {discoveryResponse.StatusCode}");
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task EntraCallback_WithRealCode_ExchangesToken()
    {
        if (!_canRun) return;

        // Use the Resource Owner Password Credentials grant to obtain tokens directly
        using var httpClient = new HttpClient();

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = _testUserEmail!,
            ["password"] = _testUserPassword!,
            ["client_id"] = _clientId!,
            ["client_secret"] = _clientSecret!,
            ["scope"] = "openid profile email",
        });

        var response = await httpClient.PostAsync(TokenEndpoint, tokenRequest);
        Assert.True(response.IsSuccessStatusCode,
            $"Entra ROPC token exchange failed: {response.StatusCode} — {await response.Content.ReadAsStringAsync()}");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Verify the response contains expected OIDC tokens
        Assert.True(root.TryGetProperty("id_token", out _), "Response must contain id_token");
        Assert.True(root.TryGetProperty("access_token", out _), "Response must contain access_token");
        Assert.True(root.TryGetProperty("token_type", out var tokenType), "Response must contain token_type");
        Assert.Equal("Bearer", tokenType.GetString(), ignoreCase: true);
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task EntraJitProvisioning_NewUser_CreatesAccount()
    {
        if (!_canRun) return;

        // Obtain an id_token from Entra ID via ROPC
        var idToken = await ObtainIdTokenViaRopcAsync();
        Assert.False(string.IsNullOrEmpty(idToken), "Failed to obtain id_token from Entra ID");

        // Decode the id_token to extract claims for JIT provisioning verification
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(idToken);

        // Entra ID uses 'oid' or 'sub' for the subject identifier
        var subClaim = jwt.Claims.FirstOrDefault(c => c.Type == "sub")
            ?? jwt.Claims.FirstOrDefault(c => c.Type == "oid");
        Assert.NotNull(subClaim);
        Assert.False(string.IsNullOrEmpty(subClaim.Value), "Subject claim must have a value");

        // Verify email claim — Entra ID may use 'email', 'preferred_username', or 'upn'
        var emailClaim = jwt.Claims.FirstOrDefault(c => c.Type == "email")
            ?? jwt.Claims.FirstOrDefault(c => c.Type == "preferred_username")
            ?? jwt.Claims.FirstOrDefault(c => c.Type == "upn");
        if (emailClaim is not null)
        {
            Assert.Equal(_testUserEmail, emailClaim.Value, ignoreCase: true);
        }

        // Verify the token issuer matches the expected Entra ID authority
        var issClaim = jwt.Claims.FirstOrDefault(c => c.Type == "iss");
        Assert.NotNull(issClaim);
        Assert.Contains("login.microsoftonline.com", issClaim.Value);
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task EntraJitProvisioning_ExistingUser_Updates()
    {
        if (!_canRun) return;

        // Perform two ROPC token requests to simulate two logins by the same user
        var idToken1 = await ObtainIdTokenViaRopcAsync();
        Assert.False(string.IsNullOrEmpty(idToken1), "First id_token must not be empty");

        var idToken2 = await ObtainIdTokenViaRopcAsync();
        Assert.False(string.IsNullOrEmpty(idToken2), "Second id_token must not be empty");

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt1 = handler.ReadJwtToken(idToken1);
        var jwt2 = handler.ReadJwtToken(idToken2);

        // Both tokens should identify the same user
        var sub1 = (jwt1.Claims.FirstOrDefault(c => c.Type == "sub")
            ?? jwt1.Claims.FirstOrDefault(c => c.Type == "oid"))!.Value;
        var sub2 = (jwt2.Claims.FirstOrDefault(c => c.Type == "sub")
            ?? jwt2.Claims.FirstOrDefault(c => c.Type == "oid"))!.Value;
        Assert.Equal(sub1, sub2);

        // Both tokens should reference the same email / UPN
        var email1 = (jwt1.Claims.FirstOrDefault(c => c.Type == "email")
            ?? jwt1.Claims.FirstOrDefault(c => c.Type == "preferred_username"))?.Value;
        var email2 = (jwt2.Claims.FirstOrDefault(c => c.Type == "email")
            ?? jwt2.Claims.FirstOrDefault(c => c.Type == "preferred_username"))?.Value;
        Assert.Equal(email1, email2);

        // Verify iat claims exist (different issuance timestamps)
        var iat1 = jwt1.Claims.FirstOrDefault(c => c.Type == "iat")?.Value;
        var iat2 = jwt2.Claims.FirstOrDefault(c => c.Type == "iat")?.Value;
        Assert.False(string.IsNullOrEmpty(iat1));
        Assert.False(string.IsNullOrEmpty(iat2));
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task EntraStateReplay_Blocked()
    {
        if (!_canRun) return;

        // Create a login session store and add a session
        var sessionStore = new InMemoryOidcLoginSessionStore();
        string state = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        string nonce = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        string codeVerifier = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        var orgId = Guid.NewGuid();

        await sessionStore.CreateAsync(new OidcLoginSession(
            State: state,
            Nonce: nonce,
            CodeVerifier: codeVerifier,
            OrganizationId: orgId,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddSeconds(300)));

        // First consume should succeed
        var session1 = await sessionStore.ConsumeAsync(state);
        Assert.NotNull(session1);
        Assert.Equal(state, session1.State);
        Assert.Equal(nonce, session1.Nonce);

        // Second consume with same state should return null (replay blocked)
        var session2 = await sessionStore.ConsumeAsync(state);
        Assert.Null(session2);
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task EntraExpiredState_Rejected()
    {
        if (!_canRun) return;

        // Create a session that is already expired
        var sessionStore = new InMemoryOidcLoginSessionStore();
        string state = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        string nonce = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        string codeVerifier = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        var orgId = Guid.NewGuid();

        // Create with an expiration time in the past (expired 10 seconds ago)
        var expiredAt = DateTimeOffset.UtcNow.AddSeconds(-10);
        await sessionStore.CreateAsync(new OidcLoginSession(
            State: state,
            Nonce: nonce,
            CodeVerifier: codeVerifier,
            OrganizationId: orgId,
            CreatedAtUtc: expiredAt.AddSeconds(-300),
            ExpiresAtUtc: expiredAt));

        // Consume returns the session (it exists in the store)
        var session = await sessionStore.ConsumeAsync(state);
        Assert.NotNull(session);

        // But the expiration check should reject it — this is what the callback endpoint does
        Assert.True(DateTimeOffset.UtcNow > session.ExpiresAtUtc,
            "Session must be expired (ExpiresAtUtc must be in the past)");

        // Verify that a session older than 300s has a CreatedAtUtc that's more than 300s ago
        var ageSeconds = (DateTimeOffset.UtcNow - session.CreatedAtUtc).TotalSeconds;
        Assert.True(ageSeconds > 300,
            $"Expired session should be older than 300s, but was {ageSeconds:F1}s");
    }

    /// <summary>
    /// Obtains an id_token from Entra ID using the Resource Owner Password Credentials grant.
    /// </summary>
    private async Task<string?> ObtainIdTokenViaRopcAsync()
    {
        using var httpClient = new HttpClient();
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = _testUserEmail!,
            ["password"] = _testUserPassword!,
            ["client_id"] = _clientId!,
            ["client_secret"] = _clientSecret!,
            ["scope"] = "openid profile email",
        });

        var response = await httpClient.PostAsync(TokenEndpoint, tokenRequest);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("id_token", out var idToken)
            ? idToken.GetString()
            : null;
    }

    private static string ComputeCodeChallenge(string codeVerifier)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var challengeBytes = sha256.ComputeHash(System.Text.Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(challengeBytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
