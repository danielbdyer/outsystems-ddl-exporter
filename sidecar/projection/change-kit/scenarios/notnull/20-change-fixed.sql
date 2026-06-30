-- notnull / 20-change-fixed.sql
-- The remediation: backfill the empties FIRST (the value the developer chooses
-- for currently-empty records), THEN apply the mandatory constraint. In SSDT
-- this is the pre-deployment backfill + the declarative NOT NULL definition; the
-- pre-deployment script runs before the constraint applies, so the same release
-- is acceptable for ordinary table sizes.
-- (Pattern: 14-Multi-Phase-Pattern-Templates.md §17.2 NULL -> NOT NULL on a
--  Populated Table.)

-- Phase 1 -- backfill the currently-empty records (Option A: a literal default)
UPDATE dbo.Customer
SET Email = N'unknown@placeholder.com'
WHERE Email IS NULL;

-- Phase 2 -- now the constraint is safe to apply
ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(200) NOT NULL;
