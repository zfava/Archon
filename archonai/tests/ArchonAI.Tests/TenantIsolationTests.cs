using ArchonAI.Identity;
using ArchonAI.Identity.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Tests;

public class TenantIsolationTests
{
    private readonly AuthenticationService _auth;
    private readonly InMemoryUserStore _users;
    private readonly InMemoryOrganizationStore _orgs;

    public TenantIsolationTests()
    {
        _users = new InMemoryUserStore();
        _orgs = new InMemoryOrganizationStore();
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

        var tokenService = new TokenService(config);
        var options = Options.Create(new AuthenticationOptions
        {
            AccessTokenLifetimeMinutes = 30,
            RefreshTokenLifetimeDays = 7,
        });

        _auth = new AuthenticationService(
            _users, _orgs, memberships, refreshTokens, invites,
            tokenService, options, NullLogger<AuthenticationService>.Instance);
    }

    [Fact]
    public async Task UsersInDifferentOrgs_HaveDifferentOrgIds()
    {
        var reg1 = await _auth.RegisterAsync("Org A", "a@a.com", "password123", "User A");
        var reg2 = await _auth.RegisterAsync("Org B", "b@b.com", "password123", "User B");

        Assert.NotNull(reg1);
        Assert.NotNull(reg2);
        Assert.NotEqual(reg1.Value.Org.Id, reg2.Value.Org.Id);
    }

    [Fact]
    public async Task UserStore_ListByOrganization_OnlyReturnsTenantUsers()
    {
        var reg1 = await _auth.RegisterAsync("Org A", "a1@a.com", "password123", "A1");
        var reg2 = await _auth.RegisterAsync("Org B", "b1@b.com", "password123", "B1");

        Assert.NotNull(reg1);
        Assert.NotNull(reg2);

        var orgAUsers = await _users.ListByOrganizationAsync(reg1.Value.Org.Id);
        var orgBUsers = await _users.ListByOrganizationAsync(reg2.Value.Org.Id);

        Assert.Single(orgAUsers);
        Assert.Equal("a1@a.com", orgAUsers[0].Email);
        Assert.Single(orgBUsers);
        Assert.Equal("b1@b.com", orgBUsers[0].Email);
    }

    [Fact]
    public async Task Login_ReturnsCorrectTenantContext()
    {
        await _auth.RegisterAsync("Acme", "admin@acme.com", "password123", "Admin");
        var login = await _auth.LoginAsync("admin@acme.com", "password123");

        Assert.NotNull(login);
        Assert.Equal("Admin", login.Value.User.Role);
        Assert.Contains("acme", login.Value.Org.Slug);
    }

    [Fact]
    public async Task AccessToken_ContainsTenantClaims()
    {
        var reg = await _auth.RegisterAsync("TestOrg", "t@t.com", "password123", "T");
        Assert.NotNull(reg);

        var token = reg.Value.Tokens.AccessToken;
        // Decode JWT payload (base64url)
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        string payload = parts[1];
        // Pad base64
        payload = payload.Replace('-', '+').Replace('_', '/');
        switch (payload.Length % 4) { case 2: payload += "=="; break; case 3: payload += "="; break; }
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));

        Assert.Contains("tenant_id", json);
        Assert.Contains("org_id", json);
        Assert.Contains("org_slug", json);
        Assert.Contains(reg.Value.Org.Id.ToString(), json);
    }

    [Fact]
    public async Task InviteFlow_AddsUserToCorrectOrg()
    {
        var reg = await _auth.RegisterAsync("InvOrg", "owner@inv.com", "password123", "Owner");
        Assert.NotNull(reg);

        var invite = await _auth.InviteUserAsync(
            reg.Value.Org.Id, "newbie@inv.com", "Operator", reg.Value.User.Id);
        Assert.NotNull(invite);

        var accept = await _auth.AcceptInviteAsync(
            invite.Value.RawToken, "newpass123", "Newbie");
        Assert.NotNull(accept);
        Assert.Equal("newbie@inv.com", accept.Value.User.Email);
    }
}
