-- Rollback for Migration 015: Drop policy_simulations table
DROP TABLE IF EXISTS archonai.policy_simulations CASCADE;
