-- Migration 003: Create RBAC tables (roles, assignments, policies)
-- Role-based access control with policy evaluation engine.
-- System roles (Admin, Operator, Viewer) are seeded by the application on first startup.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/003_drop_rbac.sql

CREATE TABLE IF NOT EXISTS archonai.rbac_roles (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    name           text        NOT NULL,
    description    text        NOT NULL,
    permissions    jsonb       NOT NULL DEFAULT '[]'::jsonb,
    is_system      boolean     NOT NULL DEFAULT false,
    created_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS archonai.rbac_assignments (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    subject_id      text        NOT NULL,
    subject_type    text        NOT NULL,
    role_id         uuid        NOT NULL REFERENCES archonai.rbac_roles(id) ON DELETE CASCADE,
    assigned_by     text        NOT NULL,
    assigned_at_utc timestamptz NOT NULL DEFAULT now()
);

-- Look up all assignments for a subject (user/agent/service)
CREATE INDEX IF NOT EXISTS idx_rbac_assignments_subject_id
    ON archonai.rbac_assignments (subject_id);

-- Cascade cleanup: find assignments by role
CREATE INDEX IF NOT EXISTS idx_rbac_assignments_role_id
    ON archonai.rbac_assignments (role_id);

CREATE TABLE IF NOT EXISTS archonai.rbac_policies (
    id                   uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    name                 text        NOT NULL,
    description          text        NOT NULL,
    required_permissions jsonb       NOT NULL DEFAULT '[]'::jsonb,
    resource             text        NOT NULL,
    effect               text        NOT NULL,  -- 'allow' or 'deny'
    conditions           jsonb       NOT NULL DEFAULT '{}'::jsonb,
    is_enabled           boolean     NOT NULL DEFAULT true,
    created_at_utc       timestamptz NOT NULL DEFAULT now()
);

-- Migration 003 applied successfully
