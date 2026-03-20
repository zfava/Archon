-- Migration 024: Create control plane alert and system state tables
-- Shared PostgreSQL persistence for control plane operational state:
-- system pause flag, active alerts, and recent agent activity events.
-- Replaces the in-memory state in ControlPlaneObservabilityService
-- to support multi-instance correctness.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/024_drop_control_plane_alerts.sql

-- System-level state (pause flag). Single-row table.
CREATE TABLE IF NOT EXISTS archonai.control_plane_system_state (
    id              int         PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    is_paused       boolean     NOT NULL DEFAULT false,
    pause_reason    text,
    updated_at_utc  timestamptz NOT NULL DEFAULT now()
);

-- Seed the single row
INSERT INTO archonai.control_plane_system_state (id, is_paused, pause_reason, updated_at_utc)
VALUES (1, false, NULL, now())
ON CONFLICT (id) DO NOTHING;

-- Active system alerts
CREATE TABLE IF NOT EXISTS archonai.control_plane_alerts (
    alert_id        uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    severity        text        NOT NULL,
    component       text        NOT NULL,
    message         text        NOT NULL,
    is_acknowledged boolean     NOT NULL DEFAULT false,
    raised_at_utc   timestamptz NOT NULL DEFAULT now()
);

-- Query unacknowledged alerts
CREATE INDEX IF NOT EXISTS idx_cp_alerts_unacknowledged
    ON archonai.control_plane_alerts (is_acknowledged, raised_at_utc DESC)
    WHERE is_acknowledged = false;

-- Recent agent activity events (bounded, used for dashboard)
CREATE TABLE IF NOT EXISTS archonai.control_plane_agent_events (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agent_id        uuid        NOT NULL,
    agent_name      text        NOT NULL,
    event_type      text        NOT NULL,
    description     text        NOT NULL,
    occurred_at_utc timestamptz NOT NULL DEFAULT now()
);

-- Query recent events ordered by time
CREATE INDEX IF NOT EXISTS idx_cp_agent_events_occurred
    ON archonai.control_plane_agent_events (occurred_at_utc DESC);

-- Migration 024 applied successfully
