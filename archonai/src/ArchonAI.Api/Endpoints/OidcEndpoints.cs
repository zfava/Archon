using ArchonAI.Api.Dtos;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using ArchonAI.Identity;
using ArchonAI.Identity.Stores;
using Microsoft.Extensions.Options;

namespace ArchonAI.Api.Endpoints;

public static class OidcEndpoints
{
    public static IEndpointRouteBuilder MapOidcEndpoints(this IEndpointRouteBuilder app)
    {
        var oidc = app.MapGroup("/api/v1/auth/oidc")
            .AllowAnonymous()
            .RequireRateLimiting("api")
            .WithTags("oidc");

        oidc.MapGet("/login", async (
            string tenant,
            ITenantAuthConfigStore tenantAuthConfigStore,
            IOrganizationStore orgStore,
            IOptions<OidcOptions> oidcOptions,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(tenant))
                return Results.BadRequest(new { error = "Tenant slug is required." });

            var org = await orgStore.GetBySlugAsync(tenant, ct);
            if (org is null)
                return Results.NotFound(new { error = "Tenant not found." });

            var config = await tenantAuthConfigStore.GetByOrganizationIdAsync(org.Id, ct);
            if (config is null || !config.IsEnabled)
                return Results.BadRequest(new { error = "No OIDC provider configured for this tenant." });

            string codeVerifier = OidcTokenExchangeService.GenerateOidcStateOrNonce();
            byte[] challengeBytes;
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                challengeBytes = sha256.ComputeHash(System.Text.Encoding.ASCII.GetBytes(codeVerifier));
            }
            string codeChallenge = Convert.ToBase64String(challengeBytes)
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

            string state = OidcTokenExchangeService.GenerateOidcStateOrNonce();
            string nonce = OidcTokenExchangeService.GenerateOidcStateOrNonce();

            var opts = oidcOptions.Value;
            string redirectUri = $"{opts.CallbackBaseUrl.TrimEnd('/')}{opts.CallbackPath}";
            string scopes = string.Join(" ", config.Scopes.Length > 0 ? config.Scopes : new[] { "openid", "profile", "email" });
            string authorizeUrl = $"{config.Authority.TrimEnd('/')}/authorize"
                + $"?client_id={Uri.EscapeDataString(config.ClientId)}"
                + $"&response_type=code"
                + $"&scope={Uri.EscapeDataString(scopes)}"
                + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                + $"&state={Uri.EscapeDataString(state)}"
                + $"&nonce={Uri.EscapeDataString(nonce)}"
                + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
                + $"&code_challenge_method=S256";

            // PKCE design: The CodeVerifier is intentionally returned to the client. The client holds it
            // in memory and sends it back in the /callback request. The server then includes it in the
            // back-channel token exchange with the IdP, which verifies it against the code_challenge
            // that was sent in this /authorize URL. This is the standard PKCE flow per RFC 7636.
            return Results.Ok(new OidcLoginResponse(
                authorizeUrl, state, nonce, codeVerifier, org.Id));
        });

        oidc.MapPost("/callback", async (
            OidcCallbackRequest req,
            OidcTokenExchangeService exchangeService,
            ITenantAuthConfigStore tenantAuthConfigStore,
            IHttpClientFactory httpClientFactory,
            IOptions<OidcOptions> oidcOptions,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("ArchonAI.Api.Endpoints.OidcEndpoints");
            if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.State) || req.OrganizationId == Guid.Empty)
                return Results.BadRequest(new { error = "Code, state, and organizationId are required." });

            if (string.IsNullOrWhiteSpace(req.CodeVerifier))
                return Results.BadRequest(new { error = "CodeVerifier is required for PKCE flow." });

            var config = await tenantAuthConfigStore.GetByOrganizationIdAsync(req.OrganizationId, ct);
            if (config is null || !config.IsEnabled)
                return Results.BadRequest(new { error = "No OIDC provider configured or enabled for this organization." });

            var opts = oidcOptions.Value;
            string redirectUri = $"{opts.CallbackBaseUrl.TrimEnd('/')}{opts.CallbackPath}";
            string tokenEndpoint = $"{config.Authority.TrimEnd('/')}/token";

            var tokenRequestParams = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = req.Code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["code_verifier"] = req.CodeVerifier,
            };

            using var httpClient = httpClientFactory.CreateClient();
            using var tokenResponse = await httpClient.PostAsync(
                tokenEndpoint,
                new FormUrlEncodedContent(tokenRequestParams),
                ct);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorBody = await tokenResponse.Content.ReadAsStringAsync(ct);
                logger.LogWarning(
                    "OIDC token exchange failed for org {OrgId}: HTTP {StatusCode} from {TokenEndpoint}: {Error}",
                    req.OrganizationId, (int)tokenResponse.StatusCode, tokenEndpoint, errorBody);
                return Results.BadRequest(new { error = "Authorization code exchange failed. The code may be expired or invalid." });
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(ct);
            using var tokenDoc = System.Text.Json.JsonDocument.Parse(tokenJson);
            var root = tokenDoc.RootElement;

            if (!root.TryGetProperty("id_token", out var idTokenElement))
            {
                logger.LogWarning("OIDC token response missing id_token for org {OrgId}", req.OrganizationId);
                return Results.BadRequest(new { error = "IdP token response did not include an id_token." });
            }

            string idToken = idTokenElement.GetString()!;

            string expectedNonce;
            try
            {
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(idToken);
                expectedNonce = jwt.Payload.Nonce ?? string.Empty;
            }
            catch
            {
                return Results.BadRequest(new { error = "Failed to read id_token from IdP response." });
            }

            var result = await exchangeService.ExchangeAsync(idToken, expectedNonce, req.OrganizationId, ct);
            if (result is null)
                return Results.Unauthorized();

            return Results.Ok(new OidcCallbackResponse(
                result.Tokens.AccessToken,
                result.Tokens.RefreshToken,
                result.Tokens.ExpiresAtUtc,
                new UserInfo(result.User.Id, result.User.Email, result.User.DisplayName, result.User.Role),
                new OrgInfo(result.Organization.Id, result.Organization.Name, result.Organization.Slug),
                result.IsNewUser));
        });

        oidc.MapGet("/providers/{tenant}", async (
            string tenant,
            ITenantAuthConfigStore tenantAuthConfigStore,
            IOrganizationStore orgStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(tenant))
                return Results.BadRequest(new { error = "Tenant slug is required." });

            var org = await orgStore.GetBySlugAsync(tenant, ct);
            if (org is null)
                return Results.NotFound(new { error = "Tenant not found." });

            var config = await tenantAuthConfigStore.GetByOrganizationIdAsync(org.Id, ct);
            if (config is null)
                return Results.Ok(new { hasOidc = false, providerType = (string?)null });

            return Results.Ok(new
            {
                hasOidc = config.IsEnabled,
                providerType = config.ProviderType.ToString(),
                authority = config.Authority,
                autoProvision = config.AutoProvision
            });
        });

        return app;
    }

    public static IEndpointRouteBuilder MapTenantAuthEndpoints(this IEndpointRouteBuilder admin)
    {
        var tenantAuth = admin.MapGroup("/auth");

        tenantAuth.MapGet("/configs", async (
            ITenantAuthConfigStore configStore,
            CancellationToken ct) =>
        {
            var configs = await configStore.ListAsync(ct);
            return Results.Ok(configs.Select(c => new TenantAuthConfigResponse(
                c.Id, c.OrganizationId, c.ProviderType.ToString(), c.Authority,
                c.ClientId, c.Domain, c.Scopes, c.AutoProvision, c.DefaultRole, c.IsEnabled,
                c.CreatedAtUtc, c.UpdatedAtUtc)));
        });

        tenantAuth.MapGet("/configs/{orgId:guid}", async (
            Guid orgId,
            ITenantAuthConfigStore configStore,
            CancellationToken ct) =>
        {
            var config = await configStore.GetByOrganizationIdAsync(orgId, ct);
            if (config is null)
                return Results.NotFound(new { error = "No auth config for this organization." });

            return Results.Ok(new TenantAuthConfigResponse(
                config.Id, config.OrganizationId, config.ProviderType.ToString(), config.Authority,
                config.ClientId, config.Domain, config.Scopes, config.AutoProvision, config.DefaultRole,
                config.IsEnabled, config.CreatedAtUtc, config.UpdatedAtUtc));
        });

        tenantAuth.MapPost("/configs", async (
            CreateTenantAuthConfigRequest req,
            ITenantAuthConfigStore configStore,
            IOrganizationStore orgStore,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<OidcProviderType>(req.ProviderType, true, out var providerType))
                return Results.BadRequest(new { error = $"Invalid provider type. Must be one of: {string.Join(", ", Enum.GetNames<OidcProviderType>())}" });

            var org = await orgStore.GetByIdAsync(req.OrganizationId, ct);
            if (org is null)
                return Results.NotFound(new { error = "Organization not found." });

            var existing = await configStore.GetByOrganizationIdAsync(req.OrganizationId, ct);
            if (existing is not null)
                return Results.Conflict(new { error = "Auth config already exists for this organization. Use PUT to update." });

            var config = new TenantAuthConfig(
                Id: Guid.NewGuid(),
                OrganizationId: req.OrganizationId,
                ProviderType: providerType,
                Authority: req.Authority,
                ClientId: req.ClientId,
                ClientSecret: req.ClientSecret,
                Domain: req.Domain,
                Scopes: req.Scopes ?? new[] { "openid", "profile", "email" },
                AutoProvision: req.AutoProvision,
                DefaultRole: req.DefaultRole ?? "Viewer",
                IsEnabled: req.IsEnabled,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedAtUtc: null);
            await configStore.CreateAsync(config, ct);

            return Results.Created($"/api/v1/admin/auth/configs/{req.OrganizationId}", new TenantAuthConfigResponse(
                config.Id, config.OrganizationId, config.ProviderType.ToString(), config.Authority,
                config.ClientId, config.Domain, config.Scopes, config.AutoProvision, config.DefaultRole,
                config.IsEnabled, config.CreatedAtUtc, config.UpdatedAtUtc));
        });

        tenantAuth.MapPut("/configs/{configId:guid}", async (
            Guid configId,
            UpdateTenantAuthConfigRequest req,
            ITenantAuthConfigStore configStore,
            CancellationToken ct) =>
        {
            var existing = await configStore.GetByIdAsync(configId, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Auth config not found." });

            OidcProviderType providerType = existing.ProviderType;
            if (req.ProviderType is not null && !Enum.TryParse(req.ProviderType, true, out providerType))
                return Results.BadRequest(new { error = "Invalid provider type." });

            var updated = existing with
            {
                ProviderType = providerType,
                Authority = req.Authority ?? existing.Authority,
                ClientId = req.ClientId ?? existing.ClientId,
                ClientSecret = req.ClientSecret ?? existing.ClientSecret,
                Domain = req.Domain ?? existing.Domain,
                Scopes = req.Scopes ?? existing.Scopes,
                AutoProvision = req.AutoProvision ?? existing.AutoProvision,
                DefaultRole = req.DefaultRole ?? existing.DefaultRole,
                IsEnabled = req.IsEnabled ?? existing.IsEnabled,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await configStore.UpdateAsync(updated, ct);

            return Results.Ok(new TenantAuthConfigResponse(
                updated.Id, updated.OrganizationId, updated.ProviderType.ToString(), updated.Authority,
                updated.ClientId, updated.Domain, updated.Scopes, updated.AutoProvision, updated.DefaultRole,
                updated.IsEnabled, updated.CreatedAtUtc, updated.UpdatedAtUtc));
        });

        tenantAuth.MapDelete("/configs/{configId:guid}", async (
            Guid configId,
            ITenantAuthConfigStore configStore,
            CancellationToken ct) =>
        {
            var existing = await configStore.GetByIdAsync(configId, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Auth config not found." });

            await configStore.DeleteAsync(configId, ct);
            return Results.NoContent();
        });

        tenantAuth.MapGet("/links/{orgId:guid}", async (
            Guid orgId,
            IExternalIdentityLinkStore linkStore,
            CancellationToken ct) =>
        {
            var links = await linkStore.ListByOrganizationAsync(orgId, ct);
            return Results.Ok(links);
        });

        return admin;
    }
}
