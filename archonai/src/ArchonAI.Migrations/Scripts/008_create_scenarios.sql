-- Migration 008: Create scenarios table
-- What-if scenario modeling with assumption tracking and comparison axes.
-- All structured fields (assumptions, effects, links) stored as jsonb.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/008_drop_scenarios.sql

CREATE TABLE IF NOT EXISTS archonai.scenarios (
    id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id         uuid        NOT NULL,
    title             text        NOT NULL,
    description       text,
    type              int         NOT NULL,  -- 0=WhatIf..6=StrategicPivot
    status            int         NOT NULL,  -- 0=Draft, 1=Active, 2=Compared, 3=Archived
    assumptions       jsonb       NOT NULL DEFAULT '[]'::jsonb,
    projected_effects jsonb       NOT NULL DEFAULT '[]'::jsonb,
    linked_kpis       jsonb       NOT NULL DEFAULT '[]'::jsonb,
    linked_decisions  jsonb       NOT NULL DEFAULT '[]'::jsonb,
    linked_entities   jsonb       NOT NULL DEFAULT '[]'::jsonb,
    created_by        text        NOT NULL,
    created_at_utc    timestamptz NOT NULL DEFAULT now(),
    updated_at_utc    timestamptz NOT NULL DEFAULT now()
);

-- Tenant-scoped scenario queries
CREATE INDEX IF NOT EXISTS idx_scenarios_tenant_id
    ON archonai.scenarios (tenant_id);

-- Filter by scenario type (WhatIf, CostReduction, etc.)
CREATE INDEX IF NOT EXISTS idx_scenarios_type
    ON archonai.scenarios (type);

-- Filter by scenario lifecycle status
CREATE INDEX IF NOT EXISTS idx_scenarios_status
    ON archonai.scenarios (status);

-- Migration 008 applied successfully
