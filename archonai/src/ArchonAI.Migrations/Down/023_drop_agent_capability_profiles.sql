-- Rollback for Migration 023: Drop agent capability profile tables
-- WARNING: Destroys all agent capability profiles and execution samples.
-- Drop in reverse dependency order (agent_execution_samples references agent_capability_profiles).
DROP TABLE IF EXISTS archonai.agent_execution_samples CASCADE;
DROP TABLE IF EXISTS archonai.agent_capability_profiles CASCADE;
