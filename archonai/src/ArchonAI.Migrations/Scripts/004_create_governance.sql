-- Migration 004: Create governance tables (approval gates, policies, audit)
-- Human-in-the-loop approval gates with separation-of-duties enforcement.
-- Default approval policies seeded by the application on first startup.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/004_drop_governance.sql

CREATE TABLE IF NOT EXISTS archonai.approval_gates (
    id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    action_type       text        NOT NULL,
    resource_id       text        NOT NULL,
    tenant_id         text        NOT NULL,
    requested_by      text        NOT NULL,
    justification     text        NOT NULL,
    status            int         NOT NULL DEFAULT 0,  -- 0=Pending, 1=Approved, 2=Denied, 3=Expired
    reviewed_by       text,
    review_notes      text,
    requested_at_utc  timestamptz NOT NULL DEFAULT now(),
    reviewed_at_utc   timestamptz,
    action_payload    text,
    execution_status  int         NOT NULL DEFAULT 0,  -- 0=NotExecuted, 1=Succeeded, 2=Failed
    execution_error   text,
    executed_at_utc   timestamptz
);

-- Tenant-scoped queries for pending approvals
CREATE INDEX IF NOT EXISTS idx_approval_gates_tenant_id
    ON archonai.approval_gates (tenant_id);

-- Filter by approval status (pending, approved, denied)
CREATE INDEX IF NOT EXISTS idx_approval_gates_status
    ON archonai.approval_gates (status);

-- Filter by action type for policy matching
CREATE INDEX IF NOT EXISTS idx_approval_gates_action_type
    ON archonai.approval_gates (action_type);

CREATE TABLE IF NOT EXISTS archonai.approval_policies (
    id                          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    action_type                 text        NOT NULL,
    description                 text        NOT NULL,
    required_approver_role      text        NOT NULL,
    require_separation_of_duties boolean    NOT NULL DEFAULT false,
    is_enabled                  boolean     NOT NULL DEFAULT true,
    created_at_utc              timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS archonai.approval_audit_entries (
    id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    approval_gate_id  uuid        NOT NULL REFERENCES archonai.approval_gates(id) ON DELETE CASCADE,
    action_type       text        NOT NULL,
    tenant_id         text        NOT NULL,
    requested_by      text        NOT NULL,
    reviewed_by       text,
    outcome           int         NOT NULL,  -- maps to ApprovalStatus enum
    occurred_at_utc   timestamptz NOT NULL DEFAULT now()
);

-- Look up audit trail for a specific approval gate
CREATE INDEX IF NOT EXISTS idx_approval_audit_gate_id
    ON archonai.approval_audit_entries (approval_gate_id);

-- Tenant-scoped audit history
CREATE INDEX IF NOT EXISTS idx_approval_audit_tenant_id
    ON archonai.approval_audit_entries (tenant_id);

-- Migration 004 applied successfully
