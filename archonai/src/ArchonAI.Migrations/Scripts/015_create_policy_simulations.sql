-- Migration 015: Create policy_simulations table
-- Read-only dry-run simulation projections for audit/review.
-- Full simulation result stored as jsonb for flexibility.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/015_drop_policy_simulations.sql

CREATE TABLE IF NOT EXISTS archonai.policy_simulations (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       uuid        NOT NULL,
    action_type     text        NOT NULL,
    title           text        NOT NULL,
    verdict         int         NOT NULL,  -- 0=Allowed..4=ObserveOnly
    simulation_data jsonb       NOT NULL,
    simulated_by    text        NOT NULL,
    simulated_at_utc timestamptz NOT NULL DEFAULT now()
);

-- Tenant-scoped simulation queries
CREATE INDEX IF NOT EXISTS idx_policy_sim_tenant_id
    ON archonai.policy_simulations (tenant_id);

-- Filter by simulated action type
CREATE INDEX IF NOT EXISTS idx_policy_sim_action_type
    ON archonai.policy_simulations (action_type);

-- Chronological simulation history (newest first)
CREATE INDEX IF NOT EXISTS idx_policy_sim_simulated_at
    ON archonai.policy_simulations (simulated_at_utc DESC);

-- Migration 015 applied successfully
