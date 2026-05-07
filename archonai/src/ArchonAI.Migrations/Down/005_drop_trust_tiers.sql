-- Rollback for Migration 005: Drop trust_tier_policies table
DROP TABLE IF EXISTS archonai.trust_tier_policies CASCADE;
