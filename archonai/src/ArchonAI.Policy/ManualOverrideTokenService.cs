using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchonAI.Policy.Models;

namespace ArchonAI.Policy;

/// <summary>
/// Issues and validates HMAC-signed manual override tokens.
/// Tokens are structured as base64url "payload.signature" — the payload is a JSON-serialized
/// ManualOverrideToken and the signature is HMACSHA256(payload, signingKey).
/// </summary>
public static class ManualOverrideTokenService
{
    private static readonly TimeSpan MaxTokenLifetime = TimeSpan.FromHours(1);

    public static string IssueToken(ManualOverrideToken token, string signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
            throw new ArgumentException("Signing key must not be empty.", nameof(signingKey));

        string payloadJson = JsonSerializer.Serialize(token);
        string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        string signature = ComputeSignature(payloadB64, signingKey);

        return $"{payloadB64}.{signature}";
    }

    public static ManualOverrideToken? ValidateToken(string token, string signingKey)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(signingKey))
            return null;

        var dotIndex = token.LastIndexOf('.');
        if (dotIndex <= 0 || dotIndex >= token.Length - 1)
            return null;

        string payloadB64 = token[..dotIndex];
        string providedSignature = token[(dotIndex + 1)..];

        string expectedSignature = ComputeSignature(payloadB64, signingKey);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSignature),
                Encoding.UTF8.GetBytes(providedSignature)))
        {
            return null;
        }

        ManualOverrideToken? parsed;
        try
        {
            byte[] payloadBytes = Base64UrlDecode(payloadB64);
            parsed = JsonSerializer.Deserialize<ManualOverrideToken>(payloadBytes);
        }
        catch
        {
            return null;
        }

        if (parsed is null)
            return null;

        // Enforce maximum lifetime of 1 hour from issuance
        if (parsed.ExpiresAtUtc - parsed.IssuedAtUtc > MaxTokenLifetime)
            return null;

        // Enforce expiration
        if (DateTimeOffset.UtcNow >= parsed.ExpiresAtUtc)
            return null;

        // Validate action is a known value
        if (!parsed.Action.Equals("allow", StringComparison.OrdinalIgnoreCase)
            && !parsed.Action.Equals("deny", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return parsed;
    }

    private static string ComputeSignature(string payloadB64, string signingKey)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(signingKey);
        byte[] dataBytes = Encoding.UTF8.GetBytes(payloadB64);
        byte[] hash = HMACSHA256.HashData(keyBytes, dataBytes);
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        string padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
