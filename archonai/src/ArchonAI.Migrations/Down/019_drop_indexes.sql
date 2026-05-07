-- Rollback for Migration 019: Drop cross-table composite indexes
-- These are safe to drop without losing data — only query performance is affected.
DROP INDEX IF EXISTS archonai.idx_audit_log_category_time;
DROP INDEX IF EXISTS archonai.idx_decisions_tenant_status;
DROP INDEX IF EXISTS archonai.idx_decisions_tenant_domain;
DROP INDEX IF EXISTS archonai.idx_opex_tenant_status_severity;
DROP INDEX IF EXISTS archonai.idx_opex_open;
DROP INDEX IF EXISTS archonai.idx_emr_tenant_layer;
DROP INDEX IF EXISTS archonai.idx_emr_session_expiry;
DROP INDEX IF EXISTS archonai.idx_proof_events_tenant_type;
DROP INDEX IF EXISTS archonai.idx_proof_events_tenant_action;
DROP INDEX IF EXISTS archonai.idx_gov_actions_tenant_type;
DROP INDEX IF EXISTS archonai.idx_hero_wf_tenant_status;
