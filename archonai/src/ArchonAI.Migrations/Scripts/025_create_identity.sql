-- Migration 025: Create identity tables (users, orgs, memberships, tokens, MFA, OIDC)
-- PostgreSQL-backed identity persistence for multi-instance production deployments.
-- Replaces file-backed DurableIdentityStore and in-memory identity stores.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/025_drop_identity.sql

-- ── Users ────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.users (
    id               uuid        PRIMARY KEY,
    email            text        NOT NULL,
    display_name     text        NOT NULL,
    password_hash    text        NOT NULL,
    organization_id  uuid        NOT NULL,
    role             text        NOT NULL,
    is_active        boolean     NOT NULL DEFAULT true,
    created_at_utc   timestamptz NOT NULL DEFAULT now(),
    last_login_at_utc timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_users_email
    ON archonai.users (email);

CREATE INDEX IF NOT EXISTS idx_users_organization_id
    ON archonai.users (organization_id);

-- ── Organizations ────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.organizations (
    id             uuid        PRIMARY KEY,
    name           text        NOT NULL,
    slug           text        NOT NULL,
    is_active      boolean     NOT NULL DEFAULT true,
    created_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_organizations_slug
    ON archonai.organizations (slug);

-- ── Memberships ──────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.memberships (
    id              uuid        PRIMARY KEY,
    user_id         uuid        NOT NULL,
    organization_id uuid        NOT NULL,
    role            text        NOT NULL,
    joined_at_utc   timestamptz NOT NULL DEFAULT now(),
    UNIQUE (user_id, organization_id)
);

CREATE INDEX IF NOT EXISTS idx_memberships_user_id
    ON archonai.memberships (user_id);

CREATE INDEX IF NOT EXISTS idx_memberships_organization_id
    ON archonai.memberships (organization_id);

-- ── Refresh Tokens ───────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.refresh_tokens (
    id             uuid        PRIMARY KEY,
    user_id        uuid        NOT NULL,
    token_hash     text        NOT NULL,
    expires_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    is_revoked     boolean     NOT NULL DEFAULT false
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_refresh_tokens_hash
    ON archonai.refresh_tokens (token_hash);

CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user_id
    ON archonai.refresh_tokens (user_id);

-- ── Invite Tokens ────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.invite_tokens (
    id              uuid        PRIMARY KEY,
    organization_id uuid        NOT NULL,
    email           text        NOT NULL,
    role            text        NOT NULL,
    token_hash      text        NOT NULL,
    expires_at_utc  timestamptz NOT NULL,
    created_at_utc  timestamptz NOT NULL DEFAULT now(),
    is_accepted     boolean     NOT NULL DEFAULT false
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_invite_tokens_hash
    ON archonai.invite_tokens (token_hash);

-- ── TOTP Credentials ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.totp_credentials (
    id               uuid        PRIMARY KEY,
    user_id          uuid        NOT NULL UNIQUE,
    encrypted_secret text        NOT NULL,
    is_verified      boolean     NOT NULL DEFAULT false,
    created_at_utc   timestamptz NOT NULL DEFAULT now()
);

-- ── WebAuthn Credentials ─────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.webauthn_credentials (
    id              uuid        PRIMARY KEY,
    user_id         uuid        NOT NULL,
    credential_id   bytea       NOT NULL,
    public_key      bytea       NOT NULL,
    sign_count      integer     NOT NULL DEFAULT 0,
    display_name    text        NOT NULL,
    created_at_utc  timestamptz NOT NULL DEFAULT now(),
    last_used_at_utc timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_webauthn_credential_id
    ON archonai.webauthn_credentials (credential_id);

CREATE INDEX IF NOT EXISTS idx_webauthn_user_id
    ON archonai.webauthn_credentials (user_id);

-- ── MFA Recovery Codes ───────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.mfa_recovery_codes (
    id             uuid        PRIMARY KEY,
    user_id        uuid        NOT NULL,
    code_hash      text        NOT NULL,
    is_used        boolean     NOT NULL DEFAULT false,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    used_at_utc    timestamptz
);

CREATE INDEX IF NOT EXISTS idx_mfa_recovery_codes_user_id
    ON archonai.mfa_recovery_codes (user_id);

-- ── MFA Challenges ───────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.mfa_challenges (
    id              uuid        PRIMARY KEY,
    user_id         uuid        NOT NULL,
    token_hash      text        NOT NULL,
    allowed_methods jsonb       NOT NULL DEFAULT '[]'::jsonb,
    expires_at_utc  timestamptz NOT NULL,
    created_at_utc  timestamptz NOT NULL DEFAULT now(),
    is_used         boolean     NOT NULL DEFAULT false
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_mfa_challenges_hash
    ON archonai.mfa_challenges (token_hash);

-- ── MFA Policies ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.mfa_policies (
    organization_id uuid        PRIMARY KEY,
    mode            integer     NOT NULL DEFAULT 0,
    updated_at_utc  timestamptz NOT NULL DEFAULT now()
);

-- ── Tenant Auth Configs (OIDC provider configuration) ────────────────────────
CREATE TABLE IF NOT EXISTS archonai.tenant_auth_configs (
    id              uuid        PRIMARY KEY,
    organization_id uuid        NOT NULL UNIQUE,
    provider_type   integer     NOT NULL,
    authority       text        NOT NULL,
    client_id       text        NOT NULL,
    client_secret   text        NOT NULL,
    domain          text,
    scopes          jsonb       NOT NULL DEFAULT '[]'::jsonb,
    auto_provision  boolean     NOT NULL DEFAULT false,
    default_role    text        NOT NULL DEFAULT 'Viewer',
    is_enabled      boolean     NOT NULL DEFAULT true,
    created_at_utc  timestamptz NOT NULL DEFAULT now(),
    updated_at_utc  timestamptz
);

-- ── External Identity Links (OIDC federated user mappings) ───────────────────
CREATE TABLE IF NOT EXISTS archonai.external_identity_links (
    id               uuid        PRIMARY KEY,
    user_id          uuid        NOT NULL,
    organization_id  uuid        NOT NULL,
    provider_type    integer     NOT NULL,
    external_subject text        NOT NULL,
    external_issuer  text        NOT NULL,
    external_email   text,
    created_at_utc   timestamptz NOT NULL DEFAULT now(),
    last_used_at_utc timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_external_identity_subject_issuer
    ON archonai.external_identity_links (external_subject, external_issuer);

CREATE INDEX IF NOT EXISTS idx_external_identity_user_org
    ON archonai.external_identity_links (user_id, organization_id);

-- ── OIDC Login Sessions (server-side state for PKCE/nonce) ───────────────────
CREATE TABLE IF NOT EXISTS archonai.oidc_login_sessions (
    state           text        PRIMARY KEY,
    nonce           text        NOT NULL,
    code_verifier   text        NOT NULL,
    organization_id uuid        NOT NULL,
    created_at_utc  timestamptz NOT NULL DEFAULT now(),
    expires_at_utc  timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_oidc_login_sessions_expires
    ON archonai.oidc_login_sessions (expires_at_utc);

-- Migration 025 applied successfully
