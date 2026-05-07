-- Rollback for Migration 021: Drop agent registry tables
-- WARNING: Destroys all registered agent definitions and collected metrics.
-- Drop in reverse dependency order (agent_metrics references registered_agents).
DROP TABLE IF EXISTS archonai.agent_metrics CASCADE;
DROP TABLE IF EXISTS archonai.registered_agents CASCADE;
