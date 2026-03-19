-- Rollback for Migration 014: Drop hero_workflow_instances table
DROP TABLE IF EXISTS archonai.hero_workflow_instances CASCADE;
