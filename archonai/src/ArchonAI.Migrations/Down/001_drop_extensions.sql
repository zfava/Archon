-- Rollback for Migration 001: Drop extensions and schema
-- WARNING: This will cascade-drop ALL tables in the archonai schema.
-- Only use for complete environment teardown.

DROP SCHEMA IF EXISTS archonai CASCADE;
DROP EXTENSION IF EXISTS pgcrypto;
-- NOTE: Do NOT drop the vector extension if other schemas use it.
-- DROP EXTENSION IF EXISTS vector;
