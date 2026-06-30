-- notnull / 10-change-naive.sql
-- The change as an OutSystems developer first writes it: just make Email
-- mandatory. In Service Studio you would flip Is Mandatory = Yes and publish,
-- and the platform sorted out the existing rows. SSDT does not. This is the
-- declarative edit's effect -- and it is EXPECTED TO BREAK, because real rows
-- still hold NULL.
--
-- Expected: Msg 515, "Cannot insert the value NULL into column 'Email'".
-- (Anti-pattern: 16-Anti-Patterns-Gallery.md  §19.2 The Optimistic NOT NULL.)

ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(200) NOT NULL;
