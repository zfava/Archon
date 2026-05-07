-- Migration 002: Create audit_log table
-- Append-only ledger with SHA-256 hash chain for tamper detection.
-- No UPDATE or DELETE should ever be issued against this table.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/002_drop_audit_log.sql

CREATE TABLE IF NOT EXISTS archonai.audit_log (
    id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    event_type        text        NOT NULL,
    category          text        NOT NULL,
    source            text        NOT NULL,
    subject_id        text        NOT NULL,
    subject_type      text        NOT NULL,
    action            text        NOT NULL,
    resource_type     text        NOT NULL,
    resource_id       text        NOT NULL,
    description       text        NOT NULL,
    metadata          jsonb       NOT NULL DEFAULT '{}'::jsonb,
    -- SHA-256 hash chain: checksum = SHA256(id|event_type|...|previous_checksum)
    checksum          text        NOT NULL,
    previous_entry_id uuid,
    occurred_at_utc   timestamptz NOT NULL
);

-- Filter by event category (agent, workflow, user, etc.)
CREATE INDEX IF NOT EXISTS idx_audit_log_category
    ON archonai.audit_log (category);

-- Look up all audit entries for a specific subject
CREATE INDEX IF NOT EXISTS idx_audit_log_subject_id
    ON archonai.audit_log (subject_id);

-- Filter by resource type for resource-scoped queries
CREATE INDEX IF NOT EXISTS idx_audit_log_resource_type
    ON archonai.audit_log (resource_type);

-- Chronological ordering and time-range queries (newest first)
CREATE INDEX IF NOT EXISTS idx_audit_log_occurred_at_utc
    ON archonai.audit_log (occurred_at_utc DESC);

-- Migration 002 applied successfully
