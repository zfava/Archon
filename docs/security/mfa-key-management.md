# MFA Key Management

Last verified: 2026-03-20

This document describes the key management architecture for ArchonAI's multi-factor authentication subsystem.

## Key Inventory

| Key | Environment Variable | Purpose | Rotation Impact |
|-----|---------------------|---------|-----------------|
| TOTP Encryption Key | `ARCHONAI_TOTP_ENCRYPTION_KEY` | Encrypts TOTP shared secrets at rest | Requires key versioning (see below) |
| JWT Signing Key | `ARCHONAI_JWT_SIGNING_KEY` | Signs JWT access/refresh tokens | No impact on TOTP (decoupled) |
| JWT Previous Key | `ARCHONAI_JWT_SIGNING_KEY_PREVIOUS` | Validates tokens signed with the previous key during rollover | No impact on TOTP |

## Key Independence

TOTP secret encryption and JWT signing use **completely independent key material**:

```
JWT Signing:  ARCHONAI_JWT_SIGNING_KEY → HMAC-SHA256 token signatures
TOTP Encrypt: ARCHONAI_TOTP_ENCRYPTION_KEY → HKDF → AES-256-CBC + HMAC-SHA256
```

Rotating the JWT signing key has **zero effect** on TOTP secret decryptability. This is the core design improvement — the previous implementation derived the TOTP encryption key from the JWT signing key, creating a dangerous coupling.

## TOTP Key Rotation Strategy

### Current Key Version: v1

All TOTP ciphertexts are prefixed with `v1:` to identify the key version. When rotation is needed:

1. **Set the new key**: Update `ARCHONAI_TOTP_ENCRYPTION_KEY` to the new value
2. **Bump the version**: Implement `v2:` encryption in `DedicatedTotpSecretEncryptor`
3. **Retain old key**: Keep the `v1:` decryption path active for backward compatibility
4. **Lazy migration**: Existing secrets are re-encrypted to `v2:` on next successful verification
5. **Decommission**: Once all secrets are migrated (monitor logs), remove `v1:` decryption support

### Rotation Checklist

- [ ] Generate new TOTP encryption key (minimum 32 bytes, cryptographically random)
- [ ] Deploy new key alongside `v2:` encryption support
- [ ] Monitor for `v1:` decryptions in logs
- [ ] After all users have verified at least once, remove `v1:` support (optional)

## JWT Key Rotation (Unchanged)

JWT key rotation follows the existing dual-key validation pattern:

1. Set `ARCHONAI_JWT_SIGNING_KEY_PREVIOUS` to the current key
2. Set `ARCHONAI_JWT_SIGNING_KEY` to the new key
3. Both keys validate tokens during the rollover window
4. After all old tokens expire, remove the previous key

**This process has no effect on TOTP secrets** — they are encrypted with a separate key.

## Deployment Modes

### Kubernetes Secrets

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: archonai-secrets
data:
  ARCHONAI_TOTP_ENCRYPTION_KEY: <base64-encoded-key>
  ARCHONAI_JWT_SIGNING_KEY: <base64-encoded-key>
```

### External Secrets Operator

```yaml
apiVersion: external-secrets.io/v1beta1
kind: ExternalSecret
spec:
  data:
    - secretKey: ARCHONAI_TOTP_ENCRYPTION_KEY
      remoteRef:
        key: archonai/totp-encryption-key
    - secretKey: ARCHONAI_JWT_SIGNING_KEY
      remoteRef:
        key: archonai/jwt-signing-key
```

### HashiCorp Vault

Mount separate Vault paths for TOTP and JWT keys:

```
vault kv put secret/archonai/totp encryption_key=<value>
vault kv put secret/archonai/jwt  signing_key=<value>
```

## Fallback Behavior

| Scenario | Behavior |
|----------|----------|
| `ARCHONAI_TOTP_ENCRYPTION_KEY` set | Dedicated key used for all new encryptions |
| `ARCHONAI_TOTP_ENCRYPTION_KEY` not set, `ARCHONAI_JWT_SIGNING_KEY` set, **dev/test** | Falls back to JWT key with `LogWarning`. Acceptable for development only. |
| `ARCHONAI_TOTP_ENCRYPTION_KEY` not set, **production-like** | `ProductionConfigValidator` raises `Critical` finding. `DedicatedTotpSecretEncryptor` throws `InvalidOperationException`. Health check returns Unhealthy. K8s will not route traffic. |
| Neither set | `DedicatedTotpSecretEncryptor` throws `InvalidOperationException` at runtime. MFA is non-functional. |
| Legacy ciphertext encountered | Decrypted with JWT-derived key (`SHA256(JWT_KEY)` as AES key), re-encrypted with dedicated key on next successful verification. |

## Recommended Setup

Generate a dedicated TOTP encryption key (minimum 32 bytes, cryptographically random):

```bash
openssl rand -base64 48
```

Set as `ARCHONAI_TOTP_ENCRYPTION_KEY` in your secrets configuration (K8s Secret, vault, or environment variable).

## Residual Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| TOTP key not set in production | **Blocked** | `ProductionConfigValidator` raises Critical. `DedicatedTotpSecretEncryptor` throws. K8s will not route traffic to unhealthy pods. |
| Legacy secrets not yet migrated | Low | Migration is lazy and transparent; no user impact |
| Vault-backed key delivery | Low | Three vault `ISecretProvider` implementations exist (HashiCorp, AWS, Azure). Key can be delivered via vault chain. |
| Recovery codes use PBKDF2 (not encryption) | None | Recovery codes are one-way hashed, not encrypted; no key dependency |
