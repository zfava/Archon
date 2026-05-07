-- Rollback for Migration 009: Drop operational_exceptions table
DROP TABLE IF EXISTS archonai.operational_exceptions CASCADE;
