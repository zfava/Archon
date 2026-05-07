using ArchonAI.Api.Dtos;
using ArchonAI.Core.Interfaces;
using ArchonAI.Identity;
using ArchonAI.Identity.Stores;

namespace ArchonAI.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth")
            .AllowAnonymous()
            .RequireRateLimiting("api")
            .WithTags("auth");

        auth.MapPost("/register", async (
            AuthenticationService authService,
            RegisterRequest req,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password)
                || string.IsNullOrWhiteSpace(req.OrganizationName) || string.IsNullOrWhiteSpace(req.DisplayName))
                return Results.BadRequest(new { error = "All fields are required." });

            if (req.Password.Length < 8)
                return Results.BadRequest(new { error = "Password must be at least 8 characters." });

            var result = await authService.RegisterAsync(req.OrganizationName, req.Email, req.Password, req.DisplayName, ct);
            if (result is null)
                return Results.Conflict(new { error = "Email already registered." });

            var (org, user, tokens) = result.Value;
            return Results.Ok(new AuthResponse(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAtUtc,
                new UserInfo(user.Id, user.Email, user.DisplayName, user.Role),
                new OrgInfo(org.Id, org.Name, org.Slug)));
        });

        auth.MapPost("/login", async (
            AuthenticationService authService,
            ArchonAI.Identity.Mfa.MfaChallengeService mfaChallengeService,
            LoginRequest req,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { error = "Email and password are required." });

            var result = await authService.LoginAsync(req.Email, req.Password, ct);
            if (result is null)
                return Results.Unauthorized();

            if (result.User is not null && await mfaChallengeService.HasMfaEnabledAsync(result.User.Id, ct))
            {
                var challenge = await mfaChallengeService.CreateChallengeAsync(result.User.Id, ct);
                if (challenge is not null)
                {
                    return Results.Ok(new MfaLoginResponse(
                        MfaRequired: true,
                        MfaToken: challenge.Value.MfaToken,
                        Methods: challenge.Value.Methods,
                        User: new UserInfo(result.User.Id, result.User.Email, result.User.DisplayName, result.User.Role)));
                }
            }

            return Results.Ok(new AuthResponse(result.Tokens!.AccessToken, result.Tokens.RefreshToken, result.Tokens.ExpiresAtUtc,
                new UserInfo(result.User!.Id, result.User.Email, result.User.DisplayName, result.User.Role),
                new OrgInfo(result.Org!.Id, result.Org.Name, result.Org.Slug)));
        });

        auth.MapPost("/refresh", async (
            AuthenticationService authService,
            RefreshRequest req,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.RefreshToken))
                return Results.BadRequest(new { error = "Refresh token is required." });

            var tokens = await authService.RefreshAsync(req.RefreshToken, ct);
            if (tokens is null)
                return Results.Unauthorized();

            return Results.Ok(new { accessToken = tokens.AccessToken, refreshToken = tokens.RefreshToken, expiresAtUtc = tokens.ExpiresAtUtc });
        });

        auth.MapPost("/logout", async (
            AuthenticationService authService,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var sub = ctx.User?.FindFirst("sub")?.Value;
            if (sub is not null && Guid.TryParse(sub, out var userId))
                await authService.LogoutAsync(userId, ct);
            return Results.Ok(new { message = "Logged out." });
        }).RequireAuthorization();

        auth.MapGet("/me", async (
            HttpContext ctx,
            IUserStore userStore,
            IOrganizationStore orgStore,
            IMembershipStore membershipStore,
            CancellationToken ct) =>
        {
            var sub = ctx.User?.FindFirst("sub")?.Value;
            if (sub is null || !Guid.TryParse(sub, out var userId))
                return Results.Unauthorized();

            var user = await userStore.GetByIdAsync(userId, ct);
            if (user is null) return Results.Unauthorized();

            var org = await orgStore.GetByIdAsync(user.OrganizationId, ct);
            if (org is null) return Results.Unauthorized();

            var membership = await membershipStore.GetAsync(user.Id, org.Id, ct);
            string role = membership?.Role ?? user.Role;

            return Results.Ok(new MeResponse(
                new UserInfo(user.Id, user.Email, user.DisplayName, role),
                new OrgInfo(org.Id, org.Name, org.Slug)));
        }).RequireAuthorization();

        auth.MapPost("/invite", async (
            AuthenticationService authService,
            HttpContext ctx,
            InviteRequest req,
            CancellationToken ct) =>
        {
            var orgIdClaim = ctx.User?.FindFirst("org_id")?.Value;
            var subClaim = ctx.User?.FindFirst("sub")?.Value;
            if (orgIdClaim is null || !Guid.TryParse(orgIdClaim, out var orgId)
                || subClaim is null || !Guid.TryParse(subClaim, out var userId))
                return Results.Unauthorized();

            var result = await authService.InviteUserAsync(orgId, req.Email, req.Role ?? "Operator", userId, ct);
            if (result is null)
                return Results.NotFound(new { error = "Organization not found." });

            return Results.Ok(new { inviteToken = result.Value.RawToken, expiresAtUtc = result.Value.Invite.ExpiresAtUtc });
        }).RequireAuthorization("AdminOnly");

        auth.MapPost("/accept-invite", async (
            AuthenticationService authService,
            AcceptInviteRequest req,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.InviteToken) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { error = "Invite token and password are required." });

            var result = await authService.AcceptInviteAsync(req.InviteToken, req.Password, req.DisplayName ?? "User", ct);
            if (result is null)
                return Results.BadRequest(new { error = "Invalid or expired invite." });

            return Results.Ok(new { accessToken = result.Value.Tokens.AccessToken, refreshToken = result.Value.Tokens.RefreshToken });
        });

        // MFA challenge endpoint (unauthenticated — uses MFA token)
        auth.MapPost("/mfa/challenge", async (
            AuthenticationService authService,
            ArchonAI.Identity.Mfa.MfaChallengeService mfaChallengeService,
            ArchonAI.Identity.Mfa.TotpService totpService,
            MfaChallengeRequest req,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.MfaToken))
                return Results.BadRequest(new { error = "MFA token is required." });

            var userId = await mfaChallengeService.ValidateChallengeAsync(req.MfaToken, ct);
            if (userId is null)
                return Results.Unauthorized();

            bool verified = false;
            if (req.Method == "totp" && !string.IsNullOrWhiteSpace(req.Code))
                verified = await totpService.VerifyAsync(userId.Value, req.Code, ct);
            else if (req.Method == "recovery" && !string.IsNullOrWhiteSpace(req.Code))
                verified = await mfaChallengeService.VerifyRecoveryCodeAsync(userId.Value, req.Code, ct);

            if (!verified)
                return Results.Unauthorized();

            var tokens = await authService.IssueTokensForUserAsync(userId.Value, ct);
            if (tokens is null)
                return Results.Unauthorized();

            var (t, user, org) = tokens.Value;
            return Results.Ok(new AuthResponse(t.AccessToken, t.RefreshToken, t.ExpiresAtUtc,
                new UserInfo(user.Id, user.Email, user.DisplayName, user.Role),
                new OrgInfo(org.Id, org.Name, org.Slug)));
        });

        return app;
    }
}
