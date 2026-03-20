# Documentation Correction Manifest

**Date:** 2026-03-20
**Scope:** All files in docs/diligence/, docs/release/open-risks.md, docs/security/, PRODUCTION_READINESS.md

---

## Stale Claims Corrected

### STALE CLAIM 1 — "AI uses echo_fallback / produces fake reasoning"

**Correct reality:** All four model providers (OpenAI, Anthropic, Azure OpenAI, Local) return hard errors (`IsSuccess: false`) when credentials are missing or endpoints are unreachable. They do not fabricate responses. The `echo_fallback` string appears only in `CompositeModelProvider` as a guard against external providers — not as a fallback output path. Runtime behavior when unconfigured: AI endpoints return structured errors, `ModelProviderActivationService` logs CRITICAL at startup, `AiRuntimeDiagnostics` reports readiness tier "unconfigured".

**Files corrected:**
| File | What Was Wrong | What Was Changed |
|------|---------------|-----------------|
| `docs/diligence/runtime-truth-summary.md` | Local provider described as "Falls back to echo stub". "Echo fallback labeling" row described stubs. "Critical truth" said "all AI output is fake". | Updated to describe hard error behavior, echo guard in CompositeModelProvider, and accurate "Critical truth" about structured errors. |
| `docs/diligence/technical-summary.md` | Partially Implemented section said "Echo stubs explicitly labeled with FinishReason: echo_fallback". Known Limitations said "produce echo-stub output". | Updated to describe hard error returns and CRITICAL logging. |
| `docs/diligence/README.md` | "default to local echo fallback". Weakest impressions said "fall back to echo stubs". | Updated to describe hard errors. |
| `docs/release/open-risks.md` | R1 said "fall back to echo stubs...produces fake reasoning". | Updated R1 to describe hard error behavior accurately. |

### STALE CLAIM 2 — "Rollback scripts missing for migrations 020-025"

**Correct reality:** All 25 migrations (001–025) have corresponding Down/ scripts. The rc-validate.yml Stage 5 migration audit verifies this on every RC run.

**Files corrected:**
| File | What Was Wrong | What Was Changed |
|------|---------------|-----------------|
| No files contained this specific claim in the current codebase. | The rollback coverage was already accurately described in deployment-readiness-summary.md and open-risks.md R6. | No changes needed — claim was already corrected in a prior pass. |

### STALE CLAIM 3 — "Vault integration not implemented"

**Correct reality:** Three vault-backed `ISecretProvider` implementations exist: `HashiCorpVaultSecretProvider` (AppRole auth, KV v2, HTTP API), `AwsSecretsManagerSecretProvider` (SDK credential chain), `AzureKeyVaultSecretProvider` (DefaultAzureCredential). All implement `ISecretRotationNotifier`. Full chain: Vault → AWS → Azure → File → Environment.

**Files corrected:**
| File | What Was Wrong | What Was Changed |
|------|---------------|-----------------|
| `docs/diligence/runtime-truth-summary.md` | Section 8 said "No HashiCorp Vault, AWS SM, or Azure KV provider implemented". Item 4 said "no ISecretProvider vault backend exists". | Updated to list all three providers with evidence. |
| `docs/diligence/technical-summary.md` | Said "No external vault integration". Known Limitations said same. | Updated to describe three implementations and chain. |
| `docs/diligence/README.md` | Listed "Secret vault integration" under "Not Implemented". Weakest impressions said "Secrets in plaintext". | Moved to "Implemented Since Prior Audit". Updated weakest impressions. |
| `docs/diligence/deployment-readiness-summary.md` | "No ISecretProvider implementation for HashiCorp Vault, AWS SM, or Azure KV". Residual gap #2 said same. | Updated both to describe three implementations. |

### STALE CLAIM 4 — "TOTP uses JWT key / TOTP encryption posture inconsistent"

**Correct reality:** `DedicatedTotpSecretEncryptor` uses `ARCHONAI_TOTP_ENCRYPTION_KEY` with HKDF-derived AES-256-CBC + HMAC-SHA256. JWT fallback is permitted only in dev/test (emits Warning). In production, `ProductionConfigValidator` reports CRITICAL and `DedicatedTotpSecretEncryptor` throws `InvalidOperationException`.

**Files corrected:**
| File | What Was Wrong | What Was Changed |
|------|---------------|-----------------|
| `docs/diligence/runtime-truth-summary.md` | TOTP row said "derives key from JWT signing key (stand-in for KMS)". | Updated to describe DedicatedTotpSecretEncryptor with health-gated production enforcement. |
| `docs/security/mfa-key-management.md` | Fallback table said "Neither set: Uses hardcoded development fallback". Residual risks understated production enforcement. | Updated fallback table to show production throws/blocks. Removed hardcoded fallback reference. Added recommended setup section. |
| `docs/security/totp-key-management.md` | Production requirements table said "JWT Key Acceptable: Yes (with warning)" for all environments. | Updated to show production/staging reject JWT fallback with health gate. |
| `docs/release/open-risks.md` | R5 said "TOTP secret encryption uses JWT signing key as KMS stand-in". R20 said "uses AES derived from JWT_SIGNING_KEY...stand-in". | Updated R5 to cite DedicatedTotpSecretEncryptor. Updated R20 to describe health-gated enforcement. |

---

## Additional Corrections

| File | Change |
|------|--------|
| `docs/diligence/security-summary.md` | "Unverified Security Areas" section was massively stale — listed SSO/OIDC, MFA, secrets, persistence, scanning, circuit breakers, CORS as "not implemented". Replaced with "Previously Unverified — Now Implemented" section with evidence, plus accurate "Remaining Unverified Areas". Updated compliance readiness. |
| `docs/diligence/README.md` | "Not Implemented" section listed 7 items that are now implemented. Replaced with "Implemented Since Prior Audit" section. |
| All touched files | Added "Last verified: 2026-03-20" line. |
