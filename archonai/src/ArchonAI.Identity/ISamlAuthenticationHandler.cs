namespace ArchonAI.Identity;

/// <summary>
/// SAML 2.0 authentication handler interface. Defined for future enterprise SSO
/// requirements where OIDC is not available. Currently stubbed with NotImplementedException.
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
/// Stub implementation of SAML 2.0 authentication. All methods throw NotImplementedException
/// with a clear message directing consumers to use OIDC federation instead.
/// </summary>
public sealed class NotImplementedSamlHandler : ISamlAuthenticationHandler
{
    public Task<SamlAuthenticationResult> AuthenticateAsync(string samlResponse, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "SAML 2.0 authentication is not yet implemented. Use OIDC federation (Okta, Microsoft Entra ID, Auth0) instead.");
    }

    public Task<string> GenerateAuthnRequestAsync(string entityId, string assertionConsumerServiceUrl, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "SAML 2.0 authentication is not yet implemented. Use OIDC federation (Okta, Microsoft Entra ID, Auth0) instead.");
    }
}
