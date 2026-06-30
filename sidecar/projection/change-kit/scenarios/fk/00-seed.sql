-- fk / 00-seed.sql  (loop self-test, not a user deliverable)
-- BEFORE: Orders, some pointing at a Customer that does not exist (orphans).
-- Drop-create idempotent; drop child before parent.

DROP TABLE IF EXISTS dbo.[Order];
DROP TABLE IF EXISTS dbo.Customer;

CREATE TABLE dbo.Customer
(
    CustomerId INT NOT NULL CONSTRAINT PK_Customer PRIMARY KEY,
    Name       NVARCHAR(100) NOT NULL
);
INSERT INTO dbo.Customer (CustomerId, Name) VALUES
    (1, N'Ana Pereira'),
    (2, N'Bruno Costa');

CREATE TABLE dbo.[Order]
(
    OrderId    INT NOT NULL CONSTRAINT PK_Order PRIMARY KEY,
    CustomerId INT NOT NULL          -- no FK yet
);
INSERT INTO dbo.[Order] (OrderId, CustomerId) VALUES
    (10, 1),
    (11, 2),
    (12, 99);                        -- orphan: Customer 99 does not exist
