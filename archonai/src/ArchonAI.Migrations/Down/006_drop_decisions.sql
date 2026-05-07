-- Rollback for Migration 006: Drop decision tables
-- WARNING: Cascades to financial_consequences (FK) and outcome_records (FK).
DROP TABLE IF EXISTS archonai.decision_lifecycle_events CASCADE;
DROP TABLE IF EXISTS archonai.decisions CASCADE;
