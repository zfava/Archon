CREATE TABLE IF NOT EXISTS archonai.retention_log (
    id              uuid PRIMARY KEY,
    ran_at_utc      timestamptz NOT NULL,
    audit_rows_deleted   bigint NOT NULL DEFAULT 0,
    trace_rows_deleted   bigint NOT NULL DEFAULT 0,
    telemetry_rows_deleted bigint NOT NULL DEFAULT 0,
    duration_ms     bigint NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_retention_log_ran_at_utc
    ON archonai.retention_log (ran_at_utc DESC);
