-- fk / 20-change-fixed.sql  (loop self-test)
-- The remediation: deal with the orphans FIRST, then add a trusted FK. Here we
-- delete the orphan rows (Option A); the alternative is the WITH NOCHECK ->
-- clean -> WITH CHECK CHECK pattern. EXPECTED TO SUCCEED and the FK is trusted.
-- (Pattern: 14-Multi-Phase-Pattern-Templates.md §17.4 Add FK with Orphan Data;
--  detector query in 16-Anti-Patterns-Gallery.md §19.3 "The fix".)

DELETE o
FROM dbo.[Order] o
LEFT JOIN dbo.Customer c ON c.CustomerId = o.CustomerId
WHERE c.CustomerId IS NULL;

ALTER TABLE dbo.[Order]
    ADD CONSTRAINT FK_Order_Customer
    FOREIGN KEY (CustomerId) REFERENCES dbo.Customer(CustomerId);
