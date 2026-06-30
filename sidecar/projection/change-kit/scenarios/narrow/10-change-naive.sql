-- narrow / 10-change-naive.sql  (loop self-test)
-- Naively narrow Code to NVARCHAR(10). EXPECTED TO BREAK: over-length rows exist.
-- (Anti-pattern: 16-Anti-Patterns-Gallery.md §19.4 The Ambitious Narrowing.)
-- Expected: Msg 2628/8152-class "String or binary data would be truncated".

ALTER TABLE dbo.Product ALTER COLUMN Code NVARCHAR(10) NOT NULL;
