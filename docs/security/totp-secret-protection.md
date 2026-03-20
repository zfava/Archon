# TOTP Secret Protection

This document describes how TOTP (Time-based One-Time Password) secrets are protected at rest in ArchonAI.

## Overview

TOTP secrets are 20-byte random values (Base32-encoded) that serve as the shared secret between the user's authenticator app and the server. Because these secrets can be used to generate valid TOTP codes indefinitely, they must be encrypted at rest with a key that is:

1. **Dedicated** — not shared with other cryptographic operations (e.g., JWT signing)
2. **Rotation-safe** — key rotation does not break previously enrolled users
3. **Authenticated** — ciphertext tampering is detected before decryption

## Encryption Design

### Algorithm

- **AES-256-CBC** with **HMAC-SHA256** (Encrypt-then-MAC)
- Key derivation: **HKDF-SHA256** from the raw secret, producing separate 256-bit encryption and MAC keys
- IV: 16 random bytes per credential (generated via `RandomNumberGenerator`)

### Key Material

| Environment Variable | Purpose |
|---------------------|---------|
| `ARCHONAI_TOTP_ENCRYPTION_KEY` | **Primary** — dedicated TOTP encryption key |
| `ARCHONAI_JWT_SIGNING_KEY` | **Fallback** — used if TOTP key is not set, and for decrypting legacy ciphertexts |

In production, always set `ARCHONAI_TOTP_ENCRYPTION_KEY` to decouple TOTP encryption from JWT signing-key lifecycle.

### Key Derivation

The raw key material is never used directly as an AES key. Instead, HKDF-SHA256 derives two independent 256-bit keys:

```
HKDF(SHA256, ikm=raw_key, info="archonai-totp-enc") → 256-bit AES encryption key
HKDF(SHA256, ikm=raw_key, info="archonai-totp-mac") → 256-bit HMAC-SHA256 MAC key
```

This provides domain separation: even if the same raw key is used, the encryption and MAC keys are cryptographically independent.

### Ciphertext Format

```
v1:{Base64(IV)}.{Base64(Ciphertext)}.{Base64(HMAC)}
```

- `v1:` — key version prefix (enables future key rotation)
- IV — 16 random bytes
- Ciphertext — AES-256-CBC encrypted TOTP secret
- HMAC — HMAC-SHA256 over `IV || Ciphertext` (Encrypt-then-MAC)

### Decryption Flow

1. Check for version prefix (`v1:`)
2. If present: extract IV, ciphertext, HMAC; verify HMAC with time-constant comparison; decrypt
3. If absent: treat as legacy format, decrypt using SHA256(JWT key) as AES key

## Legacy Migration

### Background

Prior to this change, TOTP secrets were encrypted using `SHA256(ARCHONAI_JWT_SIGNING_KEY)` as the AES key, with no HMAC authentication. This created two problems:

1. JWT key rotation would break TOTP decryption for previously enrolled users
2. No ciphertext authentication — tampering could not be detected

### Migration Behavior

Legacy ciphertexts (format: `{Base64(IV)}.{Base64(Ciphertext)}`, no version prefix) are handled transparently:

1. **Detection**: `ITotpSecretEncryptor.IsLegacyEncrypted()` checks for the absence of a `v1:` prefix
2. **Decryption**: Legacy secrets are decrypted using `SHA256(ARCHONAI_JWT_SIGNING_KEY)` — the old method
3. **Re-encryption**: On the next successful TOTP verification (login or setup confirmation), the secret is re-encrypted with the dedicated key and stored in the `v1:` format
4. **No downtime**: Migration happens lazily — users are never locked out

### Migration Timeline

- **Immediate**: New enrollments use the dedicated key
- **Gradual**: Existing users are migrated on their next successful TOTP verification
- **Verification**: Monitor logs for `"Migrated TOTP secret from legacy JWT-derived encryption"` messages

## Interface

```csharp
public interface ITotpSecretEncryptor
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
    bool IsLegacyEncrypted(string ciphertext);
}
```

- `Encrypt` always produces `v1:` format with authenticated encryption
- `Decrypt` auto-detects legacy vs. versioned format
- `IsLegacyEncrypted` returns true for pre-migration ciphertexts

## Security Properties

| Property | Status |
|----------|--------|
| Encryption at rest | AES-256-CBC |
| Authenticated encryption | HMAC-SHA256 (Encrypt-then-MAC) |
| Key derivation | HKDF-SHA256 with domain separation |
| Random IVs | 16 bytes per credential |
| JWT key independence | Dedicated key decoupled from JWT lifecycle |
| Ciphertext versioning | `v1:` prefix for future key rotation |
| Legacy backward compatibility | Transparent lazy migration |
| Time-constant MAC comparison | `CryptographicOperations.FixedTimeEquals` |
