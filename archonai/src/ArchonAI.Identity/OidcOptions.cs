namespace ArchonAI.Identity;

/// <summary>
/// Configuration options for OIDC federation. Bound from the "Oidc" configuration section.
/// </summary>
public sealed class OidcOptions
{
    public const string SectionName = "Oidc";

    /// <summary>
    /// Base URL for constructing OIDC callback URLs (e.g., "https://app.archonai.com").
    /// </summary>
    public string CallbackBaseUrl { get; set; } = "https://localhost:5001";

    /// <summary>
    /// Path for OIDC callbacks. Combined with CallbackBaseUrl to form the redirect_uri.
    /// </summary>
    public string CallbackPath { get; set; } = "/api/v1/auth/oidc/callback";

    /// <summary>
    /// Maximum duration in seconds for an OIDC state parameter to remain valid.
    /// Prevents stale authorization requests from being replayed.
    /// </summary>
    public int StateExpirationSeconds { get; set; } = 300;

    /// <summary>
    /// URL to redirect the user to after successful OIDC authentication.
    /// </summary>
    public string PostLoginRedirectUrl { get; set; } = "/";
}
