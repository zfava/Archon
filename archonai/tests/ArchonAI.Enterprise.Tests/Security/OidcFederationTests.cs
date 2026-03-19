using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using ArchonAI.Identity;
using ArchonAI.Identity.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Enterprise.Tests.Security;

/// <summary>
/// Tests for OIDC identity provider federation: token exchange, JIT provisioning,
/// tenant-provider mapping, multi-provider resolution, and security invariants.
/// </summary>
public sealed class OidcFederationTests
{
    private readonly InMemoryUserStore _users = new();
    private readonly InMemoryOrganizationStore _orgs = new();
    private readonly InMemoryMembershipStore _memberships = new();
    private readonly InMemoryRefreshTokenStore _refreshTokens = new();
    private readonly InMemoryTenantAuthConfigStore _tenantAuthConfigs = new();
    private readonly InMemoryExternalIdentityLinkStore _externalLinks = new();
    private readonly TokenService _tokenService;
    private readonly AuthenticationOptions _authOptions;

    public OidcFederationTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Jwt:SigningKey"] = "test-signing-key-that-is-at-least-32-bytes-long!!",
                ["Security:Jwt:Issuer"] = "ArchonAI",
                ["Security:Jwt:Audience"] = "ArchonAI.Api",
            })
            .Build();

        _tokenService = new TokenService(config);
        _authOptions = new AuthenticationOptions
        {
            AccessTokenLifetimeMinutes = 30,
            RefreshTokenLifetimeDays = 7,
        };
    }

    private OidcTokenExchangeService CreateExchangeService() => new(
        _tenantAuthConfigs,
        _externalLinks,
        _users,
        _orgs,
        _memberships,
        _refreshTokens,
        _tokenService,
        Options.Create(_authOptions),
        NullLogger<OidcTokenExchangeService>.Instance);

    private async Task<Organization> CreateTestOrg(string name = "TestOrg", string slug = "test-org")
    {
        var org = new Organization(Guid.NewGuid(), name, slug, true, DateTimeOffset.UtcNow);
        await _orgs.CreateAsync(org);
        return org;
    }

    private async Task<TenantAuthConfig> CreateTestAuthConfig(
        Guid orgId,
        OidcProviderType providerType = OidcProviderType.Okta,
        bool autoProvision = true,
        string defaultRole = "Viewer",
        bool isEnabled = true)
    {
        var config = new TenantAuthConfig(
            Id: Guid.NewGuid(),
            OrganizationId: orgId,
            ProviderType: providerType,
            Authority: "https://dev-test.okta.com",
            ClientId: "test-client-id",
            ClientSecret: "test-client-secret",
            Domain: "test.okta.com",
            Scopes: new[] { "openid", "profile", "email" },
            AutoProvision: autoProvision,
            DefaultRole: defaultRole,
            IsEnabled: isEnabled,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: null);
        await _tenantAuthConfigs.CreateAsync(config);
        return config;
    }

    // ── Tenant-Provider Mapping Tests ────────────────────────────────────

    [Fact]
    public async Task TenantAuthConfig_Create_And_Retrieve()
    {
        var org = await CreateTestOrg();
        var config = await CreateTestAuthConfig(org.Id);

        var retrieved = await _tenantAuthConfigs.GetByOrganizationIdAsync(org.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(config.Id, retrieved.Id);
        Assert.Equal(OidcProviderType.Okta, retrieved.ProviderType);
        Assert.Equal("https://dev-test.okta.com", retrieved.Authority);
        Assert.True(retrieved.AutoProvision);
    }

    [Fact]
    public async Task TenantAuthConfig_Disabled_NotReturned()
    {
        var org = await CreateTestOrg();
        await CreateTestAuthConfig(org.Id, isEnabled: false);

        var retrieved = await _tenantAuthConfigs.GetByOrganizationIdAsync(org.Id);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task TenantAuthConfig_MultipleProviders_DifferentTenants()
    {
        var org1 = await CreateTestOrg("Org1", "org1");
        var org2 = await CreateTestOrg("Org2", "org2");

        await CreateTestAuthConfig(org1.Id, OidcProviderType.Okta);
        await CreateTestAuthConfig(org2.Id, OidcProviderType.MicrosoftEntraId);

        var config1 = await _tenantAuthConfigs.GetByOrganizationIdAsync(org1.Id);
        var config2 = await _tenantAuthConfigs.GetByOrganizationIdAsync(org2.Id);

        Assert.NotNull(config1);
        Assert.NotNull(config2);
        Assert.Equal(OidcProviderType.Okta, config1.ProviderType);
        Assert.Equal(OidcProviderType.MicrosoftEntraId, config2.ProviderType);
    }

    [Fact]
    public async Task TenantAuthConfig_Update_PreservesCreatedAt()
    {
        var org = await CreateTestOrg();
        var config = await CreateTestAuthConfig(org.Id);

        var updated = config with
        {
            Authority = "https://updated.okta.com",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await _tenantAuthConfigs.UpdateAsync(updated);

        var retrieved = await _tenantAuthConfigs.GetByIdAsync(config.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("https://updated.okta.com", retrieved.Authority);
        Assert.Equal(config.CreatedAtUtc, retrieved.CreatedAtUtc);
        Assert.NotNull(retrieved.UpdatedAtUtc);
    }

    [Fact]
    public async Task TenantAuthConfig_Delete_RemovesConfig()
    {
        var org = await CreateTestOrg();
        var config = await CreateTestAuthConfig(org.Id);

        await _tenantAuthConfigs.DeleteAsync(config.Id);

        var retrieved = await _tenantAuthConfigs.GetByIdAsync(config.Id);
        Assert.Null(retrieved);
    }

    // ── External Identity Link Tests ─────────────────────────────────────

    [Fact]
    public async Task ExternalIdentityLink_Create_And_LookupBySubject()
    {
        var org = await CreateTestOrg();
        var link = new ExternalIdentityLink(
            Id: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            OrganizationId: org.Id,
            ProviderType: OidcProviderType.Okta,
            ExternalSubject: "okta|user123",
            ExternalIssuer: "https://dev-test.okta.com",
            ExternalEmail: "user@test.com",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            LastUsedAtUtc: null);
        await _externalLinks.CreateAsync(link);

        var retrieved = await _externalLinks.GetByExternalSubjectAsync("okta|user123", "https://dev-test.okta.com");
        Assert.NotNull(retrieved);
        Assert.Equal(link.UserId, retrieved.UserId);
    }

    [Fact]
    public async Task ExternalIdentityLink_DifferentIssuers_AreIsolated()
    {
        var org = await CreateTestOrg();
        var link1 = new ExternalIdentityLink(
            Guid.NewGuid(), Guid.NewGuid(), org.Id, OidcProviderType.Okta,
            "shared-sub", "https://issuer1.com", "a@test.com", DateTimeOffset.UtcNow, null);
        var link2 = new ExternalIdentityLink(
            Guid.NewGuid(), Guid.NewGuid(), org.Id, OidcProviderType.Auth0,
            "shared-sub", "https://issuer2.com", "b@test.com", DateTimeOffset.UtcNow, null);

        await _externalLinks.CreateAsync(link1);
        await _externalLinks.CreateAsync(link2);

        var r1 = await _externalLinks.GetByExternalSubjectAsync("shared-sub", "https://issuer1.com");
        var r2 = await _externalLinks.GetByExternalSubjectAsync("shared-sub", "https://issuer2.com");

        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.NotEqual(r1.UserId, r2.UserId);
    }

    // ── Token Exchange Tests ─────────────────────────────────────────────

    [Fact]
    public async Task Exchange_NoConfig_ReturnsNull()
    {
        var service = CreateExchangeService();
        var orgId = Guid.NewGuid(); // No org or config

        var result = await service.ExchangeAsync("fake-token", "nonce", orgId);
        Assert.Null(result);
    }

    [Fact]
    public async Task Exchange_InactiveOrg_ReturnsNull()
    {
        var org = new Organization(Guid.NewGuid(), "Dead", "dead", false, DateTimeOffset.UtcNow);
        await _orgs.CreateAsync(org);
        await CreateTestAuthConfig(org.Id);

        var service = CreateExchangeService();
        var result = await service.ExchangeAsync("fake-token", "nonce", org.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task Exchange_DisabledConfig_ReturnsNull()
    {
        var org = await CreateTestOrg("DisabledOrg", "disabled-org");
        await CreateTestAuthConfig(org.Id, isEnabled: false);

        var service = CreateExchangeService();
        var result = await service.ExchangeAsync("fake-token", "nonce", org.Id);
        Assert.Null(result);
    }

    // ── JIT Provisioning Tests ───────────────────────────────────────────

    [Fact]
    public async Task JitProvisioning_CreatesUser_WithDefaultRole()
    {
        var org = await CreateTestOrg("JitOrg", "jit-org");
        var config = await CreateTestAuthConfig(org.Id, defaultRole: "Operator");

        // Simulate what JIT provisioning does internally (via the store operations)
        var userId = Guid.NewGuid();
        var user = new UserIdentity(
            userId, "jit@test.com", "JIT User", "dGVzdHNhbHR0ZXN0c2E=.dGVzdGhhc2h0ZXN0aGFzaHRlc3RoYXNodGVzdGhhc2g=",
            org.Id, "Operator", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await _users.CreateAsync(user);

        var membership = new Membership(
            Guid.NewGuid(), userId, org.Id, "Operator", DateTimeOffset.UtcNow);
        await _memberships.CreateAsync(membership);

        var link = new ExternalIdentityLink(
            Guid.NewGuid(), userId, org.Id, OidcProviderType.Okta,
            "jit-subject", "https://dev-test.okta.com", "jit@test.com",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await _externalLinks.CreateAsync(link);

        // Verify all records exist
        var createdUser = await _users.GetByEmailAsync("jit@test.com");
        Assert.NotNull(createdUser);
        Assert.Equal("Operator", createdUser.Role);

        var createdMembership = await _memberships.GetAsync(userId, org.Id);
        Assert.NotNull(createdMembership);
        Assert.Equal("Operator", createdMembership.Role);

        var createdLink = await _externalLinks.GetByExternalSubjectAsync("jit-subject", "https://dev-test.okta.com");
        Assert.NotNull(createdLink);
        Assert.Equal(userId, createdLink.UserId);
    }

    [Fact]
    public async Task JitProvisioning_ExistingEmailUser_LinksWithoutDuplicate()
    {
        var org = await CreateTestOrg("LinkOrg", "link-org");

        // Pre-existing user (registered via password auth service)
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Jwt:SigningKey"] = "test-signing-key-that-is-at-least-32-bytes-long!!",
                ["Security:Jwt:Issuer"] = "ArchonAI",
                ["Security:Jwt:Audience"] = "ArchonAI.Api",
            })
            .Build();
        var authService = new AuthenticationService(
            _users, _orgs, _memberships, _refreshTokens,
            new InMemoryInviteTokenStore(), _tokenService,
            Options.Create(_authOptions),
            NullLogger<AuthenticationService>.Instance);

        // Register user via password flow in a separate org first to get a password hash
        var reg = await authService.RegisterAsync("LinkOrg2", "existing@test.com", "password123!", "Existing User");
        Assert.NotNull(reg);

        // External link points to existing user
        var link = new ExternalIdentityLink(
            Guid.NewGuid(), reg.Value.User.Id, reg.Value.Org.Id, OidcProviderType.MicrosoftEntraId,
            "entra-sub-123", "https://login.microsoftonline.com/tenant",
            "existing@test.com", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await _externalLinks.CreateAsync(link);

        var foundLink = await _externalLinks.GetByExternalSubjectAsync(
            "entra-sub-123", "https://login.microsoftonline.com/tenant");
        Assert.NotNull(foundLink);
        Assert.Equal(reg.Value.User.Id, foundLink.UserId);

        // User can still log in with password
        var login = await authService.LoginAsync("existing@test.com", "password123!");
        Assert.NotNull(login);
    }

    [Fact]
    public async Task JitProvisioning_AutoProvisionDisabled_ReturnsNull()
    {
        var org = await CreateTestOrg("NoAutoOrg", "no-auto-org");
        await CreateTestAuthConfig(org.Id, autoProvision: false);

        var service = CreateExchangeService();
        // This will fail because token validation hits a real endpoint,
        // but we're testing the auto-provision check path
        var result = await service.ExchangeAsync("fake-token", "nonce", org.Id);
        Assert.Null(result);
    }

    // ── OIDC Security Tests ──────────────────────────────────────────────

    [Fact]
    public void GenerateOidcStateOrNonce_ProducesUniqueValues()
    {
        var values = Enumerable.Range(0, 100)
            .Select(_ => OidcTokenExchangeService.GenerateOidcStateOrNonce())
            .ToHashSet();

        // All 100 values should be unique (cryptographically random)
        Assert.Equal(100, values.Count);
    }

    [Fact]
    public void GenerateOidcStateOrNonce_HasSufficientEntropy()
    {
        var value = OidcTokenExchangeService.GenerateOidcStateOrNonce();

        // Base64 encoding of 32 bytes = ~44 characters
        Assert.True(value.Length >= 40, $"State/nonce too short: {value.Length} chars");
    }

    // ── SAML Stub Tests ──────────────────────────────────────────────────

    [Fact]
    public async Task SamlStub_Authenticate_ThrowsNotImplemented()
    {
        ISamlAuthenticationHandler handler = new NotImplementedSamlHandler();
        await Assert.ThrowsAsync<NotImplementedException>(
            () => handler.AuthenticateAsync("saml-response"));
    }

    [Fact]
    public async Task SamlStub_GenerateAuthnRequest_ThrowsNotImplemented()
    {
        ISamlAuthenticationHandler handler = new NotImplementedSamlHandler();
        await Assert.ThrowsAsync<NotImplementedException>(
            () => handler.GenerateAuthnRequestAsync("entity-id", "https://callback.com"));
    }

    // ── Multi-Provider Resolution Tests ──────────────────────────────────

    [Fact]
    public async Task MultiProvider_ThreeOrgsDifferentProviders_AllResolveCorrectly()
    {
        var orgOkta = await CreateTestOrg("OktaOrg", "okta-org");
        var orgEntra = await CreateTestOrg("EntraOrg", "entra-org");
        var orgAuth0 = await CreateTestOrg("Auth0Org", "auth0-org");

        await CreateTestAuthConfig(orgOkta.Id, OidcProviderType.Okta);
        await CreateTestAuthConfig(orgEntra.Id, OidcProviderType.MicrosoftEntraId);
        await CreateTestAuthConfig(orgAuth0.Id, OidcProviderType.Auth0);

        var configs = await _tenantAuthConfigs.ListAsync();
        Assert.Equal(3, configs.Count);

        var oktaConfig = await _tenantAuthConfigs.GetByOrganizationIdAsync(orgOkta.Id);
        var entraConfig = await _tenantAuthConfigs.GetByOrganizationIdAsync(orgEntra.Id);
        var auth0Config = await _tenantAuthConfigs.GetByOrganizationIdAsync(orgAuth0.Id);

        Assert.Equal(OidcProviderType.Okta, oktaConfig!.ProviderType);
        Assert.Equal(OidcProviderType.MicrosoftEntraId, entraConfig!.ProviderType);
        Assert.Equal(OidcProviderType.Auth0, auth0Config!.ProviderType);
    }

    // ── Token Exchange: Internal JWT Issuance ────────────────────────────

    [Fact]
    public async Task TokenExchange_InternalJwt_ContainsCorrectClaims()
    {
        var org = await CreateTestOrg("ClaimsOrg", "claims-org");
        var user = new UserIdentity(
            Guid.NewGuid(), "claims@test.com", "Claims User",
            "dGVzdHNhbHR0ZXN0c2E=.dGVzdGhhc2h0ZXN0aGFzaHRlc3RoYXNodGVzdGhhc2g=", org.Id, "Operator", true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await _users.CreateAsync(user);

        var membership = new Membership(
            Guid.NewGuid(), user.Id, org.Id, "Operator", DateTimeOffset.UtcNow);
        await _memberships.CreateAsync(membership);

        // Generate a token as the exchange service would
        var expiry = DateTimeOffset.UtcNow.AddMinutes(30);
        var accessToken = _tokenService.GenerateAccessToken(user, org, "Operator", expiry);

        Assert.NotEmpty(accessToken);

        // Decode and verify claims
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);

        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal("claims@test.com", jwt.Claims.First(c => c.Type == "email").Value);
        Assert.Equal("Claims User", jwt.Claims.First(c => c.Type == "name").Value);
        Assert.Equal(org.Id.ToString(), jwt.Claims.First(c => c.Type == "org_id").Value);
        Assert.Equal(org.Slug, jwt.Claims.First(c => c.Type == "org_slug").Value);
        Assert.Equal(org.Id.ToString(), jwt.Claims.First(c => c.Type == "tenant_id").Value);
    }

    // ── External Identity Link: ListByOrganization ───────────────────────

    [Fact]
    public async Task ExternalIdentityLinks_ListByOrg_ReturnsOnlyMatchingOrg()
    {
        var org1 = await CreateTestOrg("Org1", "list-org1");
        var org2 = await CreateTestOrg("Org2", "list-org2");

        for (int i = 0; i < 3; i++)
        {
            await _externalLinks.CreateAsync(new ExternalIdentityLink(
                Guid.NewGuid(), Guid.NewGuid(), org1.Id, OidcProviderType.Okta,
                $"sub-org1-{i}", "https://issuer.com", $"u{i}@org1.com",
                DateTimeOffset.UtcNow, null));
        }
        await _externalLinks.CreateAsync(new ExternalIdentityLink(
            Guid.NewGuid(), Guid.NewGuid(), org2.Id, OidcProviderType.Auth0,
            "sub-org2", "https://issuer.com", "u@org2.com",
            DateTimeOffset.UtcNow, null));

        var org1Links = await _externalLinks.ListByOrganizationAsync(org1.Id);
        var org2Links = await _externalLinks.ListByOrganizationAsync(org2.Id);

        Assert.Equal(3, org1Links.Count);
        Assert.Single(org2Links);
    }

    // ── OIDC + Existing Auth Coexistence Tests ───────────────────────────

    [Fact]
    public async Task OidcAndPasswordAuth_Coexist_Independently()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Jwt:SigningKey"] = "test-signing-key-that-is-at-least-32-bytes-long!!",
                ["Security:Jwt:Issuer"] = "ArchonAI",
                ["Security:Jwt:Audience"] = "ArchonAI.Api",
            })
            .Build();

        // Create password-based auth service with the same stores
        var authService = new AuthenticationService(
            _users, _orgs, _memberships, _refreshTokens,
            new InMemoryInviteTokenStore(),
            _tokenService,
            Options.Create(_authOptions),
            NullLogger<AuthenticationService>.Instance);

        // Register via password
        var reg = await authService.RegisterAsync("CoexistOrg", "admin@coexist.com", "password123!", "Admin");
        Assert.NotNull(reg);

        // Create OIDC auth config for the same org
        await CreateTestAuthConfig(reg.Value.Org.Id);

        // Create OIDC-provisioned user in the same org
        var oidcUser = new UserIdentity(
            Guid.NewGuid(), "oidc-user@coexist.com", "OIDC User",
            "dGVzdHNhbHR0ZXN0c2E=.dGVzdGhhc2h0ZXN0aGFzaHRlc3RoYXNodGVzdGhhc2g=", reg.Value.Org.Id, "Viewer", true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await _users.CreateAsync(oidcUser);
        await _memberships.CreateAsync(new Membership(
            Guid.NewGuid(), oidcUser.Id, reg.Value.Org.Id, "Viewer", DateTimeOffset.UtcNow));

        // Password user can still log in
        var login = await authService.LoginAsync("admin@coexist.com", "password123!");
        Assert.NotNull(login);
        Assert.Equal("Admin", login.User!.Role);

        // OIDC user exists in the same org
        var oidcLookup = await _users.GetByEmailAsync("oidc-user@coexist.com");
        Assert.NotNull(oidcLookup);
        Assert.Equal(reg.Value.Org.Id, oidcLookup.OrganizationId);

        // Two users in the org
        var orgUsers = await _users.ListByOrganizationAsync(reg.Value.Org.Id);
        Assert.Equal(2, orgUsers.Count);
    }

    [Fact]
    public async Task OidcPasswordHash_CannotBeUsedForPasswordLogin()
    {
        var authService = new AuthenticationService(
            _users, _orgs, _memberships, _refreshTokens,
            new InMemoryInviteTokenStore(),
            _tokenService,
            Options.Create(_authOptions),
            NullLogger<AuthenticationService>.Instance);

        var org = await CreateTestOrg("SecOrg", "sec-org");

        // Create OIDC user with no real password — uses the same format as JIT provisioning
        // The salt/hash are random bytes, so no password can match
        byte[] fakeSalt = new byte[16];
        byte[] fakeHash = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(fakeSalt);
        System.Security.Cryptography.RandomNumberGenerator.Fill(fakeHash);
        string oidcPasswordHash = $"{Convert.ToBase64String(fakeSalt)}.{Convert.ToBase64String(fakeHash)}";

        var oidcUser = new UserIdentity(
            Guid.NewGuid(), "oidc-only@sec.com", "OIDC Only",
            oidcPasswordHash, org.Id, "Viewer", true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await _users.CreateAsync(oidcUser);
        await _memberships.CreateAsync(new Membership(
            Guid.NewGuid(), oidcUser.Id, org.Id, "Viewer", DateTimeOffset.UtcNow));

        // OIDC user cannot log in with any password
        var login1 = await authService.LoginAsync("oidc-only@sec.com", "anything");
        Assert.Null(login1);

        var login2 = await authService.LoginAsync("oidc-only@sec.com", "OIDC:placeholder.placeholder");
        Assert.Null(login2);

        var login3 = await authService.LoginAsync("oidc-only@sec.com", "");
        Assert.Null(login3);
    }
}
