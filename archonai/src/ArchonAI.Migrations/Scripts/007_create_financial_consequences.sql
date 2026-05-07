-- Migration 007: Create financial_consequences table
-- Economic impact modeling attached to decisions.
-- One consequence per decision (UNIQUE constraint on decision_id).
-- All monetary fields nullable — partial information is valid.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/007_drop_financial_consequences.sql

CREATE TABLE IF NOT EXISTS archonai.financial_consequences (
    id                          uuid             PRIMARY KEY DEFAULT gen_random_uuid(),
    decision_id                 uuid             NOT NULL UNIQUE
                                REFERENCES archonai.decisions(id) ON DELETE CASCADE,
    tenant_id                   uuid             NOT NULL,
    expected_revenue_impact_low  numeric,
    expected_revenue_impact_high numeric,
    expected_cost_impact_low     numeric,
    expected_cost_impact_high    numeric,
    expected_margin_impact       numeric,
    expected_cash_timing_impact  text,
    labor_impact                 text,
    downside_risk                numeric,
    upside_potential             numeric,
    confidence_adjustment        double precision,
    roi_estimate_low             numeric,
    roi_estimate_high            numeric,
    break_even_estimate          text,
    assumptions                  jsonb            NOT NULL DEFAULT '[]'::jsonb,
    notes                        text,
    created_by                   text             NOT NULL,
    created_at_utc               timestamptz      NOT NULL DEFAULT now(),
    updated_at_utc               timestamptz      NOT NULL DEFAULT now()
);

-- Look up financial consequence by decision
CREATE INDEX IF NOT EXISTS idx_financial_consequences_decision_id
    ON archonai.financial_consequences (decision_id);

-- Tenant-scoped financial queries
CREATE INDEX IF NOT EXISTS idx_financial_consequences_tenant_id
    ON archonai.financial_consequences (tenant_id);

-- Migration 007 applied successfully
