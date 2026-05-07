-- Migration 018: Create inspection tables
-- Decision rationale bundles, policy evaluations, memory references,
-- and workflow failure diagnostics for operator transparency.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/018_drop_inspection.sql

CREATE TABLE IF NOT EXISTS archonai.inspection_policy_evaluations (
    evaluation_id        uuid             PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id            uuid             NOT NULL,
    subject_type         text             NOT NULL,
    subject_id           text             NOT NULL,
    is_allowed           boolean          NOT NULL,
    risk_score           double precision NOT NULL,
    confidence_score     double precision NOT NULL,
    requires_approval    boolean          NOT NULL,
    approval_state       text             NOT NULL,
    manual_override_state text            NOT NULL,
    approval_checkpoint  text             NOT NULL,
    guardrail_violations jsonb            NOT NULL DEFAULT '[]'::jsonb,
    rules_evaluated      jsonb            NOT NULL DEFAULT '[]'::jsonb,
    reason               text             NOT NULL,
    evaluated_at_utc     timestamptz      NOT NULL DEFAULT now()
);

-- Tenant-scoped policy evaluation queries
CREATE INDEX IF NOT EXISTS idx_insp_policy_tenant_id
    ON archonai.inspection_policy_evaluations (tenant_id);

-- Composite: look up evaluation for a specific subject (decision, action, workflow)
CREATE INDEX IF NOT EXISTS idx_insp_policy_subject
    ON archonai.inspection_policy_evaluations (subject_type, subject_id);

CREATE TABLE IF NOT EXISTS archonai.inspection_memory_references (
    id              uuid             PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       uuid             NOT NULL,
    subject_type    text             NOT NULL,
    subject_id      text             NOT NULL,
    memory_id       uuid             NOT NULL,
    memory_type     text             NOT NULL,
    source          text             NOT NULL,
    content_summary text             NOT NULL,
    relevance_score double precision NOT NULL,
    usage_context   text             NOT NULL,
    retrieved_at_utc timestamptz     NOT NULL DEFAULT now()
);

-- Tenant-scoped memory reference queries
CREATE INDEX IF NOT EXISTS idx_insp_memref_tenant_id
    ON archonai.inspection_memory_references (tenant_id);

-- Composite: look up memory references for a specific subject
CREATE INDEX IF NOT EXISTS idx_insp_memref_subject
    ON archonai.inspection_memory_references (subject_type, subject_id);

CREATE TABLE IF NOT EXISTS archonai.inspection_workflow_diagnostics (
    workflow_id          uuid        PRIMARY KEY,  -- natural key, not generated
    tenant_id            uuid        NOT NULL,
    workflow_name        text        NOT NULL,
    current_state        text        NOT NULL,
    failure_category     text        NOT NULL,
    failure_reason       text        NOT NULL,
    failed_step_name     text,
    failed_step_index    int,
    step_diagnostics     jsonb       NOT NULL DEFAULT '[]'::jsonb,
    policy_evaluations   jsonb       NOT NULL DEFAULT '[]'::jsonb,
    context_used         jsonb       NOT NULL DEFAULT '[]'::jsonb,
    is_retryable         boolean     NOT NULL,
    suggested_remediation text,
    related_exceptions   jsonb       NOT NULL DEFAULT '[]'::jsonb,
    failed_at_utc        timestamptz NOT NULL,
    inspected_at_utc     timestamptz NOT NULL DEFAULT now()
);

-- Tenant-scoped workflow diagnostics
CREATE INDEX IF NOT EXISTS idx_insp_wfdiag_tenant_id
    ON archonai.inspection_workflow_diagnostics (tenant_id);

-- Migration 018 applied successfully
