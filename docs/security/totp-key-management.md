# TOTP Key Management

Last verified: 2026-03-20

## Overview

ArchonAI encrypts TOTP secrets at rest using AES-256-CBC with HMAC-SHA256 (Encrypt-then-MAC), implemented in `DedicatedTotpSecretEncryptor`. Key material is derived via HKDF-SHA256 from a raw secret, producing separate 256-bit encryption and MAC keys.

## Key Resolution

The encryptor resolves its key from the following sources, in order:

1. `ARCHONAI_TOTP_ENCRYPTION_KEY` — **preferred**, dedicated TOTP encryption key
2. `ARCHONAI_JWT_SIGNING_KEY` — **fallback**, acceptable but logs a warning

If neither is set, the encryptor **throws `InvalidOperationException`** at runtime. There is no hardcoded fallback key.

## What Was Unsafe Before

Prior to this hardening, the encryptor contained a hardcoded development fallback key (`default-dev-key-not-for-production!!`) that was used when both environment variables were unset. This key was visible in source code, meaning:

- Any attacker with access to the codebase could decrypt all TOTP secrets
- Production deployments that forgot to set the key would silently use the insecure fallback
- The only signal was a `LogWarning` that could easily be missed

## What Was Fixed

- The hardcoded `DevFallbackKey` constant was **removed entirely**
- Key resolution now **hard-fails** if no key is configured
- The legacy decryption path (for migrating secrets encrypted with the old JWT-derived key) also requires `ARCHONAI_JWT_SIGNING_KEY` to be explicitly set
- Constants were made `internal` for testability

## Production Requirements

| Environment | TOTP Key Required? | JWT Key Acceptable as Fallback? | Health Gate |
|---|---|---|---|
| Production | **Required** | **No** — `DedicatedTotpSecretEncryptor` throws, `ProductionConfigValidator` raises Critical, health check returns Unhealthy | K8s will not route traffic |
| Staging | **Required** | **No** — same enforcement as production | K8s will not route traffic |
| Development | Recommended | Yes (emits `LogWarning`) | No blocking |
| CI/Test | Set in test configuration | Yes (emits `LogWarning`) | No blocking |

**Generate a key:** `openssl rand -base64 48`

## Ciphertext Format

- **Current (v1):** `v1:{Base64(IV)}.{Base64(Ciphertext)}.{Base64(HMAC)}`
- **Legacy:** `{Base64(IV)}.{Base64(Ciphertext)}` — no MAC, SHA256(JWT key) as AES key

Legacy ciphertexts are automatically detected and can be decrypted for migration. Callers should re-encrypt with the current key after successful verification.

## Key Rotation

The `v1:` prefix enables future key rotation. A `v2:` scheme would use a new key while `v1:` ciphertexts remain decryptable with the old key via version-specific resolution.
