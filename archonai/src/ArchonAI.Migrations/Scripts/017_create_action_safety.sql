-- Migration 017: Create action safety tables
-- Safety classifications per action type + governed action execution records.
-- Default classifications seeded by the application on first startup.
-- Rollback tracking with multi-attempt history stored as jsonb.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/017_drop_action_safety.sql

CREATE TABLE IF NOT EXISTS archonai.action_safety_classifications (
    id                       uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    action_type              text        NOT NULL UNIQUE,
    reversibility            int         NOT NULL,  -- 0=Reversible, 1=Compensatable, 2=Irreversible
    rollback_supported       boolean     NOT NULL,
    rollback_strategy        int         NOT NULL,  -- 0=None..4=Compensation
    rollback_window_ticks    bigint,                 -- TimeSpan.Ticks, NULL = no window
    compensation_description text,
    operator_notes           text,
    classified_by            text        NOT NULL,
    classified_at_utc        timestamptz NOT NULL DEFAULT now()
);

-- Look up classification by action type (UNIQUE enforced above)
CREATE INDEX IF NOT EXISTS idx_safety_class_action_type
    ON archonai.action_safety_classifications (action_type);

CREATE TABLE IF NOT EXISTS archonai.governed_action_records (
    id                    uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id             uuid        NOT NULL,
    decision_id           uuid,
    workflow_id           uuid,
    approval_gate_id      uuid,
    action_type           text        NOT NULL,
    description           text        NOT NULL,
    safety_classification jsonb       NOT NULL,
    status                int         NOT NULL,  -- 0=Executed..8=Irreversible
    executed_by           text        NOT NULL,
    executed_at_utc       timestamptz NOT NULL DEFAULT now(),
    rollback_history      jsonb       NOT NULL DEFAULT '[]'::jsonb,
    compensation_outcome  text,
    updated_at_utc        timestamptz NOT NULL DEFAULT now()
);

-- Tenant-scoped governed action queries
CREATE INDEX IF NOT EXISTS idx_gov_actions_tenant_id
    ON archonai.governed_action_records (tenant_id);

-- Filter governed actions by type
CREATE INDEX IF NOT EXISTS idx_gov_actions_action_type
    ON archonai.governed_action_records (action_type);

-- Cross-reference governed actions with decisions
CREATE INDEX IF NOT EXISTS idx_gov_actions_decision_id
    ON archonai.governed_action_records (decision_id)
    WHERE decision_id IS NOT NULL;

-- Cross-reference governed actions with workflows
CREATE INDEX IF NOT EXISTS idx_gov_actions_workflow_id
    ON archonai.governed_action_records (workflow_id)
    WHERE workflow_id IS NOT NULL;

-- Filter by governed action status (Executed, RolledBack, etc.)
CREATE INDEX IF NOT EXISTS idx_gov_actions_status
    ON archonai.governed_action_records (status);

-- Migration 017 applied successfully
