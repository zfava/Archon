-- Rollback for Migration 010: Drop outcome_records table
DROP TABLE IF EXISTS archonai.outcome_records CASCADE;
