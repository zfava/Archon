-- Rollback for Migration 017: Drop action safety tables
DROP TABLE IF EXISTS archonai.governed_action_records CASCADE;
DROP TABLE IF EXISTS archonai.action_safety_classifications CASCADE;
