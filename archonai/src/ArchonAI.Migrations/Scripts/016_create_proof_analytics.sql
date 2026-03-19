-- Migration 016: Create proof_events table
-- Decision-to-outcome lineage: every proof event in the lifecycle of a decision.
-- Supports predicted-vs-actual, approval conversion, execution trends, trust grading.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/016_drop_proof_analytics.sql

CREATE TABLE IF NOT EXISTS archonai.proof_events (
    id                uuid             PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id         uuid             NOT NULL,
    decision_id       uuid             NOT NULL,
    workflow_id       uuid,
    event_type        int              NOT NULL,  -- 0=DecisionCreated..11=EconomicImpactAttributed
    actor             text             NOT NULL,
    detail            text,
    expected_value    numeric,
    actual_value      numeric,
    variance          numeric,
    variance_percent  double precision,
    action_type       text,
    is_success        boolean,
    override_reason   text,
    economic_impact   numeric,
    impact_attribution text,
    occurred_at_utc   timestamptz      NOT NULL DEFAULT now()
);

-- Look up all proof events for a specific decision
CREATE INDEX IF NOT EXISTS idx_proof_events_decision_id
    ON archonai.proof_events (decision_id);

-- Look up all proof events for a specific workflow
CREATE INDEX IF NOT EXISTS idx_proof_events_workflow_id
    ON archonai.proof_events (workflow_id)
    WHERE workflow_id IS NOT NULL;

-- Tenant-scoped analytics queries
CREATE INDEX IF NOT EXISTS idx_proof_events_tenant_id
    ON archonai.proof_events (tenant_id);

-- Filter by event type for specific analytics
CREATE INDEX IF NOT EXISTS idx_proof_events_event_type
    ON archonai.proof_events (event_type);

-- Chronological proof trail (newest first)
CREATE INDEX IF NOT EXISTS idx_proof_events_occurred_at
    ON archonai.proof_events (occurred_at_utc DESC);

-- Migration 016 applied successfully
