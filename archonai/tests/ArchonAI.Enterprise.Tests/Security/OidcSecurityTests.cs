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
        // The OidcCallbackRequest record requires Code, State, OrganizationId.
        // There is NO IdToken parameter — the OIDC flow enforces authorization code exchange.
        // An attacker cannot submit a raw IdToken to bypass the server-side token exchange.

        var request = new OidcCallbackRequest(
            Code: "auth-code-from-idp",
            State: "random-state",
            OrganizationId: Guid.NewGuid());

        // Verify the request has the correct fields for authorization code flow
        Assert.Equal("auth-code-from-idp", request.Code);
        Assert.Equal("random-state", request.State);
        Assert.NotEqual(Guid.Empty, request.OrganizationId);

        // Verify OidcCallbackRequest does NOT have an IdToken or CodeVerifier property.
        // IdToken: clients cannot inject a raw IdToken directly.
        // CodeVerifier: held server-side, never client-supplied.
        var properties = typeof(OidcCallbackRequest).GetProperties();
        var propertyNames = properties.Select(p => p.Name).ToList();

        Assert.DoesNotContain("IdToken", propertyNames);
        Assert.DoesNotContain("id_token", propertyNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CodeVerifier", propertyNames);
        Assert.Contains("Code", propertyNames);
        Assert.Contains("State", propertyNames);
    }

    // ── Test 2: Callback parameters are minimal (Code + State + OrgId) ──

    [Fact]
    public void OidcCallbackRequest_HasMinimalClientSuppliedParameters()
    {
        // The callback request should only contain the authorization code,
        // the state (for server-side lookup), and the organization ID.
        // All security-sensitive values (nonce, code_verifier) are server-side.

        var constructors = typeof(OidcCallbackRequest).GetConstructors();
        Assert.Single(constructors);

        var parameters = constructors[0].GetParameters();
        var paramNames = parameters.Select(p => p.Name).ToList();

        // Exactly 3 parameters — Code, State, OrganizationId
        Assert.Equal(3, parameters.Length);
        Assert.Contains("Code", paramNames);
        Assert.Contains("State", paramNames);
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

    // ── Test 3: Login response does not leak nonce or code_verifier ─────

    [Fact]
    public void OidcLoginResponse_DoesNotExposeNonceOrCodeVerifier()
    {
        var properties = typeof(OidcLoginResponse).GetProperties();
        var propertyNames = properties.Select(p => p.Name).ToList();

        // Nonce and CodeVerifier must NOT be exposed to the client
        Assert.DoesNotContain("Nonce", propertyNames);
        Assert.DoesNotContain("CodeVerifier", propertyNames);

        // Should still expose AuthorizeUrl, State, OrganizationId
        Assert.Contains("AuthorizeUrl", propertyNames);
        Assert.Contains("State", propertyNames);
        Assert.Contains("OrganizationId", propertyNames);
    }
}
