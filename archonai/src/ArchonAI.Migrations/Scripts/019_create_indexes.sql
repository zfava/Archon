-- Migration 019: Create cross-table composite indexes and performance indexes
-- These indexes support common cross-service query patterns not covered
-- by the per-table indexes in earlier migrations.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/019_drop_indexes.sql

-- ── Audit Log ─────────────────────────────────────────────────
-- Combined filter: category + time range (common dashboard query)
CREATE INDEX IF NOT EXISTS idx_audit_log_category_time
    ON archonai.audit_log (category, occurred_at_utc DESC);

-- ── Decisions ─────────────────────────────────────────────────
-- Combined filter: tenant + status + domain (common list query)
CREATE INDEX IF NOT EXISTS idx_decisions_tenant_status
    ON archonai.decisions (tenant_id, status);

-- Combined filter: tenant + domain (domain-scoped queries)
CREATE INDEX IF NOT EXISTS idx_decisions_tenant_domain
    ON archonai.decisions (tenant_id, domain);

-- ── Operational Exceptions ────────────────────────────────────
-- Combined filter: tenant + status + severity (priority queue)
CREATE INDEX IF NOT EXISTS idx_opex_tenant_status_severity
    ON archonai.operational_exceptions (tenant_id, status, severity);

-- Open exceptions only (partial index for queue queries)
CREATE INDEX IF NOT EXISTS idx_opex_open
    ON archonai.operational_exceptions (tenant_id, severity DESC)
    WHERE status = 0;  -- ExceptionStatus.Open

-- ── Enterprise Memory ─────────────────────────────────────────
-- Combined filter: tenant + layer (common query pattern)
CREATE INDEX IF NOT EXISTS idx_emr_tenant_layer
    ON archonai.enterprise_memory_records (tenant_id, layer);

-- Session memory cleanup: find expired session records efficiently
CREATE INDEX IF NOT EXISTS idx_emr_session_expiry
    ON archonai.enterprise_memory_records (created_at_utc)
    WHERE layer = 0 AND expires_at_utc IS NOT NULL;

-- ── Proof Analytics ───────────────────────────────────────────
-- Combined filter: tenant + event type (analytics queries)
CREATE INDEX IF NOT EXISTS idx_proof_events_tenant_type
    ON archonai.proof_events (tenant_id, event_type);

-- Combined filter: tenant + action type (trust analytics)
CREATE INDEX IF NOT EXISTS idx_proof_events_tenant_action
    ON archonai.proof_events (tenant_id, action_type)
    WHERE action_type IS NOT NULL;

-- ── Governed Actions ──────────────────────────────────────────
-- Combined filter: tenant + action type (safety analytics)
CREATE INDEX IF NOT EXISTS idx_gov_actions_tenant_type
    ON archonai.governed_action_records (tenant_id, action_type);

-- ── Hero Workflows ────────────────────────────────────────────
-- Combined filter: tenant + status (active workflow listing)
CREATE INDEX IF NOT EXISTS idx_hero_wf_tenant_status
    ON archonai.hero_workflow_instances (tenant_id, status);

-- Migration 019 applied successfully
