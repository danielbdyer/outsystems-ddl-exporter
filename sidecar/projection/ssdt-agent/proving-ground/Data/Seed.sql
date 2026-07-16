/*
  Data/Seed.sql — the real-SHAPED seed. (Build Action = None; included via post-deploy :r.)

  These rows are the WHOLE POINT of the proving ground. Each scenario plants exactly the data
  state that flips a bucket, so the SAME .sql edit classifies differently depending on which
  rows exist. Every block is commented with the flip it triggers.

  Idempotency contract: every MERGE is guarded. Re-running this seed with the data unchanged
  captures ZERO rows. An unconditional `WHEN MATCHED THEN UPDATE` would over-capture — that is
  the CDC-silence anti-proof; do not write it that way.

  Parents before children: Status/Account/Category seed before Customer/Product; Customer before
  CustomerAddress; Order before OrderLine (FK targets must exist first).

  NEW BLOCKS (2026-06-30) — enriched proving ground. Ordering enforced below:
    Status, Account, Category            (parent lookups / parent tables)
    -> Customer (now carries Region + AccountId), Product (now carries LegacyCode + CategoryId)
    -> CustomerAddress (1:1 child of Customer), Order (now carries StatusText)
    -> OrderLine (child of Order).
  SCRATCH-ONLY negatives (NEVER baked into this authored positive — see each module header):
    STR-02N (2nd CustomerAddress for one Customer), STA-03 negative (unmapped StatusText),
    STA-04N (hard-DELETE a referenced Category). Produce them in a throwaway copy per
    `../self-test/PROTOCOL.md`.
*/

SET NOCOUNT ON;
GO

----------------------------------------------------------------------------------------------
-- dbo.Status — the static / lookup rows. Explicit ids (no IDENTITY).
--   Adding a new value here (e.g. (4, N'Refunded', 1)) is the Declarative+Post-Deploy proof
--   (self-test prompt 6). Re-publishing with these unchanged must capture 0 rows.
----------------------------------------------------------------------------------------------
MERGE dbo.Status AS target
USING (VALUES
    (1, N'Pending',   1),
    (2, N'Shipped',   1),
    (3, N'Cancelled', 1)
) AS source (Id, Code, IsActive)
    ON target.Id = source.Id
WHEN MATCHED AND (
        target.Code     <> source.Code
     OR target.IsActive <> source.IsActive
    ) THEN
    UPDATE SET target.Code = source.Code, target.IsActive = source.IsActive
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Code, IsActive) VALUES (source.Id, source.Code, source.IsActive);
GO

----------------------------------------------------------------------------------------------
-- dbo.Account — move-attribute DESTINATION + split/merge partner (parent of Customer.AccountId).
--   3 rows, IDENTITY so IDENTITY_INSERT brackets the seed. Region populated so the STR-03 move
--   has a destination shape. Seeds BEFORE Customer (Customer.AccountId points here).
----------------------------------------------------------------------------------------------
SET IDENTITY_INSERT dbo.Account ON;
GO
MERGE dbo.Account AS target
USING (VALUES
    (1, N'Acme Holdings',   N'West'),
    (2, N'Globex Group',    N'East'),
    (3, N'Umbrella Parent', N'North')
) AS source (Id, Name, Region)
    ON target.Id = source.Id
WHEN MATCHED AND (
        target.Name <> source.Name
     OR ISNULL(target.Region, N'<NULL>') <> ISNULL(source.Region, N'<NULL>')
    ) THEN
    UPDATE SET target.Name = source.Name, target.Region = source.Region
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Name, Region) VALUES (source.Id, source.Name, source.Region);
GO
SET IDENTITY_INSERT dbo.Account OFF;
GO

----------------------------------------------------------------------------------------------
-- dbo.Category — second static/lookup entity (explicit ids, NO IDENTITY) + identity-swap source.
--   3 lookup rows. FK target for Product.CategoryId. STA-04N (delete-seed-value) hard-DELETEs a
--   REFERENCED row in a SCRATCH copy to fire the orphan negative — never here. Re-publish silent.
----------------------------------------------------------------------------------------------
MERGE dbo.Category AS target
USING (VALUES
    (1, N'Hardware', 1),
    (2, N'Software', 1),
    (3, N'Service',  1)
) AS source (Id, Code, IsActive)
    ON target.Id = source.Id
WHEN MATCHED AND (
        target.Code     <> source.Code
     OR target.IsActive <> source.IsActive
    ) THEN
    UPDATE SET target.Code = source.Code, target.IsActive = source.IsActive
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Code, IsActive) VALUES (source.Id, source.Code, source.IsActive);
GO

----------------------------------------------------------------------------------------------
-- dbo.Customer — make-mandatory FLIP seed.
--   Rows 1,2,4 have Email populated; rows 3 and 5 have Email NULL. The NULLs are what make
--   `Email NOT NULL` is blocked under Strict (self-test prompt 1). Re-seeding with ZERO NULLs still
--   blocks — the guard fires on row presence (COL-03C); only an EMPTY table publishes clean (COL-03B).
--   ContactPhone is populated so the rename proof has data to lose if the refactorlog is missing.
----------------------------------------------------------------------------------------------
--   Region (populated) is the move-attribute SOURCE (STR-03); AccountId links 1:1 to dbo.Account
--   for rows 1,2,4 and is NULL for the Email-NULL rows (3,5) so the make-mandatory seed is
--   untouched. Both columns are NULLABLE so the existing COL-03 flip is unaffected.
SET IDENTITY_INSERT dbo.Customer ON;
GO
MERGE dbo.Customer AS target
USING (VALUES
    (1, N'Acme Corp',      N'orders@acme.example',     N'+1-206-555-0101', N'West',    1),
    (2, N'Globex',         N'ap@globex.example',       N'+1-206-555-0102', N'East',    2),
    (3, N'Initech',        CAST(NULL AS NVARCHAR(256)), N'+1-206-555-0103', N'Central', CAST(NULL AS INT)),  -- Email NULL -> flips make-mandatory
    (4, N'Umbrella',       N'finance@umbrella.example', N'+1-206-555-0104', N'North',   3),
    (5, N'Stark Industries', CAST(NULL AS NVARCHAR(256)), N'+1-206-555-0105', N'South',  CAST(NULL AS INT)) -- Email NULL -> flips make-mandatory
) AS source (Id, Name, Email, ContactPhone, Region, AccountId)
    ON target.Id = source.Id
WHEN MATCHED AND (
        target.Name <> source.Name
     OR ISNULL(target.Email, N'<NULL>')        <> ISNULL(source.Email, N'<NULL>')
     OR ISNULL(target.ContactPhone, N'<NULL>') <> ISNULL(source.ContactPhone, N'<NULL>')
     OR ISNULL(target.Region, N'<NULL>')       <> ISNULL(source.Region, N'<NULL>')
     OR ISNULL(target.AccountId, -1)           <> ISNULL(source.AccountId, -1)
    ) THEN
    UPDATE SET target.Name = source.Name, target.Email = source.Email, target.ContactPhone = source.ContactPhone,
               target.Region = source.Region, target.AccountId = source.AccountId
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Name, Email, ContactPhone, Region, AccountId)
    VALUES (source.Id, source.Name, source.Email, source.ContactPhone, source.Region, source.AccountId);
GO
SET IDENTITY_INSERT dbo.Customer OFF;
GO

----------------------------------------------------------------------------------------------
-- dbo.Product — Ambitious Narrowing + add-unique FLIP seed.
--   'STANDARD-SKU-001' is 16 chars > 10 -> narrowing Code to NVARCHAR(10) truncates -> Strict
--     data-loss block (self-test prompt 5). MAX(LEN(Code)) probe predicts it.
--   The two 'DUPE' rows share Code -> add-unique on Code fails the unique index build on the dupe.
----------------------------------------------------------------------------------------------
--   LegacyCode (populated, NOT NULL) is the column whose data blocks a delete-attribute (COL-09); CategoryId
--   references dbo.Category (seeded above) and is what makes STA-04N's hard-DELETE orphan real.
--   Neither column touches Code, so narrow (COL-06) and add-unique (CON-02) are unaffected.
SET IDENTITY_INSERT dbo.Product ON;
GO
MERGE dbo.Product AS target
USING (VALUES
    (1, N'A100',             N'Widget',          N'LGC-A100', 1),
    (2, N'B200',             N'Gadget',          N'LGC-B200', 2),
    (3, N'STANDARD-SKU-001', N'Over-length code', N'LGC-003',  3),  -- 16 chars -> flips Ambitious Narrowing
    (4, N'DUPE',             N'First dupe',       N'LGC-004',  1),  -- duplicate Code -> flips add-unique
    (5, N'DUPE',             N'Second dupe',      N'LGC-005',  2)   -- duplicate Code -> flips add-unique
) AS source (Id, Code, Name, LegacyCode, CategoryId)
    ON target.Id = source.Id
WHEN MATCHED AND (
        target.Code <> source.Code
     OR ISNULL(target.Name, N'<NULL>') <> ISNULL(source.Name, N'<NULL>')
     OR target.LegacyCode <> source.LegacyCode
     OR ISNULL(target.CategoryId, -1) <> ISNULL(source.CategoryId, -1)
    ) THEN
    UPDATE SET target.Code = source.Code, target.Name = source.Name,
               target.LegacyCode = source.LegacyCode, target.CategoryId = source.CategoryId
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Code, Name, LegacyCode, CategoryId)
    VALUES (source.Id, source.Code, source.Name, source.LegacyCode, source.CategoryId);
GO
SET IDENTITY_INSERT dbo.Product OFF;
GO

----------------------------------------------------------------------------------------------
-- dbo.[Order] — Forgotten FK Check FLIP seed.
--   Orders 1-3 reference real Customers (1,2,4). Order 4 references CustomerId 999 which has NO
--   matching Customer -> ORPHAN. Adding a clean FK Order.CustomerId -> Customer.Id is blocked by
--   this orphan at deploy (self-test prompt 4). All StatusId values reference seeded Status rows.
----------------------------------------------------------------------------------------------
--   StatusText (free text) is the extract-to-lookup SOURCE (STA-03): every value maps to a
--   Status.Code ('Pending'/'Shipped'/'Cancelled'). The STA-03 negative adds an UNMAPPED value in
--   a SCRATCH copy — never here. StatusText is kept consistent with StatusId's Status.Code.
SET IDENTITY_INSERT dbo.[Order] ON;
GO
MERGE dbo.[Order] AS target
USING (VALUES
    (1, 1,   1, 120.00, N'Pending'),
    (2, 2,   2, 540.50, N'Shipped'),
    (3, 4,   3,  75.25, N'Cancelled'),
    (4, 999, 1, 300.00, N'Pending')   -- CustomerId 999 has NO parent -> orphan -> flips Forgotten FK Check
) AS source (Id, CustomerId, StatusId, Total, StatusText)
    ON target.Id = source.Id
WHEN MATCHED AND (
        target.CustomerId <> source.CustomerId
     OR target.StatusId   <> source.StatusId
     OR target.Total      <> source.Total
     OR target.StatusText <> source.StatusText
    ) THEN
    UPDATE SET target.CustomerId = source.CustomerId, target.StatusId = source.StatusId,
               target.Total = source.Total, target.StatusText = source.StatusText
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, CustomerId, StatusId, Total, StatusText)
    VALUES (source.Id, source.CustomerId, source.StatusId, source.Total, source.StatusText);
GO
SET IDENTITY_INSERT dbo.[Order] OFF;
GO

----------------------------------------------------------------------------------------------
-- dbo.CustomerAddress — EXACTLY ONE row per Customer (proven 1:1) for merge-tables (STR-02).
--   Only real Customers (1..5) get an address. STR-02N (a 2nd address for one Customer) is a
--   SCRATCH edit that breaks totality on purpose — NEVER baked here. Seeds AFTER Customer.
----------------------------------------------------------------------------------------------
SET IDENTITY_INSERT dbo.CustomerAddress ON;
GO
MERGE dbo.CustomerAddress AS target
USING (VALUES
    (1, 1, N'1 Acme Way',       N'Seattle',  N'98101'),
    (2, 2, N'2 Globex Blvd',    N'Bellevue', N'98004'),
    (3, 3, N'3 Initech Loop',   N'Redmond',  N'98052'),
    (4, 4, N'4 Umbrella Court', N'Tacoma',   N'98402'),
    (5, 5, N'5 Stark Tower',    N'Everett',  N'98201')
) AS source (Id, CustomerId, Line1, City, PostalCode)
    ON target.Id = source.Id
WHEN MATCHED AND (
        target.CustomerId <> source.CustomerId
     OR target.Line1      <> source.Line1
     OR ISNULL(target.City, N'<NULL>')       <> ISNULL(source.City, N'<NULL>')
     OR ISNULL(target.PostalCode, N'<NULL>') <> ISNULL(source.PostalCode, N'<NULL>')
    ) THEN
    UPDATE SET target.CustomerId = source.CustomerId, target.Line1 = source.Line1,
               target.City = source.City, target.PostalCode = source.PostalCode
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, CustomerId, Line1, City, PostalCode)
    VALUES (source.Id, source.CustomerId, source.Line1, source.City, source.PostalCode);
GO
SET IDENTITY_INSERT dbo.CustomerAddress OFF;
GO

----------------------------------------------------------------------------------------------
-- dbo.OrderLine — 2-3 lines per Order (deep FK graph) for define-pk composite (KEY-01) and the
--   Order->OrderLine cascade reach (KEY-04). Only real Orders (1..4) get lines. The
--   (OrderId, LineNumber) pairs are UNIQUE so the composite-PK claim holds. Seeds AFTER Order.
----------------------------------------------------------------------------------------------
SET IDENTITY_INSERT dbo.OrderLine ON;
GO
MERGE dbo.OrderLine AS target
USING (VALUES
    (1, 1, 1,  50.00),
    (2, 1, 2,  70.00),
    (3, 2, 1, 200.50),
    (4, 2, 2, 140.00),
    (5, 2, 3, 200.00),
    (6, 3, 1,  75.25),
    (7, 4, 1, 150.00),
    (8, 4, 2, 150.00)
) AS source (Id, OrderId, LineNumber, Amount)
    ON target.Id = source.Id
WHEN MATCHED AND (
        target.OrderId    <> source.OrderId
     OR target.LineNumber <> source.LineNumber
     OR target.Amount     <> source.Amount
    ) THEN
    UPDATE SET target.OrderId = source.OrderId, target.LineNumber = source.LineNumber, target.Amount = source.Amount
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, OrderId, LineNumber, Amount)
    VALUES (source.Id, source.OrderId, source.LineNumber, source.Amount);
GO
SET IDENTITY_INSERT dbo.OrderLine OFF;
GO

----------------------------------------------------------------------------------------------
-- dbo.CdcCandidate — a handful of rows for CDC proving. ISOLATED-DB ONLY (survival rule 1 /
--   PROTOCOL §8): enable CDC only inside a unique per-executor DB, never the shared warm
--   container. These rows just give the capture feed something to see.
----------------------------------------------------------------------------------------------
SET IDENTITY_INSERT dbo.CdcCandidate ON;
GO
MERGE dbo.CdcCandidate AS target
USING (VALUES
    (1, N'Alpha', N'first row'),
    (2, N'Beta',  N'second row'),
    (3, N'Gamma', CAST(NULL AS NVARCHAR(200)))
) AS source (Id, Name, Notes)
    ON target.Id = source.Id
WHEN MATCHED AND (
        target.Name <> source.Name
     OR ISNULL(target.Notes, N'<NULL>') <> ISNULL(source.Notes, N'<NULL>')
    ) THEN
    UPDATE SET target.Name = source.Name, target.Notes = source.Notes
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Name, Notes) VALUES (source.Id, source.Name, source.Notes);
GO
SET IDENTITY_INSERT dbo.CdcCandidate OFF;
GO

PRINT 'Seed applied. Flip scenarios: NULL emails (3,5), over-length+dupe Code (3,4,5), orphan order (4).';
PRINT 'Enriched (2026-06-30): Account/Category lookups, Customer.Region+AccountId, Product.LegacyCode+CategoryId,';
PRINT 'Order.StatusText, CustomerAddress (1:1), OrderLine (deep FK), CdcCandidate (ISOLATED-DB CDC only).';
GO
