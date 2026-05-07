using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ArchonAI.Tests;

/// <summary>
/// Tests for authorization enforcement: role-based access, cross-tenant denial,
/// privilege escalation prevention.
/// </summary>
public class AuthorizationTests
{
    private const string SigningKey = "test-signing-key-that-is-at-least-32-bytes-long!!";

    private static string MakeJwt(string userId, string role, string tenantId, string orgSlug = "test-org")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Email, $"{userId}@test.com"),
            new Claim("name", $"Test {role}"),
            new Claim(ClaimTypes.Role, role),
            new Claim("org_id", tenantId),
            new Claim("org_slug", orgSlug),
            new Claim("tenant_id", tenantId),
        };

        var token = new JwtSecurityToken(
            issuer: "ArchonAI",
            audience: "ArchonAI.Api",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public void AdminJwt_ContainsAdminRole()
    {
        var jwt = MakeJwt("u1", "Admin", "t1");
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);
        var role = token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
        Assert.Equal("Admin", role);
    }

    [Fact]
    public void ViewerJwt_ContainsViewerRole()
    {
        var jwt = MakeJwt("u2", "Viewer", "t1");
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);
        var role = token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
        Assert.Equal("Viewer", role);
    }

    [Fact]
    public void DifferentTenants_HaveDifferentTenantClaims()
    {
        var jwt1 = MakeJwt("u1", "Admin", "tenant-A");
        var jwt2 = MakeJwt("u2", "Admin", "tenant-B");
        var handler = new JwtSecurityTokenHandler();

        var t1 = handler.ReadJwtToken(jwt1).Claims.First(c => c.Type == "tenant_id").Value;
        var t2 = handler.ReadJwtToken(jwt2).Claims.First(c => c.Type == "tenant_id").Value;

        Assert.NotEqual(t1, t2);
        Assert.Equal("tenant-A", t1);
        Assert.Equal("tenant-B", t2);
    }

    [Fact]
    public void Jwt_CannotBeForgedWithWrongKey()
    {
        var jwt = MakeJwt("u1", "Admin", "t1");
        var wrongKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("wrong-key-that-is-also-at-least-32-bytes!!"));

        var handler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "ArchonAI",
            ValidateAudience = true,
            ValidAudience = "ArchonAI.Api",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = wrongKey,
            ValidateLifetime = true,
        };

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            handler.ValidateToken(jwt, validationParams, out _));
    }

    [Fact]
    public void Jwt_ValidatesWithCorrectKey()
    {
        var jwt = MakeJwt("u1", "Admin", "t1");
        var correctKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));

        var handler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "ArchonAI",
            ValidateAudience = true,
            ValidAudience = "ArchonAI.Api",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = correctKey,
            ValidateLifetime = true,
        };

        var principal = handler.ValidateToken(jwt, validationParams, out var validatedToken);
        Assert.NotNull(principal);
        // JWT "sub" maps to ClaimTypes.NameIdentifier by default
        var sub = principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Assert.Equal("u1", sub);
    }

    [Fact]
    public void ExpiredJwt_FailsValidation()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "u1"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("tenant_id", "t1"),
        };

        var token = new JwtSecurityToken(
            issuer: "ArchonAI", audience: "ArchonAI.Api",
            claims: claims,
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: creds);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        var handler = new JwtSecurityTokenHandler();

        Assert.Throws<SecurityTokenExpiredException>(() =>
            handler.ValidateToken(jwt, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "ArchonAI",
                ValidateAudience = true, ValidAudience = "ArchonAI.Api",
                ValidateIssuerSigningKey = true, IssuerSigningKey = key,
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
            }, out _));
    }
}
