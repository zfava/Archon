namespace ArchonAI.Core.Models.Identity;

/// <summary>
/// Server-side record binding an OIDC login initiation to its callback.
/// Stores the state, nonce, code_verifier, and organization context so the
/// callback can validate all values against trusted originals rather than
/// trusting client-supplied parameters.
/// </summary>
public sealed record OidcLoginSession(
    /// <summary>The OIDC state parameter — used as the lookup key.</summary>
    string State,

    /// <summary>The nonce sent in the authorize URL. Validated against the id_token nonce claim.</summary>
    string Nonce,

    /// <summary>The PKCE code_verifier used in the back-channel token exchange.</summary>
    string CodeVerifier,

    /// <summary>The organization that initiated the login.</summary>
    Guid OrganizationId,

    /// <summary>When this session was created (UTC).</summary>
    DateTimeOffset CreatedAtUtc,

    /// <summary>Absolute expiration time (UTC). Callbacks after this time are rejected.</summary>
    DateTimeOffset ExpiresAtUtc);
