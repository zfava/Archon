-- Rollback for Migration 013: Drop monitoring tables
DROP TABLE IF EXISTS archonai.monitoring_counters CASCADE;
DROP TABLE IF EXISTS archonai.monitoring_snapshots CASCADE;
