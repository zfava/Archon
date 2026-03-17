using System.Security.Cryptography;
using System.Text;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Identity;

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public int AccessTokenLifetimeMinutes { get; set; } = 30;
    public int RefreshTokenLifetimeDays { get; set; } = 7;
    public int InviteTokenLifetimeDays { get; set; } = 7;
}

public sealed class AuthenticationService
{
    private readonly IUserStore _users;
    private readonly IOrganizationStore _orgs;
    private readonly IMembershipStore _memberships;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IInviteTokenStore _invites;
    private readonly TokenService _tokenService;
    private readonly AuthenticationOptions _options;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IUserStore users,
        IOrganizationStore orgs,
        IMembershipStore memberships,
        IRefreshTokenStore refreshTokens,
        IInviteTokenStore invites,
        TokenService tokenService,
        IOptions<AuthenticationOptions> options,
        ILogger<AuthenticationService> logger)
    {
        _users = users;
        _orgs = orgs;
        _memberships = memberships;
        _refreshTokens = refreshTokens;
        _invites = invites;
        _tokenService = tokenService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(AuthTokens Tokens, UserIdentity User, Organization Org)?> LoginAsync(
        string email, string password, CancellationToken ct = default)
    {
        var user = await _users.GetByEmailAsync(email, ct);
        if (user is null || !user.IsActive)
        {
            _logger.LogWarning("Login failed: user not found or inactive for {Email}", email);
            return null;
        }

        if (!VerifyPassword(password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed: bad credentials for {Email}", email);
            return null;
        }

        var org = await _orgs.GetByIdAsync(user.OrganizationId, ct);
        if (org is null || !org.IsActive)
        {
            _logger.LogWarning("Login failed: org inactive for {Email}", email);
            return null;
        }

        var membership = await _memberships.GetAsync(user.Id, org.Id, ct);
        string role = membership?.Role ?? user.Role;

        var updatedUser = user with { LastLoginAtUtc = DateTimeOffset.UtcNow };
        await _users.UpdateAsync(updatedUser, ct);

        var tokens = await IssueTokensAsync(updatedUser, org, role, ct);
        _logger.LogInformation("Login succeeded for {Email} in org {OrgSlug}", email, org.Slug);
        return (tokens, updatedUser, org);
    }

    public async Task<(Organization Org, UserIdentity User, AuthTokens Tokens)?> RegisterAsync(
        string orgName, string email, string password, string displayName,
        CancellationToken ct = default)
    {
        var existing = await _users.GetByEmailAsync(email, ct);
        if (existing is not null)
        {
            _logger.LogWarning("Registration failed: email already exists {Email}", email);
            return null;
        }

        string slug = GenerateSlug(orgName);
        var existingOrg = await _orgs.GetBySlugAsync(slug, ct);
        if (existingOrg is not null)
            slug = $"{slug}-{Guid.NewGuid().ToString()[..8]}";

        var org = new Organization(
            Id: Guid.NewGuid(),
            Name: orgName,
            Slug: slug,
            IsActive: true,
            CreatedAtUtc: DateTimeOffset.UtcNow);
        await _orgs.CreateAsync(org, ct);

        var user = new UserIdentity(
            Id: Guid.NewGuid(),
            Email: email.ToLowerInvariant(),
            DisplayName: displayName,
            PasswordHash: HashPassword(password),
            OrganizationId: org.Id,
            Role: "Admin",
            IsActive: true,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            LastLoginAtUtc: DateTimeOffset.UtcNow);
        await _users.CreateAsync(user, ct);

        var membership = new Membership(
            Id: Guid.NewGuid(),
            UserId: user.Id,
            OrganizationId: org.Id,
            Role: "Admin",
            JoinedAtUtc: DateTimeOffset.UtcNow);
        await _memberships.CreateAsync(membership, ct);

        var tokens = await IssueTokensAsync(user, org, "Admin", ct);
        _logger.LogInformation("Registered org {OrgSlug} with admin {Email}", org.Slug, email);
        return (org, user, tokens);
    }

    public async Task<AuthTokens?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        string hash = HashToken(refreshToken);
        var stored = await _refreshTokens.GetByHashAsync(hash, ct);
        if (stored is null)
        {
            _logger.LogWarning("Refresh failed: token not found or expired");
            return null;
        }

        await _refreshTokens.RevokeAsync(stored.Id, ct);

        var user = await _users.GetByIdAsync(stored.UserId, ct);
        if (user is null || !user.IsActive)
            return null;

        var org = await _orgs.GetByIdAsync(user.OrganizationId, ct);
        if (org is null || !org.IsActive)
            return null;

        var membership = await _memberships.GetAsync(user.Id, org.Id, ct);
        string role = membership?.Role ?? user.Role;

        return await IssueTokensAsync(user, org, role, ct);
    }

    public async Task LogoutAsync(Guid userId, CancellationToken ct = default)
    {
        await _refreshTokens.RevokeAllForUserAsync(userId, ct);
        _logger.LogInformation("Logged out user {UserId}, all refresh tokens revoked", userId);
    }

    public async Task<(InviteToken Invite, string RawToken)?> InviteUserAsync(
        Guid orgId, string email, string role, Guid invitedBy,
        CancellationToken ct = default)
    {
        var org = await _orgs.GetByIdAsync(orgId, ct);
        if (org is null) return null;

        string rawToken = GenerateSecureToken();
        var invite = new InviteToken(
            Id: Guid.NewGuid(),
            OrganizationId: orgId,
            Email: email.ToLowerInvariant(),
            Role: role,
            TokenHash: HashToken(rawToken),
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(_options.InviteTokenLifetimeDays),
            CreatedAtUtc: DateTimeOffset.UtcNow,
            IsAccepted: false);
        await _invites.CreateAsync(invite, ct);

        _logger.LogInformation("Invited {Email} to org {OrgId} as {Role}", email, orgId, role);
        return (invite, rawToken);
    }

    public async Task<(UserIdentity User, AuthTokens Tokens)?> AcceptInviteAsync(
        string inviteToken, string password, string displayName,
        CancellationToken ct = default)
    {
        string hash = HashToken(inviteToken);
        var invite = await _invites.GetByHashAsync(hash, ct);
        if (invite is null) return null;

        var org = await _orgs.GetByIdAsync(invite.OrganizationId, ct);
        if (org is null || !org.IsActive) return null;

        var existingUser = await _users.GetByEmailAsync(invite.Email, ct);
        UserIdentity user;
        if (existingUser is not null)
        {
            user = existingUser;
        }
        else
        {
            user = new UserIdentity(
                Id: Guid.NewGuid(),
                Email: invite.Email,
                DisplayName: displayName,
                PasswordHash: HashPassword(password),
                OrganizationId: invite.OrganizationId,
                Role: invite.Role,
                IsActive: true,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                LastLoginAtUtc: DateTimeOffset.UtcNow);
            await _users.CreateAsync(user, ct);
        }

        var existingMembership = await _memberships.GetAsync(user.Id, invite.OrganizationId, ct);
        if (existingMembership is null)
        {
            await _memberships.CreateAsync(new Membership(
                Id: Guid.NewGuid(),
                UserId: user.Id,
                OrganizationId: invite.OrganizationId,
                Role: invite.Role,
                JoinedAtUtc: DateTimeOffset.UtcNow), ct);
        }

        await _invites.AcceptAsync(invite.Id, ct);

        var tokens = await IssueTokensAsync(user, org, invite.Role, ct);
        return (user, tokens);
    }

    private async Task<AuthTokens> IssueTokensAsync(
        UserIdentity user, Organization org, string role, CancellationToken ct)
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenLifetimeMinutes);
        string accessToken = _tokenService.GenerateAccessToken(user, org, role, expiry);

        string rawRefresh = GenerateSecureToken();
        var refreshToken = new RefreshToken(
            Id: Guid.NewGuid(),
            UserId: user.Id,
            TokenHash: HashToken(rawRefresh),
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenLifetimeDays),
            CreatedAtUtc: DateTimeOffset.UtcNow,
            IsRevoked: false);
        await _refreshTokens.CreateAsync(refreshToken, ct);

        return new AuthTokens(accessToken, rawRefresh, expiry);
    }

    internal static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt,
            iterations: 100_000, HashAlgorithmName.SHA256, outputLength: 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    internal static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 2) return false;
        byte[] salt = Convert.FromBase64String(parts[0]);
        byte[] expectedHash = Convert.FromBase64String(parts[1]);
        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt,
            iterations: 100_000, HashAlgorithmName.SHA256, outputLength: 32);
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    private static string HashToken(string token)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }

    private static string GenerateSecureToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private static string GenerateSlug(string name)
    {
        return new string(name.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == ' ')
            .ToArray())
            .Replace(' ', '-')
            .Trim('-');
    }
}
