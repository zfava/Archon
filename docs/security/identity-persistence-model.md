# Identity Persistence Model

## Overview

ArchonAI's identity layer covers users, organizations, memberships, refresh tokens, invite tokens, MFA credentials (TOTP, WebAuthn, recovery codes, challenges, policies), OIDC login sessions, tenant auth configs, and external identity links.

All identity stores support two backends:

1. **PostgreSQL** — production-grade, multi-instance safe, durable
2. **In-memory** — development/testing only, single-instance, lost on restart

## Store Architecture

| Interface | PostgreSQL Store | In-Memory Fallback | Migration |
|---|---|---|---|
| `IUserStore` | `PostgresUserStore` | `InMemoryUserStore` | 025 |
| `IOrganizationStore` | `PostgresOrganizationStore` | `InMemoryOrganizationStore` | 025 |
| `IMembershipStore` | `PostgresMembershipStore` | `InMemoryMembershipStore` | 025 |
| `IRefreshTokenStore` | `PostgresRefreshTokenStore` | `InMemoryRefreshTokenStore` | 025 |
| `IInviteTokenStore` | `PostgresInviteTokenStore` | `InMemoryInviteTokenStore` | 025 |
| `IMfaStore` | `PostgresMfaStore` | `InMemoryMfaStore` | 025 |
| `ITenantAuthConfigStore` | `PostgresTenantAuthConfigStore` | `InMemoryTenantAuthConfigStore` | 025 |
| `IExternalIdentityLinkStore` | `PostgresExternalIdentityLinkStore` | `InMemoryExternalIdentityLinkStore` | 025 |
| `IOidcLoginSessionStore` | `PostgresOidcLoginSessionStore` | `InMemoryOidcLoginSessionStore` | 025 |

## Backend Selection

Backend selection is config-driven via the `ReplaceWithFactory` pattern in `ArchonAI.Persistence.DependencyInjection`:

- If `ArchonAIPersistence:ConnectionString` is set → PostgreSQL stores are used
- If unset → in-memory fallback stores are used

## What Was Unsafe Before

Prior to this hardening:

- **`DurableIdentityStore`** backed users, orgs, memberships, refresh tokens, and invite tokens using a **JSON file on disk** (`identity-state.json`). This was not multi-instance safe — two pods writing to the same file would corrupt data. In Kubernetes, each pod had its own copy, meaning identity data was **silently different across pods**.
- **MFA state, OIDC sessions, tenant auth configs, and external identity links** were all **in-memory only**. A pod restart lost all MFA credentials. OIDC login flows failed across pods because the session state was pod-local.
- There was **no startup validation** to detect or block this unsafe configuration in production.

## What Was Fixed

1. **9 PostgreSQL-backed identity stores** created, following the existing 21-store pattern
2. **Migration script 025** creates all identity tables with proper indexes and constraints
3. **Factory-based DI** automatically selects PostgreSQL when connection string is configured
4. **Startup validation** blocks production startup without durable persistence
5. **Health check** (`identity_persistence`) reports unhealthy in production without PostgreSQL

## Multi-Instance Guarantees

With PostgreSQL persistence:

- **OIDC sessions**: Atomic `DELETE ... RETURNING` ensures single-use consumption across pods
- **MFA challenges**: Token hash lookup filters expired/used challenges
- **Refresh tokens**: Hash-based lookup with revocation and expiry filtering
- **User/org/membership**: Standard CRUD with unique constraints on email, slug, user+org

## Production Requirements

Set `ArchonAIPersistence:ConnectionString` to a valid PostgreSQL connection string. The same connection string backs both domain stores (governance, decisions, etc.) and identity stores.
