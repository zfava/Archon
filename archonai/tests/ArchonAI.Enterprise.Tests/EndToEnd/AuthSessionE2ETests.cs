using ArchonAI.Identity;
using ArchonAI.Identity.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Enterprise.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the full authentication session lifecycle:
/// register → login → refresh → use → logout → denied.
/// </summary>
public sealed class AuthSessionE2ETests
{
    private AuthenticationService CreateAuth()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Jwt:SigningKey"] = "test-signing-key-that-is-at-least-32-bytes-long!!",
                ["Security:Jwt:Issuer"] = "ArchonAI",
                ["Security:Jwt:Audience"] = "ArchonAI.Api",
            })
            .Build();

        return new AuthenticationService(
            new InMemoryUserStore(), new InMemoryOrganizationStore(),
            new InMemoryMembershipStore(), new InMemoryRefreshTokenStore(),
            new InMemoryInviteTokenStore(),
            new TokenService(config),
            Options.Create(new AuthenticationOptions
            {
                AccessTokenLifetimeMinutes = 30,
                RefreshTokenLifetimeDays = 7,
            }),
            NullLogger<AuthenticationService>.Instance);
    }

    // ── Full Session Lifecycle ─────────────────────────────────────────

    [Fact]
    public async Task FullLifecycle_Register_Login_Refresh_Logout()
    {
        var auth = CreateAuth();

        // 1. Register
        var reg = await auth.RegisterAsync("Acme Corp", "admin@acme.com", "securePass123!", "Admin");
        Assert.NotNull(reg);
        Assert.Equal("admin@acme.com", reg.Value.User.Email);
        Assert.Equal("Admin", reg.Value.User.Role);
        Assert.NotEmpty(reg.Value.Tokens.AccessToken);
        Assert.NotEmpty(reg.Value.Tokens.RefreshToken);

        // 2. Login
        var login = await auth.LoginAsync("admin@acme.com", "securePass123!");
        Assert.NotNull(login);
        Assert.NotNull(login.Tokens);
        Assert.NotEmpty(login.Tokens.AccessToken);

        // 3. Refresh
        var refreshed = await auth.RefreshAsync(login.Tokens.RefreshToken);
        Assert.NotNull(refreshed);
        Assert.NotEqual(login.Tokens.RefreshToken, refreshed.RefreshToken);

        // 4. Old refresh token is revoked (rotation)
        var reuse = await auth.RefreshAsync(login.Tokens.RefreshToken);
        Assert.Null(reuse);

        // 5. Logout
        await auth.LogoutAsync(reg.Value.User.Id);

        // 6. Refresh after logout fails
        var afterLogout = await auth.RefreshAsync(refreshed.RefreshToken);
        Assert.Null(afterLogout);
    }

    // ── Multi-User Isolation ──────────────────────────────────────────

    [Fact]
    public async Task MultipleUsers_SessionsAreIndependent()
    {
        var auth = CreateAuth();

        var user1 = await auth.RegisterAsync("Org1", "u1@test.com", "pass1!", "Admin");
        var user2 = await auth.RegisterAsync("Org2", "u2@test.com", "pass2!", "Admin");

        Assert.NotNull(user1);
        Assert.NotNull(user2);

        // Logout user1
        await auth.LogoutAsync(user1.Value.User.Id);

        // User1's token is dead
        var refresh1 = await auth.RefreshAsync(user1.Value.Tokens.RefreshToken);
        Assert.Null(refresh1);

        // User2's token is still alive
        var refresh2 = await auth.RefreshAsync(user2.Value.Tokens.RefreshToken);
        Assert.NotNull(refresh2);
    }

    // ── Invite Flow E2E ───────────────────────────────────────────────

    [Fact]
    public async Task InviteFlow_OwnerInvitesUser_UserJoinsOrg()
    {
        var auth = CreateAuth();

        // Owner registers
        var owner = await auth.RegisterAsync("TeamOrg", "owner@team.com", "ownerPass!", "Owner");
        Assert.NotNull(owner);

        // Owner invites a new user
        var invite = await auth.InviteUserAsync(
            owner.Value.Org.Id, "newbie@team.com", "Operator", owner.Value.User.Id);
        Assert.NotNull(invite);

        // New user accepts invite
        var accepted = await auth.AcceptInviteAsync(invite.Value.RawToken, "newbiePass!", "Newbie");
        Assert.NotNull(accepted);
        Assert.Equal("newbie@team.com", accepted.Value.User.Email);

        // New user can login
        var login = await auth.LoginAsync("newbie@team.com", "newbiePass!");
        Assert.NotNull(login);
    }

    // ── Duplicate Registration Prevention ─────────────────────────────

    [Fact]
    public async Task DuplicateEmail_Registration_Rejected()
    {
        var auth = CreateAuth();

        var first = await auth.RegisterAsync("Org1", "dup@test.com", "pass1!", "Admin");
        Assert.NotNull(first);

        var second = await auth.RegisterAsync("Org2", "dup@test.com", "pass2!", "Admin");
        Assert.Null(second);
    }

    // ── Token Rotation Security ───────────────────────────────────────

    [Fact]
    public async Task RefreshTokenRotation_ChainedRefreshes_EachProducesNewToken()
    {
        var auth = CreateAuth();
        var reg = await auth.RegisterAsync("Org", "chain@test.com", "pass!", "Admin");
        Assert.NotNull(reg);

        var currentToken = reg.Value.Tokens.RefreshToken;
        var seenTokens = new HashSet<string> { currentToken };

        for (int i = 0; i < 5; i++)
        {
            var refreshed = await auth.RefreshAsync(currentToken);
            Assert.NotNull(refreshed);
            Assert.DoesNotContain(refreshed.RefreshToken, seenTokens);

            seenTokens.Add(refreshed.RefreshToken);
            currentToken = refreshed.RefreshToken;
        }

        Assert.Equal(6, seenTokens.Count); // 1 original + 5 refreshes
    }

    // ── Wrong Password Attempts ───────────────────────────────────────

    [Fact]
    public async Task MultipleWrongPasswords_AllFail()
    {
        var auth = CreateAuth();
        await auth.RegisterAsync("Org", "user@test.com", "correctPass!", "Admin");

        for (int i = 0; i < 10; i++)
        {
            var result = await auth.LoginAsync("user@test.com", $"wrong-{i}");
            Assert.Null(result);
        }

        // Correct password still works
        var correct = await auth.LoginAsync("user@test.com", "correctPass!");
        Assert.NotNull(correct);
    }
}
