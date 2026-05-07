-- Migration 006: Create decisions and decision_lifecycle_events tables
-- First-class business decisions with full lifecycle tracking.
-- Alternatives, constraints, and artifacts stored as jsonb for schema flexibility.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/006_drop_decisions.sql

CREATE TABLE IF NOT EXISTS archonai.decisions (
    id                   uuid             PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id            uuid             NOT NULL,
    title                text             NOT NULL,
    domain               text             NOT NULL,
    objective            text             NOT NULL,
    constraints          jsonb            NOT NULL DEFAULT '[]'::jsonb,
    assumptions          jsonb            NOT NULL DEFAULT '[]'::jsonb,
    alternatives         jsonb            NOT NULL DEFAULT '[]'::jsonb,
    recommended_option_id text            NOT NULL,
    confidence           double precision NOT NULL,
    reversibility        int              NOT NULL,  -- 0=FullyReversible, 1=Partial, 2=Irreversible
    risk_level           int              NOT NULL,  -- 0=Low, 1=Medium, 2=High, 3=Critical
    expected_value       numeric,
    requires_approval    boolean          NOT NULL,
    linked_artifacts     jsonb            NOT NULL DEFAULT '[]'::jsonb,
    status               int              NOT NULL,  -- 0=Draft..7=Superseded
    created_by           text             NOT NULL,
    created_at_utc       timestamptz      NOT NULL DEFAULT now(),
    updated_at_utc       timestamptz      NOT NULL DEFAULT now()
);

-- Tenant-scoped decision queries
CREATE INDEX IF NOT EXISTS idx_decisions_tenant_id
    ON archonai.decisions (tenant_id);

-- Filter decisions by business domain
CREATE INDEX IF NOT EXISTS idx_decisions_domain
    ON archonai.decisions (domain);

-- Filter by decision status (Draft, Proposed, Approved, etc.)
CREATE INDEX IF NOT EXISTS idx_decisions_status
    ON archonai.decisions (status);

CREATE TABLE IF NOT EXISTS archonai.decision_lifecycle_events (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    decision_id     uuid        NOT NULL REFERENCES archonai.decisions(id) ON DELETE CASCADE,
    event_type      text        NOT NULL,
    actor           text        NOT NULL,
    detail          text,
    occurred_at_utc timestamptz NOT NULL DEFAULT now()
);

-- Look up lifecycle events for a specific decision
CREATE INDEX IF NOT EXISTS idx_lifecycle_decision_id
    ON archonai.decision_lifecycle_events (decision_id);

-- Migration 006 applied successfully
