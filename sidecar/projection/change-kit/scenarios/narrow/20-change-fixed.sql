-- narrow / 20-change-fixed.sql  (loop self-test)
-- The lossless remediation: pick a width that fits the real data. Verified by
-- MAX(LEN(Code)) first; here we widen the target to 50 (i.e. keep the safe
-- width) rather than truncate. EXPECTED TO SUCCEED with every value intact.
-- (Pattern: check MAX(LEN(...)) before narrowing; if data exceeds the new limit,
--  clean it first or reconsider -- 16-Anti-Patterns-Gallery.md §19.4 "The fix".)

ALTER TABLE dbo.Product ALTER COLUMN Code NVARCHAR(50) NOT NULL;
