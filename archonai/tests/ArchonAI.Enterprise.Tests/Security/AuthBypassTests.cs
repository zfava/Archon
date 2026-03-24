using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ArchonAI.Enterprise.Tests.Security;

/// <summary>
/// Security tests for authentication bypass attempts:
/// JWT forgery, expired token rejection, algorithm confusion,
/// missing claims, and token manipulation.
/// </summary>
public sealed class AuthBypassTests
{
    private const string ValidSigningKey = "test-signing-key-that-is-at-least-32-bytes-long!!";

    private static string MakeValidJwt(
        string userId = "u1", string role = "Admin", string tenantId = "t1",
        DateTime? expires = null, string? signingKey = null)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(signingKey ?? ValidSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Email, $"{userId}@test.com"),
            new Claim(ClaimTypes.Role, role),
            new Claim("tenant_id", tenantId),
            new Claim("org_id", tenantId),
        };

        var token = new JwtSecurityToken(
            issuer: "ArchonAI", audience: "ArchonAI.Api",
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-5),
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static TokenValidationParameters ValidParams(string? key = null) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = "ArchonAI",
        ValidateAudience = true,
        ValidAudience = "ArchonAI.Api",
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(key ?? ValidSigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
    };

    // ── Token Forgery ─────────────────────────────────────────────────

    [Fact]
    public void ForgedToken_WrongSigningKey_Rejected()
    {
        var jwt = MakeValidJwt(signingKey: "attacker-key-that-is-at-least-32-bytes-long!!");
        var handler = new JwtSecurityTokenHandler();

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            handler.ValidateToken(jwt, ValidParams(), out _));
    }

    [Fact]
    public void ManipulatedPayload_InvalidSignature_Rejected()
    {
        var jwt = MakeValidJwt();
        var parts = jwt.Split('.');

        // Tamper with the payload (change a character)
        var payload = parts[1];
        var tamperedPayload = payload[0] == 'a'
            ? "b" + payload[1..]
            : "a" + payload[1..];
        var tamperedJwt = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        var handler = new JwtSecurityTokenHandler();
        Assert.ThrowsAny<Exception>(() =>
            handler.ValidateToken(tamperedJwt, ValidParams(), out _));
    }

    // ── Expired Token ─────────────────────────────────────────────────

    [Fact]
    public void ExpiredToken_Rejected()
    {
        // Create token that's been expired for a long time to avoid clock skew
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "ArchonAI", audience: "ArchonAI.Api",
            claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, "u1"), new Claim("tenant_id", "t1") },
            notBefore: DateTime.UtcNow.AddHours(-3),
            expires: DateTime.UtcNow.AddHours(-2),
            signingCredentials: creds);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        var handler = new JwtSecurityTokenHandler();

        var paramsNoSkew = ValidParams();
        paramsNoSkew.ClockSkew = TimeSpan.Zero;

        Assert.ThrowsAny<SecurityTokenException>(() =>
            handler.ValidateToken(jwt, paramsNoSkew, out _));
    }

    [Fact]
    public void FutureToken_NotYetValid_Rejected()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "ArchonAI", audience: "ArchonAI.Api",
            claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, "u1") },
            notBefore: DateTime.UtcNow.AddHours(1),
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: creds);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        var handler = new JwtSecurityTokenHandler();

        var paramsNoSkew = ValidParams();
        paramsNoSkew.ClockSkew = TimeSpan.Zero;

        Assert.Throws<SecurityTokenNotYetValidException>(() =>
            handler.ValidateToken(jwt, paramsNoSkew, out _));
    }

    // ── Wrong Issuer / Audience ───────────────────────────────────────

    [Fact]
    public void WrongIssuer_Rejected()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "EvilCorp", audience: "ArchonAI.Api",
            claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, "u1") },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        var handler = new JwtSecurityTokenHandler();

        Assert.Throws<SecurityTokenInvalidIssuerException>(() =>
            handler.ValidateToken(jwt, ValidParams(), out _));
    }

    [Fact]
    public void WrongAudience_Rejected()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "ArchonAI", audience: "SomeOtherApi",
            claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, "u1") },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        var handler = new JwtSecurityTokenHandler();

        Assert.Throws<SecurityTokenInvalidAudienceException>(() =>
            handler.ValidateToken(jwt, ValidParams(), out _));
    }

    // ── Missing Required Claims ───────────────────────────────────────

    [Fact]
    public void ValidToken_ContainsTenantClaim()
    {
        var jwt = MakeValidJwt(tenantId: "my-tenant");
        var handler = new JwtSecurityTokenHandler();

        var principal = handler.ValidateToken(jwt, ValidParams(), out _);
        var tenantClaim = principal.FindFirst("tenant_id")?.Value;

        Assert.Equal("my-tenant", tenantClaim);
    }

    [Fact]
    public void TokenWithoutTenantClaim_PassesValidation_ButLacksClaim()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Token without tenant_id claim
        var token = new JwtSecurityToken(
            issuer: "ArchonAI", audience: "ArchonAI.Api",
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "u1"),
                new Claim(ClaimTypes.Role, "Admin"),
            },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        var handler = new JwtSecurityTokenHandler();

        var principal = handler.ValidateToken(jwt, ValidParams(), out _);
        var tenantClaim = principal.FindFirst("tenant_id");

        // Token is technically valid, but tenant claim is missing
        // Middleware must reject requests without tenant_id
        Assert.Null(tenantClaim);
    }

    // ── Role Elevation Attempt ────────────────────────────────────────

    [Fact]
    public void RoleInToken_CannotBeElevated_WithoutReissue()
    {
        var viewerJwt = MakeValidJwt(role: "Viewer");
        var handler = new JwtSecurityTokenHandler();

        var principal = handler.ValidateToken(viewerJwt, ValidParams(), out _);
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;

        Assert.Equal("Viewer", role);
        Assert.DoesNotContain(principal.Claims,
            c => c.Type == ClaimTypes.Role && c.Value == "Admin");
    }

    // ── Empty/Malformed Token ─────────────────────────────────────────

    [Fact]
    public void EmptyToken_Rejected()
    {
        var handler = new JwtSecurityTokenHandler();
        Assert.ThrowsAny<Exception>(() =>
            handler.ValidateToken("", ValidParams(), out _));
    }

    [Fact]
    public void GarbageToken_Rejected()
    {
        var handler = new JwtSecurityTokenHandler();
        Assert.ThrowsAny<Exception>(() =>
            handler.ValidateToken("not.a.jwt.token.at.all", ValidParams(), out _));
    }

    [Fact]
    public void NoneAlgorithm_Rejected()
    {
        // Craft a JWT with "alg":"none" - a classic bypass attempt
        var header = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("""{"sub":"admin","role":"Admin","tenant_id":"t1","exp":9999999999}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var unsignedJwt = $"{header}.{payload}.";

        var handler = new JwtSecurityTokenHandler();
        Assert.ThrowsAny<Exception>(() =>
            handler.ValidateToken(unsignedJwt, ValidParams(), out _));
    }
}
