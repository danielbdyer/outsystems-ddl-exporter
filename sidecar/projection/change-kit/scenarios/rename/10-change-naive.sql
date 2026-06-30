-- rename / 10-change-naive.sql  (loop self-test)
-- The naked rename: a plain text edit of the column name in the .sql file makes
-- SSDT see "FirstName gone, GivenName new" and generate DROP + ADD. Here is what
-- that generates. It raises NO ERROR (rc = 0) and SILENTLY destroys every value.
-- The loop catches this via the value checksum, not the row count.
-- (Anti-pattern: 16-Anti-Patterns-Gallery.md §19.1 The Naked Rename.)

ALTER TABLE dbo.Person DROP COLUMN FirstName;
ALTER TABLE dbo.Person ADD GivenName NVARCHAR(100) NULL;
