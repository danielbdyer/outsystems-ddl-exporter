-- narrow / 00-seed.sql  (loop self-test, not a user deliverable)
-- BEFORE: a Product entity whose Code holds values longer than a proposed
-- narrower length. Drop-create idempotent.

DROP TABLE IF EXISTS dbo.Product;

CREATE TABLE dbo.Product
(
    Id   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Product PRIMARY KEY,
    Code NVARCHAR(50)      NOT NULL          -- generous today
);

INSERT INTO dbo.Product (Code) VALUES
    (N'SHORT'),
    (N'THIS-CODE-IS-DEFINITELY-LONGER-THAN-TEN-CHARS'),  -- over the new limit
    (N'OK10CHARS!');
