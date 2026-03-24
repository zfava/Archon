-- Migration 005: Create trust_tier_policies table
-- Tenant-scoped execution trust tiers controlling AI autonomy levels.
-- NOTE: tenant_id is text (not uuid) for compatibility with the TrustTier service contract.
-- Default policies seeded per-tenant by the application on first query.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/005_drop_trust_tiers.sql

CREATE TABLE IF NOT EXISTS archonai.trust_tier_policies (
    id                   uuid             PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id            text             NOT NULL,
    action_scope         text             NOT NULL,
    max_tier             int              NOT NULL,  -- 0=ObserveOnly..5=PolicyEnvelope
    confidence_threshold double precision,
    value_ceiling        numeric,
    require_reversible   boolean          NOT NULL DEFAULT false,
    description          text,
    is_enabled           boolean          NOT NULL DEFAULT true,
    created_by           text             NOT NULL,
    created_at_utc       timestamptz      NOT NULL DEFAULT now(),
    updated_at_utc       timestamptz      NOT NULL DEFAULT now()
);

-- Tenant-scoped policy lookups
CREATE INDEX IF NOT EXISTS idx_trust_tier_policies_tenant_id
    ON archonai.trust_tier_policies (tenant_id);

-- Action scope lookups across tenants
CREATE INDEX IF NOT EXISTS idx_trust_tier_policies_action_scope
    ON archonai.trust_tier_policies (action_scope);

-- Composite: find the policy for a specific tenant + action scope
CREATE INDEX IF NOT EXISTS idx_trust_tier_policies_tenant_scope
    ON archonai.trust_tier_policies (tenant_id, action_scope);

-- Migration 005 applied successfully
