-- Rollback for Migration 007: Drop financial_consequences table
DROP TABLE IF EXISTS archonai.financial_consequences CASCADE;
