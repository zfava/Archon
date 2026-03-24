-- Migration 022: Create control plane tables
-- Shared PostgreSQL persistence for the control plane subsystem.
-- Replaces the file-backed JSON DurableControlPlaneRepository
-- to support multi-instance correctness and durable state.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/022_drop_control_plane.sql

-- ── Tenants ──────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS archonai.tenants (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    name            text        NOT NULL UNIQUE,
    display_name    text        NOT NULL,
    status          int         NOT NULL,  -- 0=Provisioning..4=Deprovisioned
    tier            int         NOT NULL,  -- 0=Free..3=Enterprise
    resource_quota  jsonb       NOT NULL DEFAULT '{}'::jsonb,
    metadata        jsonb       NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc  timestamptz NOT NULL DEFAULT now(),
    activated_at_utc   timestamptz,
    suspended_at_utc   timestamptz
);

CREATE INDEX IF NOT EXISTS idx_tenants_status
    ON archonai.tenants (status);

CREATE INDEX IF NOT EXISTS idx_tenants_name
    ON archonai.tenants (name);

-- ── Managed Workflows ────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS archonai.managed_workflows (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           text        NOT NULL,
    name                text        NOT NULL,
    description         text        NOT NULL,
    status              int         NOT NULL,  -- 0=Draft..4=Failed
    strategy            text        NOT NULL,
    step_count          int         NOT NULL DEFAULT 0,
    metadata            jsonb       NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc      timestamptz NOT NULL DEFAULT now(),
    last_executed_at_utc timestamptz,
    execution_count     bigint      NOT NULL DEFAULT 0,
    failure_count       bigint      NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_managed_workflows_tenant
    ON archonai.managed_workflows (tenant_id);

CREATE INDEX IF NOT EXISTS idx_managed_workflows_status
    ON archonai.managed_workflows (status);

-- ── Managed Agents ───────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS archonai.managed_agents (
    id               uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id        text        NOT NULL,
    name             text        NOT NULL,
    version          text        NOT NULL,
    status           int         NOT NULL,  -- 0=Registering..4=Deregistered
    capabilities     jsonb       NOT NULL DEFAULT '[]'::jsonb,
    configuration    jsonb       NOT NULL DEFAULT '{}'::jsonb,
    registered_at_utc timestamptz NOT NULL DEFAULT now(),
    last_active_at_utc timestamptz,
    execution_count  bigint      NOT NULL DEFAULT 0,
    failure_count    bigint      NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_managed_agents_tenant
    ON archonai.managed_agents (tenant_id);

CREATE INDEX IF NOT EXISTS idx_managed_agents_status
    ON archonai.managed_agents (status);

-- ── Platform Policies ────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS archonai.platform_policies (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       text        NOT NULL,
    name            text        NOT NULL,
    description     text        NOT NULL,
    policy_type     int         NOT NULL,  -- 0=Security..6=Agent
    target_resource text        NOT NULL,
    rules           jsonb       NOT NULL DEFAULT '{}'::jsonb,
    is_enabled      boolean     NOT NULL DEFAULT true,
    priority        int         NOT NULL DEFAULT 0,
    created_at_utc  timestamptz NOT NULL DEFAULT now(),
    updated_at_utc  timestamptz
);

CREATE INDEX IF NOT EXISTS idx_platform_policies_tenant
    ON archonai.platform_policies (tenant_id);

CREATE INDEX IF NOT EXISTS idx_platform_policies_type
    ON archonai.platform_policies (policy_type);

CREATE INDEX IF NOT EXISTS idx_platform_policies_enabled
    ON archonai.platform_policies (is_enabled)
    WHERE is_enabled = true;

-- ── Platform Configurations ──────────────────────────────────────

CREATE TABLE IF NOT EXISTS archonai.platform_configurations (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       text        NOT NULL,
    scope           text        NOT NULL,
    key             text        NOT NULL,
    value           text        NOT NULL,
    description     text,
    is_secret       boolean     NOT NULL DEFAULT false,
    created_at_utc  timestamptz NOT NULL DEFAULT now(),
    updated_at_utc  timestamptz,
    UNIQUE (tenant_id, scope, key)
);

CREATE INDEX IF NOT EXISTS idx_platform_configs_tenant
    ON archonai.platform_configurations (tenant_id);

CREATE INDEX IF NOT EXISTS idx_platform_configs_scope
    ON archonai.platform_configurations (tenant_id, scope);

-- Migration 022 applied successfully
