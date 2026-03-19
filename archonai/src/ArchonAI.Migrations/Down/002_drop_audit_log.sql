-- Rollback for Migration 002: Drop audit_log table
-- WARNING: Destroys the entire immutable audit trail. Data is unrecoverable.
DROP TABLE IF EXISTS archonai.audit_log CASCADE;
