using ArchonAI.Identity;
using ArchonAI.Identity.Mfa;
using ArchonAI.Identity.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OtpNet;

namespace ArchonAI.Tests.Mfa;

/// <summary>
/// Integration tests for the full MFA login flow:
/// register → enable TOTP → login (gets challenge) → verify TOTP → get tokens.
/// </summary>
public sealed class MfaLoginFlowTests
{
    private readonly AuthenticationService _auth;
    private readonly TotpService _totp;
    private readonly MfaChallengeService _mfaChallenge;

    public MfaLoginFlowTests()
    {
        var mfaStore = new InMemoryMfaStore();
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

        _auth = new AuthenticationService(
            users, orgs, memberships, refreshTokens, invites,
            new TokenService(config),
            Options.Create(new AuthenticationOptions()),
            NullLogger<AuthenticationService>.Instance);

        _totp = new TotpService(mfaStore, NullLogger<TotpService>.Instance);
        var webAuthn = new WebAuthnService(mfaStore, NullLogger<WebAuthnService>.Instance);
        _mfaChallenge = new MfaChallengeService(
            mfaStore, _totp, webAuthn, NullLogger<MfaChallengeService>.Instance);
    }

    private static string GenerateValidCode(string otpAuthUri)
    {
        var uri = new Uri(otpAuthUri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        string secret = query["secret"]!;
        byte[] secretBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(secretBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        return totp.ComputeTotp();
    }

    [Fact]
    public async Task LoginWithoutMfa_ReturnsTokensDirectly()
    {
        await _auth.RegisterAsync("Org", "user@test.com", "pass123!", "User");
        var login = await _auth.LoginAsync("user@test.com", "pass123!");

        Assert.NotNull(login);
        Assert.False(login.IsMfaRequired);
        Assert.NotNull(login.Tokens);
        Assert.NotEmpty(login.Tokens.AccessToken);
    }

    [Fact]
    public async Task MfaFlow_Register_EnableTotp_Login_Challenge_Verify()
    {
        // 1. Register
        var reg = await _auth.RegisterAsync("Org", "mfa@test.com", "pass123!", "MFA User");
        Assert.NotNull(reg);
        var userId = reg.Value.User.Id;

        // 2. Enable TOTP
        var setup = await _totp.GenerateSetupAsync(userId, "mfa@test.com");
        Assert.NotNull(setup);
        string setupCode = GenerateValidCode(setup.Value.OtpAuthUri);
        Assert.True(await _totp.VerifySetupAsync(userId, setupCode));
        Assert.True(await _mfaChallenge.HasMfaEnabledAsync(userId));

        // 3. Login (password only — succeeds because AuthService doesn't enforce MFA inline)
        var login = await _auth.LoginAsync("mfa@test.com", "pass123!");
        Assert.NotNull(login);
        Assert.NotNull(login.Tokens);

        // 4. The caller should check MFA and create a challenge
        Assert.True(await _mfaChallenge.HasMfaEnabledAsync(userId));
        var challenge = await _mfaChallenge.CreateChallengeAsync(userId);
        Assert.NotNull(challenge);
        Assert.Contains("totp", challenge.Value.Methods);

        // 5. Validate TOTP
        string loginCode = GenerateValidCode(setup.Value.OtpAuthUri);
        Assert.True(await _totp.VerifyAsync(userId, loginCode));

        // 6. Validate challenge token (single-use)
        var challengeUserId = await _mfaChallenge.ValidateChallengeAsync(challenge.Value.MfaToken);
        Assert.NotNull(challengeUserId);
        Assert.Equal(userId, challengeUserId.Value);

        // 7. Issue tokens after MFA
        var tokens = await _auth.IssueTokensForUserAsync(userId);
        Assert.NotNull(tokens);
        Assert.NotEmpty(tokens.Value.Tokens.AccessToken);
    }

    [Fact]
    public async Task MfaFlow_RecoveryCode_BypassesTotp()
    {
        var reg = await _auth.RegisterAsync("Org", "recovery@test.com", "pass123!", "User");
        Assert.NotNull(reg);
        var userId = reg.Value.User.Id;

        // Enable TOTP
        var setup = await _totp.GenerateSetupAsync(userId, "recovery@test.com");
        Assert.NotNull(setup);
        string code = GenerateValidCode(setup.Value.OtpAuthUri);
        await _totp.VerifySetupAsync(userId, code);

        // Create MFA challenge
        var challenge = await _mfaChallenge.CreateChallengeAsync(userId);
        Assert.NotNull(challenge);
        Assert.Contains("recovery", challenge.Value.Methods);

        // Use recovery code instead of TOTP
        Assert.True(await _mfaChallenge.VerifyRecoveryCodeAsync(userId, setup.Value.RecoveryCodes[0]));

        // Validate challenge and issue tokens
        var challengeUserId = await _mfaChallenge.ValidateChallengeAsync(challenge.Value.MfaToken);
        Assert.NotNull(challengeUserId);
        var tokens = await _auth.IssueTokensForUserAsync(userId);
        Assert.NotNull(tokens);
    }

    [Fact]
    public async Task IssueTokensForUser_InvalidUser_ReturnsNull()
    {
        var result = await _auth.IssueTokensForUserAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task LoginResult_BackwardCompatible_NoMfa()
    {
        await _auth.RegisterAsync("Org", "old@test.com", "pass123!", "User");
        var login = await _auth.LoginAsync("old@test.com", "pass123!");

        Assert.NotNull(login);
        // Backward-compatible: direct access to Tokens, User, Org
        Assert.NotNull(login.Tokens);
        Assert.NotNull(login.User);
        Assert.NotNull(login.Org);
        Assert.False(login.IsMfaRequired);
        Assert.Null(login.MfaRequired);
    }
}
