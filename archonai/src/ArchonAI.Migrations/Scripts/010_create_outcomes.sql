-- Migration 010: Create outcome_records table
-- Predicted vs. actual outcome tracking for decision calibration.
-- One outcome per decision (UNIQUE on decision_id).
-- Variance, direction, assessment, and recalibration signals computed on actual recording.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/010_drop_outcomes.sql

CREATE TABLE IF NOT EXISTS archonai.outcome_records (
    id                       uuid             PRIMARY KEY DEFAULT gen_random_uuid(),
    decision_id              uuid             NOT NULL UNIQUE
                             REFERENCES archonai.decisions(id) ON DELETE CASCADE,
    tenant_id                uuid             NOT NULL,
    expected_outcome_summary text,
    expected_value           numeric,
    confidence_at_prediction double precision NOT NULL,
    expected_timeframe       text,
    actual_outcome_summary   text,
    actual_value             numeric,
    outcome_observed_at_utc  timestamptz,
    value_variance           numeric,
    variance_percent         double precision,
    direction                int              NOT NULL,  -- 0=Pending, 1=OnTarget, 2=Over, 3=Under
    root_cause               text,
    notes                    text,
    assessment               int              NOT NULL,  -- 0=Pending..4=CompletelyMissed
    recalibration_signal     int              NOT NULL,  -- 0=None..5=AssumptionInvalid
    recorded_by              text             NOT NULL,
    created_at_utc           timestamptz      NOT NULL DEFAULT now(),
    updated_at_utc           timestamptz      NOT NULL DEFAULT now()
);

-- Look up outcome by decision
CREATE INDEX IF NOT EXISTS idx_outcome_records_decision_id
    ON archonai.outcome_records (decision_id);

-- Tenant-scoped calibration queries
CREATE INDEX IF NOT EXISTS idx_outcome_records_tenant_id
    ON archonai.outcome_records (tenant_id);

-- Migration 010 applied successfully
