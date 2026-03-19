-- Migration 009: Create operational_exceptions table
-- Operational anomalies, failures, and drift events with priority scoring.
-- Priority score = severity_weight × urgency × economic_factor × confidence × escalation_boost.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/009_drop_exceptions.sql

CREATE TABLE IF NOT EXISTS archonai.operational_exceptions (
    id                      uuid             PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id               uuid             NOT NULL,
    category                int              NOT NULL,  -- 0=Anomaly..7=InterventionPoint
    severity                int              NOT NULL,  -- 0=Info, 1=Warning, 2=High, 3=Critical
    title                   text             NOT NULL,
    description             text             NOT NULL,
    domain                  text             NOT NULL,
    status                  int              NOT NULL,  -- 0=Open..4=Dismissed
    urgency                 double precision NOT NULL,
    economic_impact_estimate double precision NOT NULL,
    confidence              double precision NOT NULL,
    escalation_level        int              NOT NULL,  -- 0=None..3=Executive
    assigned_to             text,
    escalation_path         text,
    linked_artifacts        jsonb            NOT NULL DEFAULT '[]'::jsonb,
    recommended_action      jsonb,
    created_by              text             NOT NULL,
    created_at_utc          timestamptz      NOT NULL DEFAULT now(),
    updated_at_utc          timestamptz      NOT NULL DEFAULT now(),
    acknowledged_at_utc     timestamptz,
    resolved_at_utc         timestamptz
);

-- Tenant-scoped exception queries
CREATE INDEX IF NOT EXISTS idx_opex_tenant_id
    ON archonai.operational_exceptions (tenant_id);

-- Priority queue: filter by severity for critical alerts
CREATE INDEX IF NOT EXISTS idx_opex_severity
    ON archonai.operational_exceptions (severity);

-- Filter by exception category (Anomaly, Failure, Drift, etc.)
CREATE INDEX IF NOT EXISTS idx_opex_category
    ON archonai.operational_exceptions (category);

-- Filter by exception lifecycle status (Open, Acknowledged, etc.)
CREATE INDEX IF NOT EXISTS idx_opex_status
    ON archonai.operational_exceptions (status);

-- Filter by business domain
CREATE INDEX IF NOT EXISTS idx_opex_domain
    ON archonai.operational_exceptions (domain);

-- Migration 009 applied successfully
