-- notnull / 00-seed.sql
-- BEFORE: a Customer entity whose Email attribute is currently optional
-- (Is Mandatory = No), populated with real-shaped data that includes empties --
-- exactly the rows that make "just set it to mandatory" dangerous.
--
-- DROP-CREATE IDEMPOTENT: prove-safe.sh re-applies this to reset state between
-- the naive and fixed runs, so it must rebuild from scratch, not append.

DROP TABLE IF EXISTS dbo.Customer;

CREATE TABLE dbo.Customer
(
    Id    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customer PRIMARY KEY,
    Name  NVARCHAR(100)     NOT NULL,
    Email NVARCHAR(200)     NULL          -- Is Mandatory = No, today
);

INSERT INTO dbo.Customer (Name, Email) VALUES
    (N'Ana Pereira',   N'ana@contoso.com'),
    (N'Bruno Costa',   NULL),                 -- empty: the hazard
    (N'Carla Dias',    N'carla@contoso.com'),
    (N'Diogo Lopes',   NULL),                 -- empty: the hazard
    (N'Eva Marques',   N'eva@contoso.com');
