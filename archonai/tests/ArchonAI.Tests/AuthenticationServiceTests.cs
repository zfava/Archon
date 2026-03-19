using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using ArchonAI.Identity;
using ArchonAI.Identity.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Tests;

public class AuthenticationServiceTests
{
    private readonly AuthenticationService _auth;
    private readonly IUserStore _users;
    private readonly IOrganizationStore _orgs;
    private readonly IMembershipStore _memberships;

    public AuthenticationServiceTests()
    {
        _users = new InMemoryUserStore();
        _orgs = new InMemoryOrganizationStore();
        _memberships = new InMemoryMembershipStore();
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

        var tokenService = new TokenService(config);
        var options = Options.Create(new AuthenticationOptions
        {
            AccessTokenLifetimeMinutes = 30,
            RefreshTokenLifetimeDays = 7,
        });

        _auth = new AuthenticationService(
            _users, _orgs, _memberships, refreshTokens, invites,
            tokenService, options, NullLogger<AuthenticationService>.Instance);
    }

    [Fact]
    public async Task Register_CreatesOrgUserAndReturnsTokens()
    {
        var result = await _auth.RegisterAsync("Acme Corp", "admin@acme.com", "password123", "Admin User");

        Assert.NotNull(result);
        var (org, user, tokens) = result.Value;
        Assert.Equal("Acme Corp", org.Name);
        Assert.Equal("admin@acme.com", user.Email);
        Assert.Equal("Admin", user.Role);
        Assert.NotEmpty(tokens.AccessToken);
        Assert.NotEmpty(tokens.RefreshToken);
        Assert.True(tokens.ExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsNull()
    {
        await _auth.RegisterAsync("Org1", "dup@test.com", "password123", "User");
        var result = await _auth.RegisterAsync("Org2", "dup@test.com", "password456", "User");

        Assert.Null(result);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokens()
    {
        await _auth.RegisterAsync("Acme", "user@acme.com", "correctpass", "User");
        var result = await _auth.LoginAsync("user@acme.com", "correctpass");

        Assert.NotNull(result);
        Assert.NotNull(result.Tokens);
        Assert.NotEmpty(result.Tokens.AccessToken);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsNull()
    {
        await _auth.RegisterAsync("Acme", "user@acme.com", "correctpass", "User");
        var result = await _auth.LoginAsync("user@acme.com", "wrongpass");

        Assert.Null(result);
    }

    [Fact]
    public async Task Login_NonexistentUser_ReturnsNull()
    {
        var result = await _auth.LoginAsync("nobody@test.com", "password");
        Assert.Null(result);
    }

    [Fact]
    public async Task Refresh_ValidToken_IssuesNewTokens()
    {
        var reg = await _auth.RegisterAsync("Acme", "r@acme.com", "password123", "User");
        Assert.NotNull(reg);

        var refreshed = await _auth.RefreshAsync(reg.Value.Tokens.RefreshToken);
        Assert.NotNull(refreshed);
        Assert.NotEmpty(refreshed.AccessToken);
        // New refresh token is always different (new random bytes)
        Assert.NotEqual(reg.Value.Tokens.RefreshToken, refreshed.RefreshToken);
    }

    [Fact]
    public async Task Refresh_InvalidToken_ReturnsNull()
    {
        var result = await _auth.RefreshAsync("totally-bogus-token");
        Assert.Null(result);
    }

    [Fact]
    public async Task Refresh_UsedToken_CannotBeReusedRotation()
    {
        var reg = await _auth.RegisterAsync("Acme", "rot@acme.com", "password123", "User");
        Assert.NotNull(reg);

        // First refresh succeeds
        var first = await _auth.RefreshAsync(reg.Value.Tokens.RefreshToken);
        Assert.NotNull(first);

        // Reusing the same token fails (rotation)
        var second = await _auth.RefreshAsync(reg.Value.Tokens.RefreshToken);
        Assert.Null(second);
    }

    [Fact]
    public async Task Logout_RevokesAllRefreshTokens()
    {
        var reg = await _auth.RegisterAsync("Acme", "lo@acme.com", "password123", "User");
        Assert.NotNull(reg);

        await _auth.LogoutAsync(reg.Value.User.Id);

        var refreshed = await _auth.RefreshAsync(reg.Value.Tokens.RefreshToken);
        Assert.Null(refreshed);
    }

    [Fact]
    public void PasswordHashing_RoundTrips()
    {
        string hash = AuthenticationService.HashPassword("my-secret");
        Assert.True(AuthenticationService.VerifyPassword("my-secret", hash));
        Assert.False(AuthenticationService.VerifyPassword("wrong", hash));
    }

    [Fact]
    public void PasswordHashing_UniqueSalts()
    {
        string h1 = AuthenticationService.HashPassword("same");
        string h2 = AuthenticationService.HashPassword("same");
        Assert.NotEqual(h1, h2); // different salts
    }
}
