-- rename / 00-seed.sql  (loop self-test, not a user deliverable)
-- BEFORE: a Person entity with FirstName populated. We will "rename" FirstName
-- to GivenName. Drop-create idempotent.
--
-- This scenario exists to PROVE THE LOOP'S DATA ORACLE: the naive rename raises
-- NO error (it runs clean) yet destroys every value. Only the per-table value
-- checksum catches it -- a row-count-only oracle would falsely report "survived".

DROP TABLE IF EXISTS dbo.Person;

CREATE TABLE dbo.Person
(
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Person PRIMARY KEY,
    FirstName NVARCHAR(100)     NOT NULL
);

INSERT INTO dbo.Person (FirstName) VALUES
    (N'Ana'), (N'Bruno'), (N'Carla');
