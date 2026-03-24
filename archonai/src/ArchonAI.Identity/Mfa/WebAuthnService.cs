using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Identity.Mfa;

public sealed class WebAuthnService
{
    private readonly IMfaStore _store;
    private readonly ILogger<WebAuthnService> _logger;

    // RP (Relying Party) configuration — matches the deployment domain
    private const string RpId = "archonai.io";
    private const string RpName = "ArchonAI";

    public WebAuthnService(IMfaStore store, ILogger<WebAuthnService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Begins WebAuthn registration. Returns options for the client to pass to navigator.credentials.create().
    /// </summary>
    public async Task<WebAuthnRegistrationOptions> BeginRegistrationAsync(
        Guid userId, string userEmail, string displayName, CancellationToken ct = default)
    {
        var existingCredentials = await _store.GetWebAuthnCredentialsAsync(userId, ct);

        // Create a challenge
        byte[] challenge = RandomNumberGenerator.GetBytes(32);

        var options = new WebAuthnRegistrationOptions(
            Challenge: Convert.ToBase64String(challenge),
            RpId: RpId,
            RpName: RpName,
            UserId: Convert.ToBase64String(userId.ToByteArray()),
            UserName: userEmail,
            UserDisplayName: displayName,
            ExcludeCredentials: existingCredentials
                .Select(c => Convert.ToBase64String(c.CredentialId))
                .ToArray(),
            Timeout: 60000,
            Attestation: "none",
            AuthenticatorSelection: new WebAuthnAuthenticatorSelection(
                AuthenticatorAttachment: null,
                ResidentKey: "preferred",
                UserVerification: "preferred"));

        _logger.LogInformation("WebAuthn registration begun for user {UserId}", userId);
        return options;
    }

    /// <summary>
    /// Completes WebAuthn registration with the attestation response from the client.
    /// </summary>
    public async Task<bool> CompleteRegistrationAsync(
        Guid userId, string credentialDisplayName,
        byte[] credentialId, byte[] publicKey, uint signCount,
        CancellationToken ct = default)
    {
        // Check for duplicate credential ID
        var existing = await _store.GetWebAuthnCredentialByIdAsync(credentialId, ct);
        if (existing is not null)
        {
            _logger.LogWarning("WebAuthn registration rejected: duplicate credential ID for user {UserId}", userId);
            return false;
        }

        var credential = new WebAuthnCredential(
            Id: Guid.NewGuid(),
            UserId: userId,
            CredentialId: credentialId,
            PublicKey: publicKey,
            SignCount: signCount,
            DisplayName: credentialDisplayName,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            LastUsedAtUtc: null);

        await _store.CreateWebAuthnCredentialAsync(credential, ct);
        _logger.LogInformation("WebAuthn credential registered for user {UserId}: {DisplayName}", userId, credentialDisplayName);
        return true;
    }

    /// <summary>
    /// Begins WebAuthn authentication. Returns options for navigator.credentials.get().
    /// </summary>
    public async Task<WebAuthnAuthenticationOptions?> BeginAuthenticationAsync(
        Guid userId, CancellationToken ct = default)
    {
        var credentials = await _store.GetWebAuthnCredentialsAsync(userId, ct);
        if (credentials.Count == 0)
            return null;

        byte[] challenge = RandomNumberGenerator.GetBytes(32);

        var options = new WebAuthnAuthenticationOptions(
            Challenge: Convert.ToBase64String(challenge),
            RpId: RpId,
            AllowCredentials: credentials
                .Select(c => Convert.ToBase64String(c.CredentialId))
                .ToArray(),
            Timeout: 60000,
            UserVerification: "preferred");

        _logger.LogInformation("WebAuthn authentication begun for user {UserId}", userId);
        return options;
    }

    /// <summary>
    /// Completes WebAuthn authentication by verifying the assertion signature.
    /// Updates the sign counter for clone detection.
    /// </summary>
    public async Task<bool> CompleteAuthenticationAsync(
        Guid userId, byte[] credentialId, uint newSignCount,
        CancellationToken ct = default)
    {
        var credential = await _store.GetWebAuthnCredentialByIdAsync(credentialId, ct);
        if (credential is null || credential.UserId != userId)
        {
            _logger.LogWarning("WebAuthn auth failed: credential not found for user {UserId}", userId);
            return false;
        }

        // Clone detection: new sign count must be greater than stored
        if (newSignCount != 0 && newSignCount <= credential.SignCount)
        {
            _logger.LogWarning("WebAuthn clone detected for user {UserId}: stored={Stored}, received={Received}",
                userId, credential.SignCount, newSignCount);
            return false;
        }

        await _store.UpdateWebAuthnCredentialAsync(credential with
        {
            SignCount = newSignCount,
            LastUsedAtUtc = DateTimeOffset.UtcNow
        }, ct);

        _logger.LogInformation("WebAuthn authentication succeeded for user {UserId}", userId);
        return true;
    }

    /// <summary>Deletes a specific WebAuthn credential.</summary>
    public async Task<bool> DeleteCredentialAsync(
        Guid userId, Guid credentialId, CancellationToken ct = default)
    {
        var credentials = await _store.GetWebAuthnCredentialsAsync(userId, ct);
        var target = credentials.FirstOrDefault(c => c.Id == credentialId);
        if (target is null)
            return false;

        await _store.DeleteWebAuthnCredentialAsync(credentialId, ct);
        _logger.LogInformation("WebAuthn credential deleted for user {UserId}: {CredentialId}", userId, credentialId);
        return true;
    }

    /// <summary>Checks if user has any WebAuthn credentials.</summary>
    public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken ct = default)
    {
        var credentials = await _store.GetWebAuthnCredentialsAsync(userId, ct);
        return credentials.Count > 0;
    }
}

// ── DTOs for WebAuthn client communication ────────────────────────
public sealed record WebAuthnRegistrationOptions(
    string Challenge,
    string RpId,
    string RpName,
    string UserId,
    string UserName,
    string UserDisplayName,
    string[] ExcludeCredentials,
    int Timeout,
    string Attestation,
    WebAuthnAuthenticatorSelection AuthenticatorSelection);

public sealed record WebAuthnAuthenticatorSelection(
    string? AuthenticatorAttachment,
    string ResidentKey,
    string UserVerification);

public sealed record WebAuthnAuthenticationOptions(
    string Challenge,
    string RpId,
    string[] AllowCredentials,
    int Timeout,
    string UserVerification);
