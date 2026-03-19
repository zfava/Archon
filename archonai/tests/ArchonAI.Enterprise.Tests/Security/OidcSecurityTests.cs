using ArchonAI.Api.Dtos;
using ArchonAI.Identity;

namespace ArchonAI.Enterprise.Tests.Security;

/// <summary>
/// OIDC security tests: IdToken direct submission rejection and authorization code flow enforcement.
/// </summary>
public sealed class OidcSecurityTests
{
    // ── Test 1: Direct IdToken submission is rejected ────────────────────

    [Fact]
    public void OidcCallbackRequest_RequiresAuthorizationCode_NotIdToken()
    {
        // The OidcCallbackRequest record requires Code, State, CodeVerifier, OrganizationId.
        // There is NO IdToken parameter — the OIDC flow enforces authorization code exchange.
        // An attacker cannot submit a raw IdToken to bypass the server-side token exchange.

        var request = new OidcCallbackRequest(
            Code: "auth-code-from-idp",
            State: "random-state",
            CodeVerifier: "pkce-verifier",
            OrganizationId: Guid.NewGuid());

        // Verify the request has the correct fields for authorization code flow
        Assert.Equal("auth-code-from-idp", request.Code);
        Assert.Equal("random-state", request.State);
        Assert.Equal("pkce-verifier", request.CodeVerifier);
        Assert.NotEqual(Guid.Empty, request.OrganizationId);

        // Verify OidcCallbackRequest does NOT have an IdToken property.
        // This ensures clients cannot inject a raw IdToken directly.
        var properties = typeof(OidcCallbackRequest).GetProperties();
        var propertyNames = properties.Select(p => p.Name).ToList();

        Assert.DoesNotContain("IdToken", propertyNames);
        Assert.DoesNotContain("id_token", propertyNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Code", propertyNames);
        Assert.Contains("CodeVerifier", propertyNames);
    }

    // ── Test 2: PKCE code verifier is mandatory ─────────────────────────

    [Fact]
    public void OidcCallbackRequest_CodeVerifier_IsMandatoryForPkce()
    {
        // The authorization code flow uses PKCE (RFC 7636) which requires a code_verifier.
        // Without CodeVerifier, the token exchange with the IdP will fail.
        // This test verifies that CodeVerifier is a required constructor parameter.

        var constructors = typeof(OidcCallbackRequest).GetConstructors();
        Assert.Single(constructors);

        var parameters = constructors[0].GetParameters();
        var paramNames = parameters.Select(p => p.Name).ToList();

        // All four parameters are required (no default values)
        Assert.Equal(4, parameters.Length);
        Assert.Contains("Code", paramNames);
        Assert.Contains("State", paramNames);
        Assert.Contains("CodeVerifier", paramNames);
        Assert.Contains("OrganizationId", paramNames);

        // None have default values — all are mandatory
        Assert.All(parameters, p => Assert.False(p.HasDefaultValue,
            $"Parameter '{p.Name}' should not have a default value — it is security-critical"));

        // Verify PKCE state/nonce generation produces sufficient entropy
        var state1 = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        var state2 = OidcTokenExchangeService.GenerateOidcStateOrNonce();
        Assert.NotEqual(state1, state2);
        Assert.True(state1.Length >= 40, "PKCE state must have sufficient entropy (>=32 bytes base64)");
    }
}
