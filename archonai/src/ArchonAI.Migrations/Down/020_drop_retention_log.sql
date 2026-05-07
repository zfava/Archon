-- Rollback for Migration 020: Drop retention log table
-- WARNING: Destroys retention execution history.
DROP INDEX IF EXISTS archonai.idx_retention_log_ran_at_utc;
DROP TABLE IF EXISTS archonai.retention_log CASCADE;
