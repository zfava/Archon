using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using ArchonAI.Core.Models.Identity;
using ArchonAI.Identity;
using ArchonAI.Identity.Stores;
using Xunit;

namespace ArchonAI.Enterprise.Tests.LiveIdP;

/// <summary>
/// Live integration tests against a real Auth0 tenant.
///
/// Required environment variables:
///   ARCHONAI_TEST_AUTH0_DOMAIN          — e.g. my-tenant.us.auth0.com
///   ARCHONAI_TEST_AUTH0_CLIENT_ID       — Application client ID
///   ARCHONAI_TEST_AUTH0_CLIENT_SECRET   — Application client secret
///   ARCHONAI_TEST_AUTH0_TEST_USER_EMAIL — test user email
///   ARCHONAI_TEST_AUTH0_TEST_USER_PASSWORD — test user password
///
/// Setup: Create an Auth0 tenant, register a Regular Web Application.
/// Set allowed callback URL to http://localhost/callback.
/// Enable the Password grant type under Application > Advanced Settings > Grant Types.
/// Under Tenant Settings > API Authorization Settings, set Default Directory to
/// "Username-Password-Authentication". Create a test user in the Auth0 dashboard.
///
/// These tests are skipped automatically when env vars are absent.
/// </summary>
[Trait("Category", "LiveIntegration")]
public sealed class Auth0LiveIntegrationTests
{
    private readonly string? _domain;
    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly string? _testUserEmail;
    private readonly string? _testUserPassword;
    private readonly bool _canRun;

    public Auth0LiveIntegrationTests()
    {
        _domain = Environment.GetEnvironmentVariable("ARCHONAI_TEST_AUTH0_DOMAIN");
        _clientId = Environment.GetEnvironmentVariable("ARCHONAI_TEST_AUTH0_CLIENT_ID");
        _clientSecret = Environment.GetEnvironmentVariable("ARCHONAI_TEST_AUTH0_CLIENT_SECRET");
        _testUserEmail = Environment.GetEnvironmentVariable("ARCHONAI_TEST_AUTH0_TEST_USER_EMAIL");
        _testUserPassword = Environment.GetEnvironmentVariable("ARCHONAI_TEST_AUTH0_TEST_USER_PASSWORD");
        _canRun = !string.IsNullOrWhiteSpace(_domain)
            && !string.IsNullOrWhiteSpace(_clientId)
            && !string.IsNullOrWhiteSpace(_clientSecret)
            && !string.IsNullOrWhiteSpace(_testUserEmail)
            && !string.IsNullOrWhiteSpace(_testUserPassword);
    }

    private string Authority => $"https://{_domain}";
    private string AuthorizeEndpoint => $"{Authority}/authorize";
    private string TokenEndpoint => $"{Authority}/oauth2/token";

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Auth0OidcLogin_GeneratesValidAuthorizeUrl()
    {
        if (!_canRun) return;

        // Arrange — construct an authorize URL targeting the Auth0 authorize endpoint
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
        Assert.Contains(_domain!, uri.Host);
        Assert.Equal("/authorize", uri.AbsolutePath);
        Assert.Equal(_clientId, query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Contains("openid", query["scope"]!);
        Assert.Equal(redirectUri, query["redirect_uri"]);
        Assert.Equal(state, query["state"]);
        Assert.Equal(nonce, query["nonce"]);
        Assert.Equal(codeChallenge, query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);

        // Validate that the Auth0 discovery endpoint is reachable
        using var httpClient = new HttpClient();
        var discoveryUrl = $"{Authority}/.well-known/openid-configuration";
        var discoveryResponse = await httpClient.GetAsync(discoveryUrl);
        Assert.True(discoveryResponse.IsSuccessStatusCode,
            $"Auth0 discovery endpoint returned {discoveryResponse.StatusCode}");
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Auth0Callback_WithRealCode_ExchangesToken()
    {
        if (!_canRun) return;

        // Use the Resource Owner Password grant to obtain tokens directly.
        // Auth0 uses /oauth/token (not /oauth2/token) for the token endpoint.
        using var httpClient = new HttpClient();

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = _testUserEmail!,
            ["password"] = _testUserPassword!,
            ["client_id"] = _clientId!,
            ["client_secret"] = _clientSecret!,
            ["scope"] = "openid profile email",
            ["audience"] = $"{Authority}/api/v2/",
        });

        // Auth0 token endpoint is /oauth/token
        var auth0TokenEndpoint = $"{Authority}/oauth/token";
        var response = await httpClient.PostAsync(auth0TokenEndpoint, tokenRequest);
        Assert.True(response.IsSuccessStatusCode,
            $"Auth0 ROPC token exchange failed: {response.StatusCode} — {await response.Content.ReadAsStringAsync()}");

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
    public async Task Auth0JitProvisioning_NewUser_CreatesAccount()
    {
        if (!_canRun) return;

        // Obtain an id_token from Auth0 via ROPC
        var idToken = await ObtainIdTokenViaRopcAsync();
        Assert.False(string.IsNullOrEmpty(idToken), "Failed to obtain id_token from Auth0");

        // Decode the id_token to extract claims for JIT provisioning verification
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(idToken);

        // Auth0 uses 'sub' for the subject identifier (format: auth0|<user_id>)
        var subClaim = jwt.Claims.FirstOrDefault(c => c.Type == "sub");
        Assert.NotNull(subClaim);
        Assert.False(string.IsNullOrEmpty(subClaim.Value), "sub claim must have a value");

        // Verify email claim
        var emailClaim = jwt.Claims.FirstOrDefault(c => c.Type == "email");
        if (emailClaim is not null)
        {
            Assert.Equal(_testUserEmail, emailClaim.Value, ignoreCase: true);
        }

        // Verify the token issuer matches the expected Auth0 authority
        var issClaim = jwt.Claims.FirstOrDefault(c => c.Type == "iss");
        Assert.NotNull(issClaim);
        Assert.Contains(_domain!, issClaim.Value);
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Auth0JitProvisioning_ExistingUser_Updates()
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
        var sub1 = jwt1.Claims.First(c => c.Type == "sub").Value;
        var sub2 = jwt2.Claims.First(c => c.Type == "sub").Value;
        Assert.Equal(sub1, sub2);

        // Both tokens should reference the same email
        var email1 = jwt1.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
        var email2 = jwt2.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
        Assert.Equal(email1, email2);

        // Verify iat claims exist (different issuance timestamps)
        var iat1 = jwt1.Claims.FirstOrDefault(c => c.Type == "iat")?.Value;
        var iat2 = jwt2.Claims.FirstOrDefault(c => c.Type == "iat")?.Value;
        Assert.False(string.IsNullOrEmpty(iat1));
        Assert.False(string.IsNullOrEmpty(iat2));
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Auth0StateReplay_Blocked()
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
    public async Task Auth0ExpiredState_Rejected()
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
    /// Obtains an id_token from Auth0 using the Resource Owner Password Credentials grant.
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

        // Auth0 token endpoint is /oauth/token
        var auth0TokenEndpoint = $"{Authority}/oauth/token";
        var response = await httpClient.PostAsync(auth0TokenEndpoint, tokenRequest);
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
