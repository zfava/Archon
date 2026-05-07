-- Rollback for Migration 004: Drop governance tables
-- WARNING: Destroys all approval gates, policies, and audit entries.
DROP TABLE IF EXISTS archonai.approval_audit_entries CASCADE;
DROP TABLE IF EXISTS archonai.approval_policies CASCADE;
DROP TABLE IF EXISTS archonai.approval_gates CASCADE;
