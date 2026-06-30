-- fk / 10-change-naive.sql  (loop self-test)
-- Naively add the FK without checking for orphans. EXPECTED TO BREAK: order 12
-- references a non-existent customer.
-- (Anti-pattern: 16-Anti-Patterns-Gallery.md §19.3 The Forgotten FK Check.)
-- Expected: Msg 547 "The ALTER TABLE statement conflicted with the FOREIGN KEY".

ALTER TABLE dbo.[Order]
    ADD CONSTRAINT FK_Order_Customer
    FOREIGN KEY (CustomerId) REFERENCES dbo.Customer(CustomerId);
