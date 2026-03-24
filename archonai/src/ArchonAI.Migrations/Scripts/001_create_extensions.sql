-- Migration 001: Create required PostgreSQL extensions and schemas
-- Idempotent: all statements use IF NOT EXISTS
-- Rollback: see Down/001_drop_extensions.sql

-- pgvector extension for semantic search / embedding similarity
CREATE EXTENSION IF NOT EXISTS vector;

-- uuid generation (available by default in PG 13+ but explicit for clarity)
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- Create the archonai schema used by all persistence stores
CREATE SCHEMA IF NOT EXISTS archonai;

-- Migration 001 applied successfully
