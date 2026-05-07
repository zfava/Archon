-- Rollback for Migration 024: Drop control plane alert and system state tables
-- WARNING: Destroys system pause state, active alerts, and agent activity event history.
DROP TABLE IF EXISTS archonai.control_plane_agent_events CASCADE;
DROP TABLE IF EXISTS archonai.control_plane_alerts CASCADE;
DROP TABLE IF EXISTS archonai.control_plane_system_state CASCADE;
