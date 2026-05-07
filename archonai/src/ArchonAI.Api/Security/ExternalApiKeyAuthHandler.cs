using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using ArchonAI.Core.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ArchonAI.Api.Security;

/// <summary>
/// Authentication handler for external API requests using X-Archon-Api-Key header.
/// Validates keys against ISecretProvider and sets org-id, api-key-id, rate-limit-tier claims.
/// </summary>
public sealed class ExternalApiKeyAuthHandler : AuthenticationHandler<ExternalApiKeyAuthOptions>
{
    public const string SchemeName = "ExternalApiKey";
    public const string ApiKeyHeader = "X-Archon-Api-Key";

    private readonly ISecretProvider _secretProvider;

    public ExternalApiKeyAuthHandler(
        IOptionsMonitor<ExternalApiKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISecretProvider secretProvider)
        : base(options, logger, encoder)
    {
        _secretProvider = secretProvider;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var apiKeyValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var apiKey = apiKeyValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey))
            return Task.FromResult(AuthenticateResult.Fail("API key header is empty."));

        // Derive a stable key ID from the API key for lookup and logging (never log the key itself)
        var keyId = DeriveKeyId(apiKey);

        // Validate: look up "external-api-key:<keyId>" in the secret provider.
        // The stored value format is "orgId:rateLimitTier" (e.g., "org-uuid:standard").
        var storedValue = _secretProvider.GetSecret($"external-api-key:{keyId}");
        if (storedValue is null)
        {
            // Fallback: check a single shared key from config (for bootstrapping / dev)
            var sharedKey = _secretProvider.GetSecret("external-api-shared-key");
            if (sharedKey is null || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(apiKey), Encoding.UTF8.GetBytes(sharedKey)))
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
            }

            // Shared key: extract org from X-Archon-Org-Id header (required with shared key)
            var orgHeader = Request.Headers["X-Archon-Org-Id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(orgHeader))
                return Task.FromResult(AuthenticateResult.Fail("X-Archon-Org-Id header is required with shared API key."));

            return Task.FromResult(AuthenticateResult.Success(
                BuildTicket(keyId, orgHeader, "standard")));
        }

        var parts = storedValue.Split(':', 2);
        var orgId = parts[0];
        var rateLimitTier = parts.Length > 1 ? parts[1] : "standard";

        return Task.FromResult(AuthenticateResult.Success(
            BuildTicket(keyId, orgId, rateLimitTier)));
    }

    private AuthenticationTicket BuildTicket(string keyId, string orgId, string rateLimitTier)
    {
        var claims = new[]
        {
            new Claim("org-id", orgId),
            new Claim("api-key-id", keyId),
            new Claim("rate-limit-tier", rateLimitTier),
            new Claim("tenant_id", orgId),
            new Claim(ClaimTypes.Name, $"external:{keyId}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, SchemeName);
    }

    private static string DeriveKeyId(string apiKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexStringLower(hash)[..16];
    }
}

public sealed class ExternalApiKeyAuthOptions : AuthenticationSchemeOptions { }
