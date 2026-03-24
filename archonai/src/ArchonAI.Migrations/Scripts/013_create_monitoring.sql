-- Migration 013: Create monitoring tables
-- Dashboard snapshot persistence and counter state tracking.
-- Counters survive restarts; snapshots provide historical dashboards.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/013_drop_monitoring.sql

CREATE TABLE IF NOT EXISTS archonai.monitoring_snapshots (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    dashboard_type  text        NOT NULL,
    snapshot_data   jsonb       NOT NULL,
    captured_at_utc timestamptz NOT NULL DEFAULT now()
);

-- Query snapshots by type (workflow, agent, system)
CREATE INDEX IF NOT EXISTS idx_monitoring_snapshots_type
    ON archonai.monitoring_snapshots (dashboard_type);

-- Chronological snapshot history
CREATE INDEX IF NOT EXISTS idx_monitoring_snapshots_captured_at
    ON archonai.monitoring_snapshots (captured_at_utc DESC);

CREATE TABLE IF NOT EXISTS archonai.monitoring_counters (
    counter_name   text        PRIMARY KEY,
    counter_value  bigint      NOT NULL DEFAULT 0,
    updated_at_utc timestamptz NOT NULL DEFAULT now()
);

-- Migration 013 applied successfully
