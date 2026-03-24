ALTER TABLE archonai.retention_log ADD COLUMN IF NOT EXISTS inspection_rows_deleted bigint NOT NULL DEFAULT 0;
