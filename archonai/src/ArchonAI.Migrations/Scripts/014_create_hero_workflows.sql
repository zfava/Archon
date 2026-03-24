-- Migration 014: Create hero_workflow_instances table
-- Cross-functional hero workflows (vendor-selection, revenue-forecast-override, etc.).
-- Workflow DEFINITIONS are static in code; only INSTANCES are persisted.
-- Steps and artifacts stored as jsonb for flexible step-state tracking.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/014_drop_hero_workflows.sql

CREATE TABLE IF NOT EXISTS archonai.hero_workflow_instances (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id      uuid        NOT NULL,
    workflow_type  text        NOT NULL,
    title          text        NOT NULL,
    status         int         NOT NULL,  -- 0=Draft..6=Cancelled
    steps          jsonb       NOT NULL DEFAULT '[]'::jsonb,
    artifacts      jsonb       NOT NULL DEFAULT '{}'::jsonb,
    initiated_by   text        NOT NULL,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now()
);

-- Tenant-scoped workflow queries
CREATE INDEX IF NOT EXISTS idx_hero_wf_tenant_id
    ON archonai.hero_workflow_instances (tenant_id);

-- Filter by workflow type (vendor-selection, etc.)
CREATE INDEX IF NOT EXISTS idx_hero_wf_workflow_type
    ON archonai.hero_workflow_instances (workflow_type);

-- Filter by workflow status (InProgress, Completed, Failed, etc.)
CREATE INDEX IF NOT EXISTS idx_hero_wf_status
    ON archonai.hero_workflow_instances (status);

-- Chronological ordering for workflow listing (newest first)
CREATE INDEX IF NOT EXISTS idx_hero_wf_created_at
    ON archonai.hero_workflow_instances (created_at_utc DESC);

-- Migration 014 applied successfully
