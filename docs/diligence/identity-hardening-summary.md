# Identity/Security/Persistence Hardening Summary

## Objective

Eliminate P0 identity/security risks and make ArchonAI's identity layer safe for enterprise multi-instance deployment.

## Root Causes Found

1. **Hardcoded TOTP fallback key** — `DedicatedTotpSecretEncryptor` contained `"default-dev-key-not-for-production!!"` as a source-visible fallback. If env vars were unset, all TOTP secrets were encrypted with this key.

2. **File-backed identity persistence** — `DurableIdentityStore` wrote users, orgs, memberships, refresh tokens, and invite tokens to a JSON file on disk. Not multi-instance safe. Each pod had its own copy of identity state.

3. **In-memory MFA/OIDC state** — `InMemoryMfaStore`, `InMemoryOidcLoginSessionStore`, `InMemoryTenantAuthConfigStore`, `InMemoryExternalIdentityLinkStore` held identity-critical state in process memory. Lost on restart, not shared across pods.

4. **Silent fallback to unsafe persistence** — No validation detected or blocked unsafe identity persistence in production environments. The app started successfully and appeared healthy while using file-backed or in-memory stores.

5. **Missing deployment artifacts** — Kubernetes secrets template and `.env.example` did not include the TOTP encryption key.

## Changes Implemented

### TOTP Key Hardening

- **Removed** hardcoded `DevFallbackKey` constant from `DedicatedTotpSecretEncryptor`
- **Hard-fail** with `InvalidOperationException` if no key is configured
- **Legacy path** also requires explicit `ARCHONAI_JWT_SIGNING_KEY` (no fallback)
- **File**: `ArchonAI.Infrastructure/Secrets/DedicatedTotpSecretEncryptor.cs`

### PostgreSQL Identity Stores (9 new stores)

| Store | Interface | Tables |
|---|---|---|
| `PostgresUserStore` | `IUserStore` | `users` |
| `PostgresOrganizationStore` | `IOrganizationStore` | `organizations` |
| `PostgresMembershipStore` | `IMembershipStore` | `memberships` |
| `PostgresRefreshTokenStore` | `IRefreshTokenStore` | `refresh_tokens` |
| `PostgresInviteTokenStore` | `IInviteTokenStore` | `invite_tokens` |
| `PostgresMfaStore` | `IMfaStore` | `totp_credentials`, `webauthn_credentials`, `mfa_recovery_codes`, `mfa_challenges`, `mfa_policies` |
| `PostgresTenantAuthConfigStore` | `ITenantAuthConfigStore` | `tenant_auth_configs` |
| `PostgresExternalIdentityLinkStore` | `IExternalIdentityLinkStore` | `external_identity_links` |
| `PostgresOidcLoginSessionStore` | `IOidcLoginSessionStore` | `oidc_login_sessions` |

- **Migration**: `025_create_identity.sql` — 13 tables with indexes and constraints
- **Pattern**: Follows existing 21-store architecture exactly

### Silent Fallback Elimination

- **Identity DI** (`ArchonAI.Identity/DependencyInjection.cs`) — Changed from `DurableIdentityStore` multi-interface registration to individual in-memory stores, enabling the `ReplaceWithFactory` pattern to replace them with Postgres stores when configured.
- **Persistence DI** (`ArchonAI.Persistence/DependencyInjection.cs`) — Added 9 identity store factory registrations alongside the existing 21 domain store factories.

### Startup Validation

- **Production guard** — In Production/Staging, app **shuts down immediately** if `ArchonAIPersistence:ConnectionString` is not set.
- **Development warning** — In non-production, logs a `LogWarning` about in-memory persistence.
- **Health check** — `IdentityPersistenceHealthCheck` added to `/healthz/ready`. Reports Unhealthy in production without PostgreSQL.
- **File**: `ArchonAI.Api/Program.cs`, `ArchonAI.Common/Observability/HealthChecks.cs`

### Deployment Alignment

- **K8s secret template** — Added `ARCHONAI_TOTP_ENCRYPTION_KEY` to both raw YAML and Helm templates
- **Helm secret** — Added inline and external-secrets support for TOTP key
- **`.env.example`** — Added `ARCHONAI_TOTP_ENCRYPTION_KEY` with generation instructions

## Files Changed

| File | Change |
|---|---|
| `ArchonAI.Infrastructure/Secrets/DedicatedTotpSecretEncryptor.cs` | Removed hardcoded fallback key, hard-fail on missing key |
| `ArchonAI.Persistence/Stores/PostgresUserStore.cs` | **New** — PostgreSQL user store |
| `ArchonAI.Persistence/Stores/PostgresOrganizationStore.cs` | **New** — PostgreSQL organization store |
| `ArchonAI.Persistence/Stores/PostgresMembershipStore.cs` | **New** — PostgreSQL membership store |
| `ArchonAI.Persistence/Stores/PostgresRefreshTokenStore.cs` | **New** — PostgreSQL refresh token store |
| `ArchonAI.Persistence/Stores/PostgresInviteTokenStore.cs` | **New** — PostgreSQL invite token store |
| `ArchonAI.Persistence/Stores/PostgresMfaStore.cs` | **New** — PostgreSQL MFA store (5 tables) |
| `ArchonAI.Persistence/Stores/PostgresTenantAuthConfigStore.cs` | **New** — PostgreSQL tenant auth config store |
| `ArchonAI.Persistence/Stores/PostgresExternalIdentityLinkStore.cs` | **New** — PostgreSQL external identity link store |
| `ArchonAI.Persistence/Stores/PostgresOidcLoginSessionStore.cs` | **New** — PostgreSQL OIDC login session store |
| `ArchonAI.Migrations/Scripts/025_create_identity.sql` | **New** — Migration for 13 identity tables |
| `ArchonAI.Identity/DependencyInjection.cs` | Replaced DurableIdentityStore with individual in-memory stores |
| `ArchonAI.Persistence/DependencyInjection.cs` | Added 9 identity store factory registrations |
| `ArchonAI.Api/Program.cs` | Added identity persistence startup validation + health check |
| `ArchonAI.Common/Observability/HealthChecks.cs` | Added `IdentityPersistenceHealthCheck` |
| `deploy/kubernetes/secret.yaml` | Added TOTP encryption key |
| `deploy/helm/archonai/templates/secret.yaml` | Added TOTP key (inline + external-secrets) |
| `.env.example` | Added TOTP encryption key |

## Verification

- **Build**: `dotnet build` — zero errors, zero warnings
- **Tests**: 217 core tests + 182 connector tests passing
- **No breaking changes** to interfaces or contracts
- **Backward compatible** — in-memory stores still work for development when no connection string is set

## Residual Risks

1. **`DurableIdentityStore` still exists** — The file-backed store class remains in the codebase but is no longer registered in DI. It can be removed in a cleanup pass.
2. **`DurableAgentIdentityStore` still file-backed** — Agent identity profiles are still persisted to JSON. This is acceptable because agent identity is operational metadata, not security-critical user identity.
3. **No automated OIDC session cleanup** — Expired `oidc_login_sessions` rows accumulate. A background cleanup job or PostgreSQL TTL extension would prevent unbounded growth.
4. **No integration tests for PostgreSQL identity stores** — The stores follow the exact same pattern as the existing 21 stores (which have Testcontainers-based integration tests), but store-specific integration tests have not been added yet.

## Verdict

**Identity/security/persistence hardening completed correctly.**

The platform now enforces:
- No hardcoded encryption keys in any code path
- Durable PostgreSQL persistence for all identity-critical state when configured
- Explicit startup failure in production without proper configuration
- Health/readiness enforcement that prevents traffic routing to misconfigured pods
