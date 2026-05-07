-- Rollback for Migration 018: Drop inspection tables
DROP TABLE IF EXISTS archonai.inspection_workflow_diagnostics CASCADE;
DROP TABLE IF EXISTS archonai.inspection_memory_references CASCADE;
DROP TABLE IF EXISTS archonai.inspection_policy_evaluations CASCADE;
