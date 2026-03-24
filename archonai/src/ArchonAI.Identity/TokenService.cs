using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ArchonAI.Identity;

public sealed class TokenService
{
    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _audience;

    public TokenService(IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Security:Jwt");
        string signingKey = jwtSection["SigningKey"]
            ?? Environment.GetEnvironmentVariable("ARCHONAI_JWT_SIGNING_KEY")
            ?? throw new InvalidOperationException("JWT signing key not configured.");

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        _issuer = jwtSection["Issuer"] ?? "ArchonAI";
        _audience = jwtSection["Audience"] ?? "ArchonAI.Api";
    }

    public string GenerateAccessToken(
        UserIdentity user, Organization org, string role, DateTimeOffset expiresAt)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("name", user.DisplayName),
            new Claim("org_id", org.Id.ToString()),
            new Claim("org_slug", org.Slug),
            new Claim(ClaimTypes.Role, role),
            new Claim("tenant_id", org.Id.ToString()),
        };

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
