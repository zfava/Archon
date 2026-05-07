using ArchonAI.Api.Dtos;
using ArchonAI.Core.Interfaces;

namespace ArchonAI.Api.Endpoints;

public static class MfaEndpoints
{
    public static IEndpointRouteBuilder MapMfaEndpoints(this IEndpointRouteBuilder app)
    {
        var mfa = app.MapGroup("/api/v1/auth/mfa")
            .RequireAuthorization()
            .RequireRateLimiting("api")
            .WithTags("mfa");

        mfa.MapGet("/status", async (
            HttpContext ctx,
            ArchonAI.Identity.Mfa.MfaChallengeService mfaChallengeService,
            CancellationToken ct) =>
        {
            var sub = ctx.User?.FindFirst("sub")?.Value;
            if (sub is null || !Guid.TryParse(sub, out var userId))
                return Results.Unauthorized();
            var status = await mfaChallengeService.GetMfaStatusAsync(userId, ct);
            return Results.Ok(status);
        });

        mfa.MapPost("/totp/setup", async (
            HttpContext ctx,
            ArchonAI.Identity.Mfa.TotpService totpService,
            CancellationToken ct) =>
        {
            var sub = ctx.User?.FindFirst("sub")?.Value;
            var email = ctx.User?.FindFirst("email")?.Value ?? ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            if (sub is null || !Guid.TryParse(sub, out var userId) || email is null)
                return Results.Unauthorized();

            var result = await totpService.GenerateSetupAsync(userId, email, ct);
            if (result is null)
                return Results.Conflict(new { error = "TOTP is already enabled. Disable it first." });

            return Results.Ok(new TotpSetupResponse(result.Value.OtpAuthUri, result.Value.RecoveryCodes));
        });

        mfa.MapPost("/totp/verify", async (
            HttpContext ctx,
            ArchonAI.Identity.Mfa.TotpService totpService,
            TotpVerifyRequest req,
            CancellationToken ct) =>
        {
            var sub = ctx.User?.FindFirst("sub")?.Value;
            if (sub is null || !Guid.TryParse(sub, out var userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(req.Code))
                return Results.BadRequest(new { error = "Code is required." });

            var verified = await totpService.VerifySetupAsync(userId, req.Code, ct);
            return verified ? Results.Ok(new { enrolled = true }) : Results.BadRequest(new { error = "Invalid code." });
        });

        mfa.MapDelete("/totp", async (
            HttpContext ctx,
            ArchonAI.Identity.Mfa.TotpService totpService,
            string code,
            CancellationToken ct) =>
        {
            var sub = ctx.User?.FindFirst("sub")?.Value;
            if (sub is null || !Guid.TryParse(sub, out var userId))
                return Results.Unauthorized();

            var disabled = await totpService.DisableAsync(userId, code, ct);
            return disabled ? Results.Ok(new { disabled = true }) : Results.BadRequest(new { error = "Invalid code." });
        });

        mfa.MapPost("/webauthn/register/begin", async (
            HttpContext ctx,
            ArchonAI.Identity.Mfa.WebAuthnService webAuthnService,
            CancellationToken ct) =>
        {
            var sub = ctx.User?.FindFirst("sub")?.Value;
            var email = ctx.User?.FindFirst("email")?.Value ?? ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            var name = ctx.User?.FindFirst("name")?.Value;
            if (sub is null || !Guid.TryParse(sub, out var userId) || email is null)
                return Results.Unauthorized();

            var options = await webAuthnService.BeginRegistrationAsync(userId, email, name ?? email, ct);
            return Results.Ok(options);
        });

        mfa.MapPost("/webauthn/register/complete", async (
            HttpContext ctx,
            ArchonAI.Identity.Mfa.WebAuthnService webAuthnService,
            WebAuthnRegisterCompleteRequest req,
            CancellationToken ct) =>
        {
            var sub = ctx.User?.FindFirst("sub")?.Value;
            if (sub is null || !Guid.TryParse(sub, out var userId))
                return Results.Unauthorized();

            var success = await webAuthnService.CompleteRegistrationAsync(
                userId, req.DisplayName ?? "Security Key",
                Convert.FromBase64String(req.CredentialId),
                Convert.FromBase64String(req.PublicKey),
                req.SignCount, ct);

            return success
                ? Results.Ok(new { registered = true })
                : Results.BadRequest(new { error = "Registration failed." });
        });

        mfa.MapDelete("/webauthn/{id:guid}", async (
            Guid id,
            HttpContext ctx,
            ArchonAI.Identity.Mfa.WebAuthnService webAuthnService,
            CancellationToken ct) =>
        {
            var sub = ctx.User?.FindFirst("sub")?.Value;
            if (sub is null || !Guid.TryParse(sub, out var userId))
                return Results.Unauthorized();

            var deleted = await webAuthnService.DeleteCredentialAsync(userId, id, ct);
            return deleted ? Results.Ok(new { deleted = true }) : Results.NotFound();
        });

        mfa.MapPost("/recovery/regenerate", async (
            HttpContext ctx,
            ArchonAI.Identity.Mfa.TotpService totpService,
            TotpVerifyRequest req,
            CancellationToken ct) =>
        {
            var sub = ctx.User?.FindFirst("sub")?.Value;
            var email = ctx.User?.FindFirst("email")?.Value ?? ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            if (sub is null || !Guid.TryParse(sub, out var userId) || email is null)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(req.Code))
                return Results.BadRequest(new { error = "Current TOTP code is required." });

            var verified = await totpService.VerifyAsync(userId, req.Code, ct);
            if (!verified)
                return Results.BadRequest(new { error = "Invalid code." });

            var result = await totpService.GenerateSetupAsync(userId, email, ct);
            if (result is null)
                return Results.BadRequest(new { error = "TOTP not enabled." });

            return Results.Ok(new { recoveryCodes = result.Value.RecoveryCodes });
        });

        mfa.MapGet("/policy", async (
            HttpContext ctx,
            IMfaStore mfaStore,
            CancellationToken ct) =>
        {
            var orgIdClaim = ctx.User?.FindFirst("org_id")?.Value;
            if (orgIdClaim is null || !Guid.TryParse(orgIdClaim, out var orgId))
                return Results.Unauthorized();

            var policy = await mfaStore.GetMfaPolicyAsync(orgId, ct);
            return Results.Ok(new MfaPolicyResponse(
                policy?.Mode.ToString().ToLowerInvariant() ?? "disabled"));
        });

        mfa.MapPut("/policy", async (
            HttpContext ctx,
            IMfaStore mfaStore,
            MfaPolicyRequest req,
            CancellationToken ct) =>
        {
            var orgIdClaim = ctx.User?.FindFirst("org_id")?.Value;
            if (orgIdClaim is null || !Guid.TryParse(orgIdClaim, out var orgId))
                return Results.Unauthorized();

            if (!Enum.TryParse<ArchonAI.Core.Models.Identity.MfaPolicyMode>(req.Mode, true, out var mode))
                return Results.BadRequest(new { error = "Invalid mode. Use: disabled, optional, or required." });

            await mfaStore.UpsertMfaPolicyAsync(new ArchonAI.Core.Models.Identity.MfaPolicy(
                orgId, mode, DateTimeOffset.UtcNow), ct);

            return Results.Ok(new MfaPolicyResponse(mode.ToString().ToLowerInvariant()));
        }).RequireAuthorization("AdminOnly");

        mfa.MapPost("/admin-reset/{targetUserId:guid}", async (
            Guid targetUserId,
            HttpContext ctx,
            ArchonAI.Identity.Mfa.TotpService totpService,
            IMfaStore mfaStore,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var adminSub = ctx.User?.FindFirst("sub")?.Value;
            if (adminSub is null || !Guid.TryParse(adminSub, out var adminId))
                return Results.Unauthorized();

            await mfaStore.DeleteTotpCredentialAsync(targetUserId, ct);
            await mfaStore.DeleteAllRecoveryCodesAsync(targetUserId, ct);
            var webAuthnCreds = await mfaStore.GetWebAuthnCredentialsAsync(targetUserId, ct);
            foreach (var cred in webAuthnCreds)
                await mfaStore.DeleteWebAuthnCredentialAsync(cred.Id, ct);

            var logger = loggerFactory.CreateLogger("ArchonAI.Api.Endpoints.MfaEndpoints");
            logger.LogWarning("MFA reset by admin {AdminId} for user {TargetUserId}", adminId, targetUserId);
            return Results.Ok(new { reset = true, targetUserId });
        }).RequireAuthorization("AdminOnly");

        return app;
    }
}
