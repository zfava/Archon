namespace ArchonAI.Identity;

/// <summary>
/// SAML 2.0 authentication handler interface. Defined for future enterprise SSO
/// requirements where OIDC is not available.
/// </summary>
public interface ISamlAuthenticationHandler
{
    Task<SamlAuthenticationResult> AuthenticateAsync(string samlResponse, CancellationToken ct = default);
    Task<string> GenerateAuthnRequestAsync(string entityId, string assertionConsumerServiceUrl, CancellationToken ct = default);
}

public sealed record SamlAuthenticationResult(
    bool IsAuthenticated,
    string? Subject,
    string? Email,
    string? DisplayName,
    string? Issuer,
    IDictionary<string, string>? Attributes);

/// <summary>
/// Stub implementation of SAML 2.0 authentication. Returns safe defaults directing
/// consumers to use OIDC federation instead.
/// </summary>
public sealed class NotImplementedSamlHandler : ISamlAuthenticationHandler
{
    public Task<SamlAuthenticationResult> AuthenticateAsync(string samlResponse, CancellationToken ct = default)
    {
        return Task.FromResult(new SamlAuthenticationResult(false, null, null, null, null, null));
    }

    public Task<string> GenerateAuthnRequestAsync(string entityId, string assertionConsumerServiceUrl, CancellationToken ct = default)
    {
        return Task.FromResult("SAML 2.0 is not yet supported. Use OIDC federation instead.");
    }
}
