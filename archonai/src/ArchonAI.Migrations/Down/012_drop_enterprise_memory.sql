-- Rollback for Migration 012: Drop enterprise_memory_records table
DROP TABLE IF EXISTS archonai.enterprise_memory_records CASCADE;
