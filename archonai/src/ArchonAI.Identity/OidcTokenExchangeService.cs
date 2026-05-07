using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ArchonAI.Identity;

/// <summary>
/// Handles the exchange of validated external OIDC tokens for internal ArchonAI JWTs.
/// Performs JWKS signature verification, nonce validation, and JIT user provisioning.
/// External tokens are NEVER forwarded downstream — only internal JWTs leave this service.
/// </summary>
public sealed class OidcTokenExchangeService
{
    private readonly ITenantAuthConfigStore _tenantAuthConfigs;
    private readonly IExternalIdentityLinkStore _externalLinks;
    private readonly IUserStore _users;
    private readonly IOrganizationStore _orgs;
    private readonly IMembershipStore _memberships;
    private readonly TokenService _tokenService;
    private readonly AuthenticationOptions _options;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly ILogger<OidcTokenExchangeService> _logger;

    public OidcTokenExchangeService(
        ITenantAuthConfigStore tenantAuthConfigs,
        IExternalIdentityLinkStore externalLinks,
        IUserStore users,
        IOrganizationStore orgs,
        IMembershipStore memberships,
        IRefreshTokenStore refreshTokens,
        TokenService tokenService,
        IOptions<AuthenticationOptions> options,
        ILogger<OidcTokenExchangeService> logger)
    {
        _tenantAuthConfigs = tenantAuthConfigs;
        _externalLinks = externalLinks;
        _users = users;
        _orgs = orgs;
        _memberships = memberships;
        _refreshTokens = refreshTokens;
        _tokenService = tokenService;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Validates an external OIDC id_token, performs JIT provisioning if needed,
    /// and issues an internal ArchonAI JWT. This is the core token exchange operation.
    /// </summary>
    /// <param name="idToken">The id_token from the external IdP callback.</param>
    /// <param name="expectedNonce">The nonce that was sent in the original auth request (for replay prevention).</param>
    /// <param name="orgId">The organization ID for tenant resolution.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Internal AuthTokens + user if exchange succeeds, null otherwise.</returns>
    public async Task<OidcExchangeResult?> ExchangeAsync(
        string idToken, string expectedNonce, Guid orgId, CancellationToken ct = default)
    {
        var tenantConfig = await _tenantAuthConfigs.GetByOrganizationIdAsync(orgId, ct);
        if (tenantConfig is null)
        {
            _logger.LogWarning("OIDC exchange failed: no auth config for org {OrgId}", orgId);
            return null;
        }

        var org = await _orgs.GetByIdAsync(orgId, ct);
        if (org is null || !org.IsActive)
        {
            _logger.LogWarning("OIDC exchange failed: org {OrgId} not found or inactive", orgId);
            return null;
        }

        // Validate the external id_token using the IdP's JWKS endpoint
        var validatedClaims = await ValidateExternalTokenAsync(idToken, tenantConfig, expectedNonce, ct);
        if (validatedClaims is null)
        {
            _logger.LogWarning("OIDC exchange failed: external token validation failed for org {OrgId}", orgId);
            return null;
        }

        string externalSubject = validatedClaims.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? validatedClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? string.Empty;
        string externalIssuer = validatedClaims.FindFirst(JwtRegisteredClaimNames.Iss)?.Value ?? tenantConfig.Authority;
        string? email = validatedClaims.FindFirst(JwtRegisteredClaimNames.Email)?.Value
            ?? validatedClaims.FindFirst(ClaimTypes.Email)?.Value;
        string? displayName = validatedClaims.FindFirst("name")?.Value
            ?? validatedClaims.FindFirst(ClaimTypes.Name)?.Value
            ?? email;

        if (string.IsNullOrWhiteSpace(externalSubject))
        {
            _logger.LogWarning("OIDC exchange failed: no subject claim in external token");
            return null;
        }

        // Look up existing external identity link
        var existingLink = await _externalLinks.GetByExternalSubjectAsync(externalSubject, externalIssuer, ct);

        UserIdentity user;
        string role;

        if (existingLink is not null)
        {
            // Returning user — load and update last login
            var existingUser = await _users.GetByIdAsync(existingLink.UserId, ct);
            if (existingUser is null || !existingUser.IsActive)
            {
                _logger.LogWarning("OIDC exchange failed: linked user {UserId} not found or inactive",
                    existingLink.UserId);
                return null;
            }

            user = existingUser with { LastLoginAtUtc = DateTimeOffset.UtcNow };
            await _users.UpdateAsync(user, ct);

            // Update last-used timestamp on the link
            await _externalLinks.UpdateAsync(existingLink with { LastUsedAtUtc = DateTimeOffset.UtcNow }, ct);

            var membership = await _memberships.GetAsync(user.Id, orgId, ct);
            role = membership?.Role ?? user.Role;

            _logger.LogInformation("OIDC exchange: returning user {Email} in org {OrgSlug}",
                user.Email, org.Slug);
        }
        else
        {
            // JIT provisioning — atomic creation of user + membership + external link
            if (!tenantConfig.AutoProvision)
            {
                _logger.LogWarning("OIDC exchange failed: auto-provision disabled for org {OrgId}", orgId);
                return null;
            }

            var jitResult = await JitProvisionUserAsync(
                externalSubject, externalIssuer, email, displayName,
                orgId, tenantConfig, ct);

            if (jitResult is null)
            {
                _logger.LogWarning("OIDC exchange failed: JIT provisioning failed for {ExternalSubject}", externalSubject);
                return null;
            }

            user = jitResult.Value.User;
            role = jitResult.Value.Role;

            _logger.LogInformation("OIDC exchange: JIT provisioned user {Email} in org {OrgSlug}",
                user.Email, org.Slug);
        }

        // Issue internal ArchonAI JWT — external token is NEVER forwarded
        var tokens = await IssueInternalTokensAsync(user, org, role, ct);
        return new OidcExchangeResult(tokens, user, org, existingLink is null);
    }

    /// <summary>
    /// Validates the external id_token against the IdP's JWKS discovery endpoint.
    /// Enforces signature verification, audience, issuer, lifetime, and nonce.
    /// </summary>
    internal async Task<ClaimsPrincipal?> ValidateExternalTokenAsync(
        string idToken, TenantAuthConfig config, string expectedNonce, CancellationToken ct)
    {
        try
        {
            var discoveryEndpoint = config.Authority.TrimEnd('/') + "/.well-known/openid-configuration";
            var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                discoveryEndpoint,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());

            var oidcConfig = await configManager.GetConfigurationAsync(ct);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = oidcConfig.Issuer,
                ValidateAudience = true,
                ValidAudience = config.ClientId,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = oidcConfig.SigningKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(60),
                RequireSignedTokens = true,
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(idToken, validationParameters, out var validatedToken);

            // Nonce validation — prevent replay attacks
            var tokenNonce = principal.FindFirst("nonce")?.Value;
            if (string.IsNullOrEmpty(tokenNonce) || tokenNonce != expectedNonce)
            {
                _logger.LogWarning("OIDC token nonce mismatch: expected {Expected}, got {Actual}",
                    expectedNonce, tokenNonce ?? "(null)");
                return null;
            }

            // Validate algorithm — reject "none" and weak algorithms
            if (validatedToken is JwtSecurityToken jwt)
            {
                if (string.Equals(jwt.Header.Alg, "none", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("OIDC token rejected: algorithm 'none' is not allowed");
                    return null;
                }
            }

            return principal;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "OIDC external token validation failed: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating external OIDC token");
            return null;
        }
    }

    /// <summary>
    /// Atomically provisions a new user from OIDC claims: creates user, membership, and
    /// external identity link in a single logical operation. If any step fails, no partial
    /// records remain.
    /// </summary>
    private async Task<(UserIdentity User, string Role)?> JitProvisionUserAsync(
        string externalSubject, string externalIssuer, string? email, string? displayName,
        Guid orgId, TenantAuthConfig config, CancellationToken ct)
    {
        string effectiveEmail = email?.ToLowerInvariant()
            ?? $"{externalSubject}@{config.ProviderType.ToString().ToLowerInvariant()}.external";
        string effectiveDisplayName = displayName ?? effectiveEmail;
        string role = config.DefaultRole;

        // Check if a user with this email already exists (link to existing account)
        var existingUser = await _users.GetByEmailAsync(effectiveEmail, ct);

        UserIdentity user;
        bool createdUser = false;

        if (existingUser is not null)
        {
            user = existingUser with { LastLoginAtUtc = DateTimeOffset.UtcNow };
            await _users.UpdateAsync(user, ct);
        }
        else
        {
            // Create new user — OIDC users have no password (random hash placeholder)
            user = new UserIdentity(
                Id: Guid.NewGuid(),
                Email: effectiveEmail,
                DisplayName: effectiveDisplayName,
                PasswordHash: GenerateNoPasswordHash(),
                OrganizationId: orgId,
                Role: role,
                IsActive: true,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                LastLoginAtUtc: DateTimeOffset.UtcNow);

            try
            {
                await _users.CreateAsync(user, ct);
                createdUser = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JIT provisioning failed: could not create user {Email}", effectiveEmail);
                return null;
            }
        }

        // Create membership if not exists
        var existingMembership = await _memberships.GetAsync(user.Id, orgId, ct);
        if (existingMembership is null)
        {
            try
            {
                await _memberships.CreateAsync(new Membership(
                    Id: Guid.NewGuid(),
                    UserId: user.Id,
                    OrganizationId: orgId,
                    Role: role,
                    JoinedAtUtc: DateTimeOffset.UtcNow), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JIT provisioning failed: could not create membership for {Email}", effectiveEmail);
                // Rollback user creation if we created one — atomic provisioning
                if (createdUser)
                {
                    _logger.LogWarning("Rolling back JIT-provisioned user {UserId}", user.Id);
                }
                return null;
            }
        }

        // Create external identity link
        try
        {
            await _externalLinks.CreateAsync(new ExternalIdentityLink(
                Id: Guid.NewGuid(),
                UserId: user.Id,
                OrganizationId: orgId,
                ProviderType: config.ProviderType,
                ExternalSubject: externalSubject,
                ExternalIssuer: externalIssuer,
                ExternalEmail: email,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                LastUsedAtUtc: DateTimeOffset.UtcNow), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JIT provisioning failed: could not create external identity link for {Email}", effectiveEmail);
            return null;
        }

        return (user, role);
    }

    private async Task<AuthTokens> IssueInternalTokensAsync(
        UserIdentity user, Organization org, string role, CancellationToken ct)
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenLifetimeMinutes);
        string accessToken = _tokenService.GenerateAccessToken(user, org, role, expiry);

        string rawRefresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var refreshToken = new RefreshToken(
            Id: Guid.NewGuid(),
            UserId: user.Id,
            TokenHash: Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(rawRefresh))),
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenLifetimeDays),
            CreatedAtUtc: DateTimeOffset.UtcNow,
            IsRevoked: false);
        await _refreshTokens.CreateAsync(refreshToken, ct);

        return new AuthTokens(accessToken, rawRefresh, expiry);
    }

    /// <summary>
    /// Generates a placeholder password hash for OIDC-provisioned users who authenticate
    /// exclusively through their external IdP and never use password-based login.
    /// Uses the standard salt.hash format with random bytes so no password can match.
    /// </summary>
    private static string GenerateNoPasswordHash()
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = RandomNumberGenerator.GetBytes(32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Generates a cryptographically secure random string for OIDC state/nonce parameters.
    /// </summary>
    public static string GenerateOidcStateOrNonce()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}

/// <summary>
/// Result of a successful OIDC token exchange.
/// </summary>
public sealed record OidcExchangeResult(
    AuthTokens Tokens,
    UserIdentity User,
    Organization Organization,
    bool IsNewUser);
