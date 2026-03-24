-- Rollback for Migration 016: Drop proof_events table
DROP TABLE IF EXISTS archonai.proof_events CASCADE;
