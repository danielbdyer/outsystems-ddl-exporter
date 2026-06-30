-- rename / 20-change-fixed.sql  (loop self-test)
-- The remediation: a true rename via sp_rename, which preserves the data. In
-- SSDT this is what the GUI rename (right-click -> Rename) records in the
-- .refactorlog so the generated script is a rename, not DROP + ADD.
-- EXPECTED TO SUCCEED with every value intact (BEFORE checksum == AFTER checksum).
-- (Anti-pattern + fix: 16-Anti-Patterns-Gallery.md §19.1; refactorlog discipline
--  in 09-The-Refactorlog-and-Rename-Discipline.md.)

EXEC sp_rename 'dbo.Person.FirstName', 'GivenName', 'COLUMN';
