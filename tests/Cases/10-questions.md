# 10 · Questions — `Get-FxQuestions`

*Delta text in, questions out (ARCH §3–§4): every vocabulary row, every DacFx statement shape seen in a real delta, the three statement classes and the closed-world stop (F4), the coordinate rules (S3) and the authored detection. Input sub-fields: `delta` (literal DacFx text, blank lines dropped), `base` (the environment's column catalog, which ARCH §3 hands Get-FxQuestions: parsed offline from schema.<env>.dacpac's model.xml), `scripts` (an authored file at head and at the merge-base).*
*Questions are in canonical order (§4 row order, then coordinate; `dbo.` dropped); `classes`, `measures Rows(T)` (counts for the scale flag; `for Scan(T)` marks a data-insensitive statement, which asks nothing) and `discharged` (answered from the delta, ARCH §4 Coordinates rule 1) are asserted only where a case states them. "Scratch run" = a shape seen once on 2026-10-18 (sqlpackage 170.5.96, SQL Server 2022, the proving ground): not a receipt.*

---

## Header, preamble and blocks that are not guards

### QST-01 · the data-loss guard, exactly as sqlpackage 170.5.96 writes it
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
expect:   questions  HasRows(Customer)
laws:     F4 M1 E1
source:   receipt · TWIN_SUCCESSION §6b boundary experiment (2026-10-15, sqlpackage 170.5.96): the guard lines of the generated delta, verbatim
```

### QST-02 · the boundary delta whole: guard, two ALTER COLUMNs, a unique build, an FK pair, a view refresh
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      GO
                     USE [$(DatabaseName)];
                     GO
                     IF EXISTS (SELECT 1
                                FROM   [master].[dbo].[sysdatabases]
                                WHERE  [name] = N'$(DatabaseName)')
                         BEGIN
                             ALTER DATABASE [$(DatabaseName)]
                                 SET QUERY_STORE (QUERY_CAPTURE_MODE = AUTO, OPERATION_MODE = READ_WRITE, MAX_STORAGE_SIZE_MB = 1000) 
                                 WITH ROLLBACK IMMEDIATE;
                         END
                     GO
                     /*
                     The type for column Code in table [dbo].[Customer] is currently  NVARCHAR (20) NOT NULL but is being changed to  NVARCHAR (10) NOT NULL. Data loss could occur and deployment may fail if the column contains data that is incompatible with type  NVARCHAR (10) NOT NULL.
                     The column Email on table [dbo].[Customer] must be changed from NULL to NOT NULL. If the table contains data, the ALTER script may not work. To avoid this issue, you must add values to this column for all rows or mark it as allowing NULL values, or enable the generation of smart-defaults as a deployment option.
                     */
                     IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     PRINT N'Altering Table [dbo].[Customer]...';
                     GO
                     ALTER TABLE [dbo].[Customer] ALTER COLUMN [Code] NVARCHAR (10) NOT NULL;
                     ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (100) NOT NULL;
                     GO
                     PRINT N'Creating Index [dbo].[Customer].[UX_Customer_Code]...';
                     GO
                     CREATE UNIQUE NONCLUSTERED INDEX [UX_Customer_Code]
                         ON [dbo].[Customer]([Code] ASC);
                     GO
                     PRINT N'Creating Foreign Key [dbo].[FK_Order_Customer]...';
                     GO
                     ALTER TABLE [dbo].[Order] WITH NOCHECK
                         ADD CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]);
                     GO
                     PRINT N'Refreshing View [dbo].[vCustomer]...';
                     GO
                     SET ANSI_NULLS ON;
                     SET QUOTED_IDENTIFIER OFF;
                     GO
                     EXECUTE sp_refreshsqlmodule N'[dbo].[vCustomer]';
                     GO
                     SET ANSI_NULLS, QUOTED_IDENTIFIER ON;
                     GO
                     PRINT N'Checking existing data against newly created constraints';
                     GO
                     USE [$(DatabaseName)];
                     GO
                     ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Customer];
                     GO
                     PRINT N'Update complete.';
                     GO
          base       Customer.Code nvarchar(20) NOT NULL SQL_Latin1_General_CP1_CI_AS · Customer.Email nvarchar(100) NULL · Order.CustomerId int NOT NULL
expect:   questions  HasRows(Customer) · NotNull(Customer.Email) · Fits(Customer.Code→nvarchar(10)) classes {overlen, overbytes}
                     · Unique(Customer,(Code)) classes {exact, case, trailing} · Orphan(Order.(CustomerId)→Customer)
                     · measures Rows(Customer), Rows(Order)
laws:     F4 D1 M1
source:   receipt · TWIN_SUCCESSION §6b (the VIEW DEFINITION-only Script generated "the data-loss guard, both ALTER COLUMNs, the unique-index build and the FK's WITH CHECK CHECK"; delta_r_meta.sql)
note:     Code was NOT NULL in base, so no NotNull(Code); Email keeps nvarchar(100), so no Fits(Email). The comment's
          "NOT NULL" text, the QUERY_STORE IF EXISTS, the view refresh and the SET / USE batches ask nothing.
```

### QST-03 · the offline and the live delta of one change ask the same questions
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      QST-02's input, and the same text with `:setvar DefaultDataPath ""` and `:setvar DefaultLogPath ""`
                     in the header (the offline Script of the same head against the extracted schema)
expect:   questions  identical for both, byte for byte in the questions JSON
laws:     F5 D1
source:   receipt · F5 · TWIN_SUCCESSION §0.1 (the offline delta, guard included, is identical to the live one apart from the header and :setvar lines)
```

### QST-04 · the generated header and the SQLCMD preamble ask nothing
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      /*
                     Deployment script for bd_probe
                     This code was generated by a tool.
                     Changes to this file may cause incorrect behavior and will be lost if
                     the code is regenerated.
                     */
                     GO
                     SET ANSI_NULLS, ANSI_PADDING, ANSI_WARNINGS, ARITHABORT, CONCAT_NULL_YIELDS_NULL, QUOTED_IDENTIFIER ON;
                     SET NUMERIC_ROUNDABORT OFF;
                     GO
                     :setvar DatabaseName "bd_probe"
                     :setvar DefaultFilePrefix "bd_probe"
                     :setvar DefaultDataPath "/var/opt/mssql/data/"
                     :setvar DefaultLogPath "/var/opt/mssql/data/"
                     GO
                     :on error exit
                     GO
                     /*
                     Detect SQLCMD mode and disable script execution if SQLCMD mode is not supported.
                     To re-enable the script after enabling SQLCMD mode, execute the following:
                     SET NOEXEC OFF; 
                     */
                     :setvar __IsSqlCmdEnabled "True"
                     GO
                     IF N'$(__IsSqlCmdEnabled)' NOT LIKE N'True'
                         BEGIN
                             PRINT N'SQLCMD mode must be enabled to successfully execute this script.';
                             SET NOEXEC ON;
                         END
                     GO
                     USE [$(DatabaseName)];
                     GO
expect:   questions  none (and no stop)
laws:     F4 D1
source:   receipt · TWIN_SUCCESSION §6b (the boundary delta's header, sqlpackage 170.5.96)
note:     Run twice: as shown, and as the real file is written (a UTF-8 BOM, CRLF on the header comment lines,
          LF elsewhere). Both ask nothing.
```

### QST-05 · a database-option IF EXISTS is not a guard
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (SELECT 1
                                FROM   [master].[dbo].[sysdatabases]
                                WHERE  [name] = N'$(DatabaseName)')
                         BEGIN
                             ALTER DATABASE [$(DatabaseName)]
                                 SET QUERY_STORE (QUERY_CAPTURE_MODE = AUTO, OPERATION_MODE = READ_WRITE, MAX_STORAGE_SIZE_MB = 1000) 
                                 WITH ROLLBACK IMMEDIATE;
                         END
                     GO
expect:   questions  none
laws:     F4 H5
source:   receipt · TWIN_SUCCESSION §6b (boundary delta); the not-a-guard rule is red-team #12's disposition
note:     An IF EXISTS is a guard only with the exact RAISERROR of QST-01 beneath it. This one reads master's catalog,
          never a user table.
```

### QST-06 · database SET options inside the sysdatabases wrapper ask nothing
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (SELECT 1
                                FROM   [master].[dbo].[sysdatabases]
                                WHERE  [name] = N'$(DatabaseName)')
                         BEGIN
                             ALTER DATABASE [$(DatabaseName)]
                                 SET ANSI_NULLS ON,
                                     ANSI_PADDING ON,
                                     QUOTED_IDENTIFIER ON 
                                 WITH ROLLBACK IMMEDIATE;
                         END
                     GO
expect:   questions  none
laws:     F4
source:   expected · the wrapper is the receipt's (QST-05); the SET list is the shape DacFx writes when a database option differs
```

### QST-07 · comment text never asks: block and line comments are stripped before matching
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      /*
                     The column [dbo].[Customer].[Email] is being dropped, data loss could occur.
                     ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
                     */
                     -- CREATE UNIQUE NONCLUSTERED INDEX [UX_Customer_Email] ON [dbo].[Customer]([Email] ASC);
                     PRINT N'Update complete.';
                     GO
expect:   questions  none
laws:     F4
source:   expected · the guard batch of every real delta opens with such a comment (QST-02)
```

## HasRows

### QST-08 · two guarded tables ask one HasRows each
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     IF EXISTS (select top 1 1 from [dbo].[Product])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
expect:   questions  HasRows(Customer) · HasRows(Product)
laws:     F4 D1
source:   verify · whether DacFx writes several guards in one batch or one batch each; the questions are the same either way, and CLS-07 attributes a block inside a multi-guard batch by its SQL72014 line
```

### QST-09 · the same table guarded twice asks one HasRows
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
expect:   questions  HasRows(Customer)
laws:     D1 M2
source:   expected · ARCH §5 step 2 (one presence row per populated table)
```

### QST-10 · a guard and an ADD … NOT NULL without a default on one table ask one HasRows
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     PRINT N'Altering Table [dbo].[Customer]...';
                     GO
                     ALTER TABLE [dbo].[Customer]
                         ADD [Segment] NVARCHAR (20) NOT NULL;
                     GO
expect:   questions  HasRows(Customer)
laws:     F4 D1
source:   receipt · SamplePr "add-mandatory: a new NOT NULL column with no default blocks a populated table, applies when empty"
note:     The ADD reads nothing but row presence: it fails on any row (Msg 4901, the permissive leg, CLS-41), which is
          exactly the guard's question, so the two sources fold into one.
```

### QST-11 · the reserved word [Order]
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Order])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
expect:   questions  HasRows(Order)
laws:     F4 D1
source:   expected · DacFx bracket-quotes every identifier (QST-02 writes [dbo].[Order])
```

### QST-12 · a non-dbo schema stays in the coordinate
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [archive].[OrderArchive])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
expect:   questions  HasRows(archive.OrderArchive)
laws:     F4 D1
source:   expected · ARCH §2 row 9 (coordinates are plain schema.table.column)
```

### QST-13 · keywords match case-folded and whitespace-collapsed; the RAISERROR text and arguments do not
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (SELECT TOP 1 1
                                FROM   [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
                             WITH NOWAIT;
                     GO
expect:   questions  HasRows(Customer)
laws:     F4 H5
source:   verify · this is how SQL72045 re-formats the guard when it quotes it (scratch run); matching must hold for both spellings
```

### QST-14 · a guard-shaped batch whose RAISERROR is not the guard's stops
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      GO
                     IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 1) WITH NOWAIT
                     GO
expect:   outcome    stop unknown-statement · "Delta line 2 reads rows, but no vocabulary row matches it. Add a row with its case." · exit 9
laws:     F4 H3 H5
source:   expected · red-team #12 (only the exact 16, 127 batch is the guard); a main-region batch that reads a table and is not a vocabulary row stops
```

### QST-15 · the guard spelled without WITH NOWAIT stops instead of guessing
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
                     GO
expect:   outcome    stop unknown-statement · "Delta line 1 reads rows, but no vocabulary row matches it" · exit 9
laws:     F4 E1
source:   verify · ARCH §11.3: whether DacFx 162.x writes the guard as 170.x does. A different spelling enters only as a vocabulary row with its case (E1)
```

## NotNull

### QST-16 · NULL → NOT NULL on the same type asks NotNull
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
                     GO
          base       Customer.Email nvarchar(256) NULL
expect:   questions  HasRows(Customer) · NotNull(Customer.Email) · measures Rows(Customer)
laws:     F4 M1
source:   receipt · F7 · SamplePr "make OrderLine.Note mandatory: production publish blocks a populated table, applies when empty"
note:     DacFx restates the whole column, so whether nullability or type changed is read from `base`, the environment's
          column catalog that ARCH §3 hands Get-FxQuestions (parsed offline from schema.<env>.dacpac's model.xml).
```

### QST-17 · NOT NULL restated on a column already NOT NULL asks no NotNull; a narrowing asks Fits
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Product])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Product] ALTER COLUMN [Code] NVARCHAR (10) NOT NULL;
                     GO
          base       Product.Code nvarchar(50) NOT NULL
expect:   questions  HasRows(Product) · Fits(Product.Code→nvarchar(10)) classes {overlen, overbytes}
laws:     F4 M1
source:   receipt · F1 (the guarded narrowing) · red-team #10 (a narrowing silently trims trailing spaces ✓: the overbytes class)
note:     overbytes is a value that fits by LEN but not by length-with-trailing-spaces; the ALTER trims it without an
          error, so it changes data without blocking.
```

### QST-18 · NULL → NOT NULL and a narrower type in one statement ask both
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (100) NOT NULL;
                     GO
          base       Customer.Email nvarchar(256) NULL
expect:   questions  HasRows(Customer) · NotNull(Customer.Email) · Fits(Customer.Email→nvarchar(100)) classes {overlen, overbytes}
laws:     F4
source:   expected · ARCH §4 (NotNull and Fits are separate rows over one statement)
```

## Fits: which value classes each type change triggers

### QST-19 · a widen asks nothing (but is measured)
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (400) NULL;
                     GO
          base       Customer.Email nvarchar(256) NULL
expect:   questions  none · measures Rows(Customer)
laws:     F4 M1
source:   receipt · SamplePr "widen: enlarging a column applies clean and preserves every value"
```

### QST-20 · a loosening to NULL asks nothing
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[CustomerAddress] ALTER COLUMN [Line1] NVARCHAR (120) NULL;
                     GO
          base       CustomerAddress.Line1 nvarchar(120) NOT NULL
expect:   questions  none · measures Rows(CustomerAddress)
laws:     F4
source:   receipt · SamplePr "make-optional: relaxing NOT NULL to NULL applies clean on a populated table"
```

### QST-21 · int → bigint asks nothing
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[OrderLine] ALTER COLUMN [LineNumber] BIGINT NOT NULL;
                     GO
          base       OrderLine.LineNumber int NOT NULL
expect:   questions  none · measures Rows(OrderLine)
laws:     F4
source:   receipt · SamplePr "retype-implicit: a widening type change applies clean and preserves every value"
```

### QST-22 · decimal(18,2) → int: roundtrip and overflow
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Order])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Order] ALTER COLUMN [Total] INT NOT NULL;
                     GO
          base       Order.Total decimal(18,2) NOT NULL
expect:   questions  HasRows(Order) · Fits(Order.Total→int) classes {roundtrip, overflow}
laws:     F4 M1
source:   receipt · F12 retype-explicit (refused with SQL72015 + Msg 50000; every value converts, 2 rows lose their cents)
```

### QST-23 · a string → int: nonconvertible and empty
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Product])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Product] ALTER COLUMN [Code] INT NOT NULL;
                     GO
          base       Product.Code nvarchar(50) NOT NULL
expect:   questions  HasRows(Product) · Fits(Product.Code→int) classes {nonconvertible, empty}
laws:     F4
source:   receipt · red-team #10 (TRY_CONVERT('') varies by type ✓) · F12 (TRY_CONVERT(INT, Product.Code) is NULL for all 5 rows)
note:     `''` becomes 0 without an error, so it is its own class. A digit string beyond int's range is nonconvertible
          to TRY_CONVERT, and raises Msg 248 at publish (CLS-42).
```

### QST-24 · a string → decimal: nonconvertible, empty, overflow, roundtrip
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[T])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[T] ALTER COLUMN [Ref] DECIMAL (9, 2) NULL;
                     GO
          base       T.Ref varchar(20) NULL
expect:   questions  HasRows(T) · Fits(T.Ref→decimal(9,2)) classes {nonconvertible, empty, overflow, roundtrip}
laws:     F4
source:   expected · TWIN_SUCCESSION §5 (TRY_CONVERT gives NULL for decimal on `''`, so `''` fails here where it passes for int)
```

### QST-25 · a string → date: nonconvertible, empty, roundtrip
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Customer] ALTER COLUMN [BirthText] DATE NULL;
                     GO
          base       Customer.BirthText nvarchar(20) NULL
expect:   questions  HasRows(Customer) · Fits(Customer.BirthText→date) classes {nonconvertible, empty, roundtrip}
laws:     F4
source:   expected · TWIN_SUCCESSION §6b (the BirthText profile: a slash date converts month-first when it was written day-first, without an error)
note:     `''` becomes 1900-01-01 silently (TWIN_SUCCESSION §5).
```

### QST-26 · a string → uniqueidentifier: nonconvertible and empty
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Account])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Account] ALTER COLUMN [ExternalKey] UNIQUEIDENTIFIER NULL;
                     GO
          base       Account.ExternalKey nvarchar(36) NULL
expect:   questions  HasRows(Account) · Fits(Account.ExternalKey→uniqueidentifier) classes {nonconvertible, empty}
laws:     F4
source:   expected · TWIN_SUCCESSION §5 (TRY_CONVERT gives NULL for GUID on `''`)
```

### QST-27 · decimal(18,2) → decimal(9,2): overflow
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[OrderLine])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[OrderLine] ALTER COLUMN [Amount] DECIMAL (9, 2) NOT NULL;
                     GO
          base       OrderLine.Amount decimal(18,2) NOT NULL
expect:   questions  HasRows(OrderLine) · Fits(OrderLine.Amount→decimal(9,2)) classes {overflow}
laws:     F4
source:   expected · ARCH §4 (overflow: precision or scale)
```

### QST-28 · decimal(18,4) → decimal(18,2): roundtrip
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[OrderLine])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[OrderLine] ALTER COLUMN [Rate] DECIMAL (18, 2) NOT NULL;
                     GO
          base       OrderLine.Rate decimal(18,4) NOT NULL
expect:   questions  HasRows(OrderLine) · Fits(OrderLine.Rate→decimal(18,2)) classes {roundtrip}
laws:     F4
source:   verify · whether an ALTER COLUMN that reduces scale rounds silently (roundtrip) or raises (then overflow is the class)
```

### QST-29 · int → smallint: overflow
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[OrderLine])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[OrderLine] ALTER COLUMN [LineNumber] SMALLINT NOT NULL;
                     GO
          base       OrderLine.LineNumber int NOT NULL
expect:   questions  HasRows(OrderLine) · Fits(OrderLine.LineNumber→smallint) classes {overflow}
laws:     F4
source:   expected · ARCH §4
```

### QST-30 · nvarchar → varchar of the same length: roundtrip
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Account])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Account] ALTER COLUMN [Name] VARCHAR (100) NOT NULL;
                     GO
          base       Account.Name nvarchar(100) NOT NULL SQL_Latin1_General_CP1_CI_AS
expect:   questions  HasRows(Account) · Fits(Account.Name→varchar(100)) classes {roundtrip}
laws:     F4
source:   expected · characters outside the column's code page become '?' without an error
```

### QST-31 · datetime2(7) → date: roundtrip
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Order])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Order] ALTER COLUMN [PlacedOn] DATE NOT NULL;
                     GO
          base       Order.PlacedOn datetime2(7) NOT NULL
expect:   questions  HasRows(Order) · Fits(Order.PlacedOn→date) classes {roundtrip}
laws:     F4
source:   expected · the time part is dropped without an error
```

### QST-32 · int → a short string: overlen
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[OrderLine])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[OrderLine] ALTER COLUMN [LineNumber] NVARCHAR (5) NOT NULL;
                     GO
          base       OrderLine.LineNumber int NOT NULL
expect:   questions  HasRows(OrderLine) · Fits(OrderLine.LineNumber→nvarchar(5)) classes {overlen}
laws:     F4
source:   verify · whether a number too wide for the string raises (Msg 8115 for nvarchar, `*` for varchar) and so whether the class is overlen or overflow
```

### QST-33 · a collation-only change asks no Fits; the unique index rebuilt under it is probed under the new collation
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      PRINT N'Dropping Index [dbo].[T].[UX_T_Code]...';
                     GO
                     DROP INDEX [UX_T_Code]
                         ON [dbo].[T];
                     GO
                     PRINT N'Altering Table [dbo].[T]...';
                     GO
                     ALTER TABLE [dbo].[T] ALTER COLUMN [Code] NVARCHAR (50) COLLATE Latin1_General_CI_AS NOT NULL;
                     GO
                     ALTER TABLE [dbo].[T]
                         ADD [Note] NVARCHAR (20) NULL;
                     GO
                     PRINT N'Creating Index [dbo].[T].[UX_T_Code]...';
                     GO
                     CREATE UNIQUE NONCLUSTERED INDEX [UX_T_Code]
                         ON [dbo].[T]([Code] ASC);
                     GO
                     PRINT N'Creating Check Constraint [dbo].[CK_T_Amt]...';
                     GO
                     ALTER TABLE [dbo].[T] WITH NOCHECK
                         ADD CONSTRAINT [CK_T_Amt] CHECK ([Amt]>(0));
                     GO
                     PRINT N'Checking existing data against newly created constraints';
                     GO
                     USE [$(DatabaseName)];
                     GO
                     ALTER TABLE [dbo].[T] WITH CHECK CHECK CONSTRAINT [CK_T_Amt];
                     GO
          base       T.Code nvarchar(50) NOT NULL Latin1_General_CS_AS · T.Amt int NULL
expect:   questions  Unique(T,(Code)) under Latin1_General_CI_AS classes {exact, case, trailing}
                     · CheckFalse(T,CK_T_Amt) predicate ([Amt]>(0)) · measures Rows(T)
laws:     F4 F9 M1
source:   receipt · red-team #3 (collation change on a unique column ✓; the generated delta, no guard)
note:     The index is built after the ALTER, so duplicates are counted under CI_AS: a case twin that is clean under the
          environment's CS_AS today is a duplicate at deploy. DROP INDEX and the nullable ADD ask nothing.
```

### QST-34 · a varchar collation change across code pages asks roundtrip
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[T] ALTER COLUMN [Ref] VARCHAR (20) COLLATE Cyrillic_General_CI_AS NULL;
                     GO
          base       T.Ref varchar(20) NULL Latin1_General_CS_AS
expect:   questions  Fits(T.Ref→varchar(20) Cyrillic_General_CI_AS) classes {roundtrip} · measures Rows(T)
laws:     F4
source:   verify · whether SQL Server rewrites varchar data across code pages on ALTER COLUMN (an nvarchar collation change does not)
```

## Unique

### QST-35 · a plain unique index on a NOT NULL string under CI_AS: exact, case, trailing
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      PRINT N'Creating Index [dbo].[Product].[UX_Product_Code]...';
                     GO
                     CREATE UNIQUE NONCLUSTERED INDEX [UX_Product_Code]
                         ON [dbo].[Product]([Code] ASC);
                     GO
          base       Product.Code nvarchar(50) NOT NULL SQL_Latin1_General_CP1_CI_AS
expect:   questions  Unique(Product,(Code)) classes {exact, case, trailing} · measures Rows(Product)
laws:     F4 M1
source:   receipt · F10 (a duplicate → Msg 1505) · SamplePr "add-unique: duplicate values block the unique index, a unique column applies"
note:     trailing applies under every collation (equality ignores trailing spaces); case only under _CI; accent only
          under _AI; nulls only for a nullable key without a filter that excludes NULL.
```

### QST-36 · a nullable key without a filter adds the nulls class
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE UNIQUE NONCLUSTERED INDEX [UX_Customer_Email]
                         ON [dbo].[Customer]([Email] ASC);
                     GO
          base       Customer.Email nvarchar(256) NULL SQL_Latin1_General_CP1_CI_AS
expect:   questions  Unique(Customer,(Email)) classes {exact, case, trailing, nulls}
laws:     F4
source:   receipt · F10 (a second NULL → Msg 1505 "(<NULL>)": a unique index permits one NULL)
```

### QST-37 · a filtered index carries its filter; IS NOT NULL removes the nulls class
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE UNIQUE NONCLUSTERED INDEX [UX_Customer_Email]
                         ON [dbo].[Customer]([Email] ASC) WHERE ([Email] IS NOT NULL);
                     GO
          base       Customer.Email nvarchar(256) NULL SQL_Latin1_General_CP1_CI_AS
expect:   questions  Unique(Customer,(Email),[Email] IS NOT NULL) classes {exact, case, trailing}
laws:     F4
source:   verify · DacFx's spelling of the filter (copied into the coordinate, never re-parsed); the class rule is F10 ("unique among the filled values" is a filtered index)
```

### QST-38 · an accent-insensitive collation adds the accent class
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE UNIQUE NONCLUSTERED INDEX [UX_Account_Name]
                         ON [dbo].[Account]([Name] ASC);
                     GO
          base       Account.Name nvarchar(100) NOT NULL Latin1_General_CI_AI
expect:   questions  Unique(Account,(Name)) classes {exact, case, accent, trailing}
laws:     F4
source:   expected · ARCH §4 (duplicates under T's collation)
```

### QST-39 · a case-sensitive collation drops the case class
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE UNIQUE NONCLUSTERED INDEX [UX_T_Code]
                         ON [dbo].[T]([Code] ASC);
                     GO
          base       T.Code nvarchar(50) NOT NULL Latin1_General_CS_AS
expect:   questions  Unique(T,(Code)) classes {exact, trailing}
laws:     F4
source:   expected · ARCH §4
```

### QST-40 · a composite key of NOT NULL ints: exact only
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE UNIQUE NONCLUSTERED INDEX [UX_OrderLine_Order_Line]
                         ON [dbo].[OrderLine]([OrderId] ASC, [LineNumber] ASC);
                     GO
          base       OrderLine.OrderId int NOT NULL · OrderLine.LineNumber int NOT NULL
expect:   questions  Unique(OrderLine,(OrderId,LineNumber)) classes {exact}
laws:     F4
source:   expected · ARCH §4
note:     With a nullable member the nulls class returns: a unique index treats (1, NULL) twice as a duplicate.
```

### QST-41 · ADD CONSTRAINT … UNIQUE asks Unique
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Product]
                         ADD CONSTRAINT [UQ_Product_Code] UNIQUE NONCLUSTERED ([Code] ASC);
                     GO
          base       Product.Code nvarchar(50) NOT NULL SQL_Latin1_General_CP1_CI_AS
expect:   questions  Unique(Product,(Code)) classes {exact, case, trailing} · measures Rows(Product)
laws:     F4 M1
source:   expected · skills/op/add-unique (the constraint form)
```

### QST-42 · ADD CONSTRAINT … PRIMARY KEY asks Unique, without the nulls class
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[OrderLine]
                         ADD CONSTRAINT [PK_OrderLine] PRIMARY KEY CLUSTERED ([OrderId] ASC, [LineNumber] ASC);
                     GO
          base       OrderLine.OrderId int NOT NULL · OrderLine.LineNumber int NOT NULL
expect:   questions  Unique(OrderLine,(OrderId,LineNumber)) classes {exact} · measures Rows(OrderLine)
laws:     F4
source:   expected · skills/op/define-pk; SamplePr "define-pk: adding a primary key over unique non-NULL keys builds the clustered index clean" (clean leg)
note:     A key column that is nullable in base fails Msg 8111 whatever the data (a data-independent `blocked null`,
          CLS-40), so it asks nothing more; whether DacFx writes an ALTER COLUMN … NOT NULL first is open.
```

### QST-43 · INCLUDE columns and sort direction are not part of the key
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE UNIQUE NONCLUSTERED INDEX [UX_Product_Code]
                         ON [dbo].[Product]([Code] DESC)
                         INCLUDE([Name]);
                     GO
          base       Product.Code nvarchar(50) NOT NULL SQL_Latin1_General_CP1_CI_AS · Product.Name nvarchar(200) NULL
expect:   questions  Unique(Product,(Code)) classes {exact, case, trailing}
laws:     F4 D1
source:   expected · ARCH §4
```

### QST-44 · a non-unique index asks nothing, and is measured as a Scan
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      PRINT N'Creating Index [dbo].[Order].[IX_Order_StatusId]...';
                     GO
                     CREATE NONCLUSTERED INDEX [IX_Order_StatusId]
                         ON [dbo].[Order]([StatusId] ASC);
                     GO
          base       Order.StatusId int NOT NULL
expect:   questions  none · measures Rows(Order) for Scan(Order)
laws:     F4
source:   receipt · SamplePr "add-index: a non-unique index builds clean over a populated table"
note:     The build reads every row, but no value under the key limit can refuse it: ARCH §4's `Scan` row, a
          data-insensitive statement. It asks no question and keeps the tier at T0; its Rows feed the scale flag.
```

### QST-45 · a unique index on a table created in this delta is answered from the delta
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE TABLE [dbo].[CustomerPreference] (
                         [Id]         INT           IDENTITY (1, 1) NOT NULL,
                         [CustomerId] INT           NOT NULL,
                         [PrefKey]    NVARCHAR (50) NOT NULL,
                         CONSTRAINT [PK_CustomerPreference_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
                     );
                     GO
                     CREATE UNIQUE NONCLUSTERED INDEX [UX_CustomerPreference_Key]
                         ON [dbo].[CustomerPreference]([CustomerId] ASC, [PrefKey] ASC);
                     GO
expect:   questions  none · discharged {Unique(CustomerPreference,(CustomerId,PrefKey)): table created in this delta}
laws:     F4 S3
source:   expected · ARCH §4 Coordinates rule 1 (a new table is empty); asking would name an object the environment lacks (S3)
```

### QST-46 · a unique index over a column added in this delta is answered from the delta
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Customer]
                         ADD [ExternalRef] NVARCHAR (40) NULL;
                     GO
                     CREATE UNIQUE NONCLUSTERED INDEX [UX_Customer_ExternalRef]
                         ON [dbo].[Customer]([ExternalRef] ASC);
                     GO
expect:   questions  Unique(Customer,(ExternalRef)) classes {nulls}, answered from the delta by Rows(Customer) ≥ 2
                     · measures Rows(Customer)
laws:     F4 S3
source:   expected · ARCH §4 Coordinates rule 1 (a new column holds its default, or NULL, in every existing row)
note:     Every existing row holds NULL in the new column, so two rows are two NULLs and the build fails Msg 1505. The
          environment has no ExternalRef to probe (S3), so the answer is Rows(T), a count the grammar already has.
```

## KeyLength

### QST-47 · a nonclustered key whose maximum exceeds 1700 bytes asks KeyLength
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE NONCLUSTERED INDEX [IX_N_Big]
                         ON [dbo].[N]([Big] ASC);
                     GO
          base       N.Big nvarchar(1000) NULL
expect:   questions  KeyLength(N,IX_N_Big) · measures Rows(N)
laws:     F4 M1
source:   receipt · red-team #16 (Msg 1945 warns at create, 1946 fails on a longer row ✓)
note:     nvarchar(1000) is 2000 bytes. The widths come from `base`, or from the delta when the column is created or
          altered in it.
```

### QST-48 · the limit is exact: 1700 bytes asks nothing, 1702 asks
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE NONCLUSTERED INDEX [IX_N_A]
                         ON [dbo].[N]([A] ASC);
                     GO
                     CREATE NONCLUSTERED INDEX [IX_N_B]
                         ON [dbo].[N]([B] ASC);
                     GO
          base       N.A nvarchar(850) NULL · N.B nvarchar(851) NULL
expect:   questions  KeyLength(N,IX_N_B) · measures Rows(N)
laws:     F4
source:   expected · the documented 1700-byte nonclustered key limit
```

### QST-49 · a clustered key uses 900 bytes; key columns sum; INCLUDE does not count
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Doc]
                         ADD CONSTRAINT [PK_Doc] PRIMARY KEY CLUSTERED ([Path] ASC);
                     GO
                     CREATE UNIQUE NONCLUSTERED INDEX [UX_Doc_Area_Slug]
                         ON [dbo].[Doc]([Area] ASC, [Slug] ASC)
                         INCLUDE([Body]);
                     GO
          base       Doc.Path nvarchar(460) NOT NULL · Doc.Area nvarchar(400) NOT NULL · Doc.Slug nvarchar(460) NOT NULL
                     · Doc.Body nvarchar(max) NULL · all SQL_Latin1_General_CP1_CI_AS
expect:   questions  Unique(Doc,(Area,Slug)) classes {exact, case, trailing} · Unique(Doc,(Path)) classes {exact, case, trailing}
                     · KeyLength(Doc,PK_Doc) · KeyLength(Doc,UX_Doc_Area_Slug)
laws:     F4 D1
source:   expected · the documented limits (900 bytes clustered, 1700 nonclustered); Path is 920 bytes, Area + Slug 1720
```

## Orphan and CheckFalse

### QST-50 · an FK added WITH NOCHECK and validated WITH CHECK CHECK asks Orphan
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      PRINT N'Creating Foreign Key [dbo].[FK_Order_Customer]...';
                     GO
                     ALTER TABLE [dbo].[Order] WITH NOCHECK
                         ADD CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]);
                     GO
                     PRINT N'Checking existing data against newly created constraints';
                     GO
                     USE [$(DatabaseName)];
                     GO
                     ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Customer];
                     GO
expect:   questions  Orphan(Order.(CustomerId)→Customer) · measures Rows(Order)
laws:     F4 M1
source:   receipt · F9 (every declarative FK add is WITH NOCHECK ADD, then WITH CHECK CHECK) · TWIN_SUCCESSION §6b
note:     The pair is joined by constraint name across batches; the ADD gives the columns and the parent, the CHECK
          CHECK is the read.
```

### QST-51 · a composite FK; a NULL in any column passes
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Shipment] WITH NOCHECK
                         ADD CONSTRAINT [FK_Shipment_OrderLine] FOREIGN KEY ([OrderId], [LineNumber]) REFERENCES [dbo].[OrderLine] ([OrderId], [LineNumber]);
                     GO
                     ALTER TABLE [dbo].[Shipment] WITH CHECK CHECK CONSTRAINT [FK_Shipment_OrderLine];
                     GO
expect:   questions  Orphan(Shipment.(OrderId,LineNumber)→OrderLine)
laws:     F4
source:   expected · ARCH §4 (MATCH SIMPLE: the probe counts rows with every FK column non-NULL and no parent)
```

### QST-52 · a delete-rule change re-validates
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Order] DROP CONSTRAINT [FK_Order_Status];
                     GO
                     ALTER TABLE [dbo].[Order] WITH NOCHECK
                         ADD CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id]) ON DELETE CASCADE;
                     GO
                     ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Status];
                     GO
expect:   questions  Orphan(Order.(StatusId)→Status)
laws:     F4
source:   receipt · F9 change-delete-rule (DROP CONSTRAINT, WITH NOCHECK ADD … ON DELETE CASCADE, WITH CHECK CHECK; the CHECK CHECK re-scans the child rows)
```

### QST-53 · an environment holding the FK disabled gets a re-validating delta
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      PRINT N'Dropping Foreign Key [dbo].[FK_T_P]...';
                     GO
                     ALTER TABLE [dbo].[T] DROP CONSTRAINT [FK_T_P];
                     GO
                     PRINT N'Creating Foreign Key [dbo].[FK_T_P]...';
                     GO
                     ALTER TABLE [dbo].[T] WITH NOCHECK
                         ADD CONSTRAINT [FK_T_P] FOREIGN KEY ([PId]) REFERENCES [dbo].[P] ([Id]);
                     GO
                     PRINT N'Checking existing data against newly created constraints';
                     GO
                     USE [$(DatabaseName)];
                     GO
                     ALTER TABLE [dbo].[T] WITH CHECK CHECK CONSTRAINT [FK_T_P];
                     GO
expect:   questions  Orphan(T.(PId)→P) · measures Rows(T)
laws:     F4 F7 F9
source:   receipt · red-team #1 (a disabled FK ✓; the generated delta)
note:     The same head asks nothing of an environment holding FK_T_P enabled and trusted: the questions come
          from each environment's own delta (F7).
```

### QST-54 · a non-dbo parent and a bracketed child
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Order] WITH NOCHECK
                         ADD CONSTRAINT [FK_Order_RefStatus] FOREIGN KEY ([StatusId]) REFERENCES [ref].[Status] ([Id]);
                     GO
                     ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_RefStatus];
                     GO
expect:   questions  Orphan(Order.(StatusId)→ref.Status)
laws:     F4 D1
source:   expected · ARCH §2 row 9
```

### QST-55 · a WITH CHECK CHECK with no ADD in the delta stops
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Status];
                     GO
expect:   outcome    stop unknown-statement · "Delta line 1 reads rows, but no vocabulary row matches it" · exit 9
laws:     F4 H2
source:   verify · whether DacFx ever writes a bare CHECK CHECK (for example to re-trust a key an environment holds enabled but untrusted); if it does, a row is added that takes the kind, columns and parent from `base`
note:     Without the ADD, FK or CHECK is unknown; guessing is not allowed.
```

### QST-56 · a WITH NOCHECK ADD with no CHECK CHECK asks nothing
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Order] WITH NOCHECK
                         ADD CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id]);
                     GO
expect:   questions  none
laws:     F4
source:   verify · whether any effective option makes DacFx write this shape; if it can, the record states that the key lands untrusted (REC-15)
```

### QST-57 · CHECK CONSTRAINT without WITH CHECK enables without validating
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Order] CHECK CONSTRAINT [FK_Order_Status];
                     GO
                     ALTER TABLE [dbo].[Order] NOCHECK CONSTRAINT [FK_Order_Customer];
                     GO
expect:   questions  none
laws:     F4
source:   expected · neither statement reads a row (red-team c6: enable-without-validate leaves the key untrusted)
```

### QST-58 · an FK on a table created in this delta is discharged
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      PRINT N'Creating Table [dbo].[CustomerPreference]...';
                     GO
                     CREATE TABLE [dbo].[CustomerPreference] (
                         [Id]         INT           IDENTITY (1, 1) NOT NULL,
                         [CustomerId] INT           NOT NULL,
                         [PrefKey]    NVARCHAR (50) NOT NULL,
                         CONSTRAINT [PK_CustomerPreference_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
                     );
                     GO
                     ALTER TABLE [dbo].[CustomerPreference] WITH NOCHECK
                         ADD CONSTRAINT [FK_CustomerPreference_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]);
                     GO
                     ALTER TABLE [dbo].[CustomerPreference] WITH CHECK CHECK CONSTRAINT [FK_CustomerPreference_Customer];
                     GO
expect:   questions  none · discharged {Orphan(CustomerPreference.(CustomerId)→Customer): table created in this delta}
laws:     F4 S3
source:   expected · SamplePr "create-entity: a new table with an identity PK and an FK to Customer applies clean"; the NOCHECK + CHECK CHECK on the new table is quoted in sample-prs/create-entity.md
note:     A changed post-deploy seed that fills the new table is asked as SeedDrift or Authored (QST-80, QST-59), not
          here.
```

### QST-59 · an FK to a parent created in this delta and filled by the post-deploy seed becomes Authored
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE TABLE [dbo].[OrderType] (
                         [Id]   INT           NOT NULL,
                         [Code] NVARCHAR (20) NOT NULL,
                         CONSTRAINT [PK_OrderType_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
                     );
                     GO
                     ALTER TABLE [dbo].[Order] WITH NOCHECK
                         ADD CONSTRAINT [FK_Order_OrderType] FOREIGN KEY ([OrderTypeId]) REFERENCES [dbo].[OrderType] ([Id]);
                     GO
                     MERGE dbo.OrderType AS target USING (VALUES (1, N'Web'), (2, N'Store')) AS source (Id, Code) ON target.Id = source.Id
                     WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Code) VALUES (source.Id, source.Code);
                     GO
                     PRINT N'Checking existing data against newly created constraints';
                     GO
                     ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_OrderType];
                     GO
          base       Order.OrderTypeId int NULL
          scripts    Data/Seed.sql at head holds the MERGE above; at the merge-base it does not
expect:   questions  Authored(post:Data/Seed.sql:1, {Order.OrderTypeId, OrderType.Id, OrderType.Code}), standing for
                     Orphan(Order.(OrderTypeId)→OrderType) · measures Rows(Order)
laws:     F4 S3
source:   verify · ARCH §4 Coordinates rule 1 rests on the order observed 2026-10-18 on sqlpackage 170.5.96 (the
          post-deploy text precedes "Checking existing data", so a seeded lookup counts); INV-H6-01 pins it per version
note:     The environment has no OrderType to probe (S3), and which child values pass depends on authored rows, so the
          Orphan question becomes Authored: the agent declares which OrderTypeId shapes the seed covers.
```

### QST-60 · a CHECK added WITH NOCHECK and validated asks CheckFalse with its predicate
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Order] WITH NOCHECK
                         ADD CONSTRAINT [CK_Order_Total] CHECK ([Total]>(0));
                     GO
                     PRINT N'Checking existing data against newly created constraints';
                     GO
                     ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [CK_Order_Total];
                     GO
expect:   questions  CheckFalse(Order,CK_Order_Total) predicate ([Total]>(0)) · measures Rows(Order)
laws:     F4 M1
source:   receipt · F10 add-check (WITH NOCHECK ADD + WITH CHECK CHECK; a violating row → Msg 547)
note:     The predicate is free-form, so the witness is agent-written (stop check-witness, PLT cases). The question
          carries the predicate exactly as DacFx wrote it.
```

### QST-61 · one validation section with an FK and a CHECK asks both, in canonical order
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Order] WITH NOCHECK
                         ADD CONSTRAINT [CK_Order_Total] CHECK ([Total]>(0));
                     GO
                     ALTER TABLE [dbo].[Order] WITH NOCHECK
                         ADD CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id]);
                     GO
                     PRINT N'Checking existing data against newly created constraints';
                     GO
                     ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [CK_Order_Total];
                     ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Status];
                     GO
expect:   questions  Orphan(Order.(StatusId)→Status) · CheckFalse(Order,CK_Order_Total) predicate ([Total]>(0))
laws:     F4 D1
source:   expected · ARCH §4 row order; F9/F10 shapes
```

### QST-62 · a CHECK over a column added in this delta: a nullable column is discharged, a defaulted one is HasRows
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Product]
                         ADD [Qty]  INT NULL,
                             [Rank] INT CONSTRAINT [DF_Product_Rank] DEFAULT (0) NOT NULL;
                     GO
                     ALTER TABLE [dbo].[Product] WITH NOCHECK
                         ADD CONSTRAINT [CK_Product_Qty] CHECK ([Qty]>(0));
                     GO
                     ALTER TABLE [dbo].[Product] WITH NOCHECK
                         ADD CONSTRAINT [CK_Product_Rank] CHECK ([Rank]>(0));
                     GO
                     ALTER TABLE [dbo].[Product] WITH CHECK CHECK CONSTRAINT [CK_Product_Qty];
                     ALTER TABLE [dbo].[Product] WITH CHECK CHECK CONSTRAINT [CK_Product_Rank];
                     GO
expect:   questions  HasRows(Product) · discharged {CheckFalse(Product,CK_Product_Qty): every existing Qty is NULL, the
                     predicate is UNKNOWN and passes} · measures Rows(Product)
laws:     F4 S3
source:   expected · ARCH §4 Coordinates rule 1 (a new column holds its default, or NULL, in every existing row)
note:     Every existing row receives Rank = 0, so CK_Product_Rank is decided by the default alone: a presence row on the
          copy meets it exactly as every environment row would. The environment has no Rank column to probe (S3).
```

## Rebuild and Computed

### QST-63 · the tmp_ms_xx rebuild: its inner IF EXISTS is not a guard
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      PRINT N'Dropping Foreign Key [dbo].[FK_T_P]...';
                     GO
                     ALTER TABLE [dbo].[T] DROP CONSTRAINT [FK_T_P];
                     GO
                     PRINT N'Starting rebuilding table [dbo].[T]...';
                     GO
                     BEGIN TRANSACTION;
                     SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
                     SET XACT_ABORT ON;
                     CREATE TABLE [dbo].[tmp_ms_xx_T] (
                         [Id]     INT           NOT NULL,
                         [Ref]    VARCHAR (20)  NULL,
                         [Code]   NVARCHAR (50) NOT NULL,
                         [PId]    INT           NULL,
                         [Amt]    INT           NULL,
                         [RefNum] AS            (CONVERT (INT, [Ref])) PERSISTED,
                         CONSTRAINT [tmp_ms_xx_constraint_PK_T1] PRIMARY KEY CLUSTERED ([Id] ASC)
                     );
                     IF EXISTS (SELECT TOP 1 1 
                                FROM   [dbo].[T])
                         BEGIN
                             INSERT INTO [dbo].[tmp_ms_xx_T] ([Id], [Code], [Ref], [PId], [Amt])
                             SELECT   [Id],
                                      [Code],
                                      [Ref],
                                      [PId],
                                      [Amt]
                             FROM     [dbo].[T]
                             ORDER BY [Id] ASC;
                         END
                     DROP TABLE [dbo].[T];
                     EXECUTE sp_rename N'[dbo].[tmp_ms_xx_T]', N'T';
                     EXECUTE sp_rename N'[dbo].[tmp_ms_xx_constraint_PK_T1]', N'PK_T', N'OBJECT';
                     COMMIT TRANSACTION;
                     SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
                     GO
                     PRINT N'Creating Index [dbo].[T].[UX_T_Code]...';
                     GO
                     CREATE UNIQUE NONCLUSTERED INDEX [UX_T_Code]
                         ON [dbo].[T]([Code] ASC);
                     GO
                     ALTER TABLE [dbo].[T] WITH NOCHECK
                         ADD CONSTRAINT [FK_T_P] FOREIGN KEY ([PId]) REFERENCES [dbo].[P] ([Id]);
                     GO
                     ALTER TABLE [dbo].[T] WITH CHECK CHECK CONSTRAINT [FK_T_P];
                     GO
          base       T.Id int NOT NULL · T.Code nvarchar(50) NOT NULL Latin1_General_CS_AS · T.Ref varchar(20) NULL · T.PId int NULL · T.Amt int NULL
expect:   questions  HasRows(T) · Unique(T,(Code)) classes {exact, trailing} · Orphan(T.(PId)→P) · Rebuild(T)
                     · Computed(T.RefNum) expression (CONVERT (INT, [Ref])) · measures Rows(T)
laws:     F4 M1 H5
source:   receipt · red-team #3 (the tmp_ms_xx rebuild ✓ and the PERSISTED computed column ✓, with no data-loss guard; the generated delta)
note:     The inner IF EXISTS guards the INSERT, not the publish: it raises nothing and is part of Rebuild. DROP TABLE and
          both sp_renames are the rebuild's own steps. The index and the FK are re-created over the moved rows, so they
          are asked again. No moved column changes type, so the rebuild adds no Fits.
```

### QST-64 · an identity rebuild asks the gapped class
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      PRINT N'Starting rebuilding table [dbo].[Category]...';
                     GO
                     BEGIN TRANSACTION;
                     SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
                     SET XACT_ABORT ON;
                     CREATE TABLE [dbo].[tmp_ms_xx_Category] (
                         [Id]       INT           IDENTITY (1, 1) NOT NULL,
                         [Code]     NVARCHAR (30) NOT NULL,
                         [IsActive] BIT           NOT NULL,
                         CONSTRAINT [tmp_ms_xx_constraint_PK_Category_Id1] PRIMARY KEY CLUSTERED ([Id] ASC)
                     );
                     IF EXISTS (SELECT TOP 1 1 
                                FROM   [dbo].[Category])
                         BEGIN
                             SET IDENTITY_INSERT [dbo].[tmp_ms_xx_Category] ON;
                             INSERT INTO [dbo].[tmp_ms_xx_Category] ([Id], [Code], [IsActive])
                             SELECT   [Id],
                                      [Code],
                                      [IsActive]
                             FROM     [dbo].[Category]
                             ORDER BY [Id] ASC;
                             SET IDENTITY_INSERT [dbo].[tmp_ms_xx_Category] OFF;
                         END
                     DROP TABLE [dbo].[Category];
                     EXECUTE sp_rename N'[dbo].[tmp_ms_xx_Category]', N'Category';
                     EXECUTE sp_rename N'[dbo].[tmp_ms_xx_constraint_PK_Category_Id1]', N'PK_Category_Id', N'OBJECT';
                     COMMIT TRANSACTION;
                     SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
                     GO
expect:   questions  HasRows(Category) · Rebuild(Category) classes {gapped}
laws:     F4
source:   receipt · F12 identity-swap (CREATE tmp_ms_xx_Category with IDENTITY(1,1), SET IDENTITY_INSERT copy preserving ids, DROP TABLE, sp_rename; not blocked by the guard)
note:     F12 quotes the statement sequence; the batch layout follows the red-team rebuild (QST-63). Gapped asks
          whether ids are gapped or do not start at 1, which the reseed carries forward.
```

### QST-65 · a rebuild that moves a retyped column asks Fits for that column
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      BEGIN TRANSACTION;
                     SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
                     SET XACT_ABORT ON;
                     CREATE TABLE [dbo].[tmp_ms_xx_T] (
                         [Id]   INT           NOT NULL,
                         [Ref]  VARCHAR (20)  NULL,
                         [Code] NVARCHAR (10) NOT NULL,
                         CONSTRAINT [tmp_ms_xx_constraint_PK_T1] PRIMARY KEY CLUSTERED ([Id] ASC)
                     );
                     IF EXISTS (SELECT TOP 1 1 
                                FROM   [dbo].[T])
                         BEGIN
                             INSERT INTO [dbo].[tmp_ms_xx_T] ([Id], [Code], [Ref])
                             SELECT   [Id],
                                      [Code],
                                      [Ref]
                             FROM     [dbo].[T]
                             ORDER BY [Id] ASC;
                         END
                     DROP TABLE [dbo].[T];
                     EXECUTE sp_rename N'[dbo].[tmp_ms_xx_T]', N'T';
                     EXECUTE sp_rename N'[dbo].[tmp_ms_xx_constraint_PK_T1]', N'PK_T', N'OBJECT';
                     COMMIT TRANSACTION;
                     SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
                     GO
          base       T.Code nvarchar(50) NOT NULL · T.Ref varchar(20) NULL
expect:   questions  HasRows(T) · Fits(T.Code→nvarchar(10)) classes {overlen, overbytes} · Rebuild(T)
laws:     F4
source:   verify · whether DacFx rebuilds (rather than ALTERs) a column narrowed together with a reorder, whether it CASTs in the SELECT, and whether a guard then precedes it
```

### QST-66 · ADD … AS (…) PERSISTED on an existing table asks Computed, unmeasured
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      PRINT N'Altering Table [dbo].[T]...';
                     GO
                     ALTER TABLE [dbo].[T]
                         ADD [RefNum] AS (CONVERT (INT, [Ref])) PERSISTED;
                     GO
          base       T.Ref varchar(20) NULL
expect:   questions  Computed(T.RefNum) expression (CONVERT (INT, [Ref])) unmeasured · measures Rows(T)
laws:     F4 M1
source:   receipt · red-team #3 (PERSISTED computed column ✓: data-reading DDL with no guard; the generated delta)
note:     No guard precedes it: computing over every existing row is the read. A non-numeric Ref fails it, and `''`
          computes to 0 silently (red-team t4/t5). The environment is never asked: the witness is agent-declared, and
          the record carries declared-not-measured (ARCH §4).
```

### QST-67 · a non-persisted computed column asks nothing
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[T]
                         ADD [RefNum] AS (CONVERT (INT, [Ref]));
                     GO
expect:   questions  none
laws:     F4
source:   expected · a non-persisted column is computed on read, never at deploy
```

### QST-68 · an index over a non-persisted computed column asks Computed
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE NONCLUSTERED INDEX [IX_T_RefNum]
                         ON [dbo].[T]([RefNum] ASC);
                     GO
          base       T.RefNum computed (CONVERT (INT, [Ref])), not persisted · T.Ref varchar(20) NULL
expect:   questions  Computed(T.RefNum) expression (CONVERT (INT, [Ref])) · measures Rows(T)
laws:     F4
source:   verify · building the index evaluates the expression for every row; ARCH §4's Computed row names only ADD … PERSISTED (reported)
```

## Statements that read no rows

### QST-69 · additive DDL asks nothing
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE SCHEMA [archive]
                         AUTHORIZATION [dbo];
                     GO
                     CREATE TABLE [archive].[OrderArchive] (
                         [Id]         INT NOT NULL,
                         [CustomerId] INT NOT NULL,
                         CONSTRAINT [PK_OrderArchive_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
                     );
                     GO
                     ALTER TABLE [dbo].[Customer]
                         ADD [MiddleName] NVARCHAR (100) NULL;
                     GO
                     ALTER TABLE [dbo].[Order]
                         ADD CONSTRAINT [DF_Order_PlacedBy] DEFAULT (N'web') FOR [PlacedBy];
                     GO
                     CREATE VIEW [dbo].[vOrderOpen]
                     AS SELECT Id, CustomerId FROM dbo.[Order] WHERE StatusId = 1;
                     GO
                     CREATE PROCEDURE [dbo].[pOrderCount]
                     AS SELECT COUNT(*) FROM dbo.[Order];
                     GO
                     GRANT SELECT ON OBJECT::[dbo].[vOrderOpen] TO [app_reader];
                     GO
                     EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Open orders', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'vOrderOpen';
                     GO
expect:   questions  none
laws:     F4
source:   expected · SamplePr "add-optional: a new nullable column applies clean on a populated table" and SamplePr "create-entity: a new table with an identity PK and an FK to Customer applies clean" back two of these shapes; the rest are recognized non-reading shapes
note:     A view or procedure body is never executed at deploy, so its SELECT reads nothing.
```

### QST-70 · ADD … NOT NULL with a DEFAULT asks nothing: a Scan that backfills
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Customer] ADD [Segment] NVARCHAR (20) CONSTRAINT [DF_Customer_Segment] DEFAULT (N'Standard') NOT NULL;
                     GO
expect:   questions  none · measures Rows(Customer) for Scan(Customer)
laws:     F4
source:   receipt · F14 (a defaulted NOT NULL column on a populated table published clean; the default stamped every existing row) · SamplePr "add-default: a NOT NULL column with a default applies clean and backfills existing rows"
note:     Every existing row receives the constant and no existing value is read or can refuse it: ARCH §4's `Scan` row
          (the backfill). The write is a volume cost, so Rows(Customer) feeds the scale flag (TIER-22). A CHECK on the
          new column is QST-62.
```

### QST-71 · a refactorlog rename and its __RefactorLog bookkeeping ask nothing
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      PRINT N'The following operation was generated from a refactoring log file 3f2a1c9e-1111-4a2b-9c3d-0123456789ab';
                     PRINT N'Rename [dbo].[Customer].[ContactPhone] to MobileNumber';
                     GO
                     EXECUTE sp_rename @objname = N'[dbo].[Customer].[ContactPhone]', @newname = N'MobileNumber', @objtype = N'COLUMN';
                     GO
                     IF OBJECT_ID(N'dbo.__RefactorLog') IS NULL
                     BEGIN
                         CREATE TABLE [dbo].[__RefactorLog] (OperationKey UNIQUEIDENTIFIER NOT NULL PRIMARY KEY)
                         EXEC sp_addextendedproperty N'microsoft_database_tools_support', N'refactoring log', N'schema', N'dbo', N'table', N'__RefactorLog'
                     END
                     GO
                     IF NOT EXISTS (SELECT OperationKey FROM [dbo].[__RefactorLog] WHERE OperationKey = '3f2a1c9e-1111-4a2b-9c3d-0123456789ab')
                     INSERT INTO [dbo].[__RefactorLog] (OperationKey) values ('3f2a1c9e-1111-4a2b-9c3d-0123456789ab')
                     GO
expect:   questions  none
laws:     F4
source:   verify · the sp_rename line is quoted live in sample-prs/rename-attribute.md (170.4.83.3); the __RefactorLog batches were seen in a scratch run
note:     The rename is an effect (REC-12), not a question. The INSERT reads only DacFx's own ledger; it is a recognized
          shape, so QST-87's rule does not stop it.
```

### QST-72 · a schema move with its refactorlog entry asks nothing
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER SCHEMA [archive] TRANSFER [dbo].[Category];
                     GO
expect:   questions  none
laws:     F4
source:   verify · the exact statement DacFx writes for a refactorlog schema move (sample-prs/move-schema.md proved the T-SQL by hand, not a generated delta)
```

### QST-73 · DROP COLUMN asks only through its guard
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      /*
                     The column [dbo].[Product].[LegacyCode] is being dropped, data loss could occur.
                     */
                     IF EXISTS (select top 1 1 from [dbo].[Product])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     PRINT N'Dropping Default Constraint [dbo].[DF_Product_LegacyCode]...';
                     GO
                     ALTER TABLE [dbo].[Product] DROP CONSTRAINT [DF_Product_LegacyCode];
                     GO
                     ALTER TABLE [dbo].[Product] DROP COLUMN [LegacyCode];
                     GO
expect:   questions  HasRows(Product)
laws:     F4
source:   receipt · SamplePr "delete-attribute: removing a populated column is BLOCKED by the data-loss guard; the scripted DROP COLUMN is the irreversible step"
```

### QST-74 · DROP TABLE asks only through its guard
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      /*
                     The table [dbo].[OrderLine] is being dropped, data loss could occur.
                     */
                     IF EXISTS (select top 1 1 from [dbo].[OrderLine])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     PRINT N'Dropping Table [dbo].[OrderLine]...';
                     GO
                     DROP TABLE [dbo].[OrderLine];
                     GO
expect:   questions  HasRows(OrderLine)
laws:     F4
source:   expected · sample-prs/delete-entity.md quotes this guarded drop from a live run (DropObjectsNotInSource = true)
```

### QST-75 · a rename without a refactorlog entry is DROP + ADD and asks HasRows only
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      /*
                     The column [dbo].[Customer].[ContactPhone] is being dropped, data loss could occur.
                     */
                     IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Customer] DROP COLUMN [ContactPhone];
                     GO
                     ALTER TABLE [dbo].[Customer]
                         ADD [MobileNumber] NVARCHAR (40) NULL;
                     GO
expect:   questions  HasRows(Customer)
laws:     F4 D1
source:   receipt · SamplePr "rename-attribute: a bare column rename (no refactorlog) is a DROP+ADD blocked by the data-loss guard; EXEC sp_rename preserves every value"
note:     Get-FxQuestions never infers a rename from names; TIER-06 and REC-13 follow the statements.
```

### QST-76 · one guard over drops, a narrowing and a re-created unique index
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      /*
                     The column [dbo].[T].[Note] is being dropped, data loss could occur.
                     The type for column Code in table [dbo].[T] is currently  NVARCHAR (50) COLLATE Latin1_General_CI_AS NOT NULL but is being changed to  NVARCHAR (40) NOT NULL. Data loss could occur and deployment may fail if the column contains data that is incompatible with type  NVARCHAR (40) NOT NULL.
                     */
                     IF EXISTS (select top 1 1 from [dbo].[T])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     PRINT N'Dropping Index [dbo].[T].[UX_T_Code]...';
                     GO
                     DROP INDEX [UX_T_Code]
                         ON [dbo].[T];
                     GO
                     PRINT N'Dropping Check Constraint [dbo].[CK_T_Amt]...';
                     GO
                     ALTER TABLE [dbo].[T] DROP CONSTRAINT [CK_T_Amt];
                     GO
                     PRINT N'Altering Table [dbo].[T]...';
                     GO
                     ALTER TABLE [dbo].[T] DROP COLUMN [Note];
                     GO
                     ALTER TABLE [dbo].[T] ALTER COLUMN [Code] NVARCHAR (40) NOT NULL;
                     GO
                     PRINT N'Creating Index [dbo].[T].[UX_T_Code]...';
                     GO
                     CREATE UNIQUE NONCLUSTERED INDEX [UX_T_Code]
                         ON [dbo].[T]([Code] ASC);
                     GO
          base       T.Code nvarchar(50) NOT NULL Latin1_General_CI_AS · T.Note nvarchar(20) NULL · database collation Latin1_General_CS_AS
expect:   questions  HasRows(T) · Fits(T.Code→nvarchar(40)) classes {overlen, overbytes} · Unique(T,(Code)) under Latin1_General_CS_AS classes {exact, trailing}
                     · measures Rows(T)
laws:     F4 M1
source:   expected · the red team's generated delta_narrow.sql (sqlpackage 170.5.96; Appendix A ties no finding number to it); the overbytes class is red-team #10 (a narrowing silently trims trailing spaces ✓)
note:     The ALTER names no COLLATE, so Code returns to the database collation (CS_AS) and the re-created index is
          probed case-sensitively. DROP INDEX, DROP CONSTRAINT and DROP COLUMN ask nothing.
```

## Batches and authored scripts

### QST-77 · GO separates only when it stands alone on its line
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      PRINT N'GO';
                     GO
                     ALTER TABLE [dbo].[Customer]
                         ADD [GoLiveDate] DATE NULL;
                     go
                     CREATE NONCLUSTERED INDEX [IX_Customer_GoLiveDate]
                         ON [dbo].[Customer]([GoLiveDate] ASC);
                     GO
expect:   questions  none · measures Rows(Customer) for Scan(Customer)
laws:     F4 D1
source:   expected · sqlcmd's separator rule (GO alone on a line, any case, trailing spaces allowed); GO inside a string or an identifier separates nothing
```

### QST-78 · a changed pre-deploy statement asks Authored, located by its text
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      GO
                     USE [$(DatabaseName)];
                     GO
                     /*
                      Pre-deploy: backfill blank emails.
                     */
                     UPDATE [dbo].[Customer] SET [Email] = N'unknown@example.invalid' WHERE [Email] IS NULL;
                     GO
                     PRINT 'Pre-deploy complete.';
                     GO
                     GO
                     IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
                     GO
          scripts    Script.PreDeployment.sql at head: lines 1–3 the comment, line 4 the UPDATE, then GO and the PRINT;
                     at the merge-base: the comment, GO and the PRINT
          base       Customer.Email nvarchar(256) NULL
expect:   questions  HasRows(Customer) · NotNull(Customer.Email) · Authored(pre:Script.PreDeployment.sql:4, {Customer.Email})
laws:     F4 H5
source:   verify · sqlpackage embeds the pre-deploy text verbatim after USE [$(DatabaseName)], with no marker (scratch run), so the region is found by matching the head file's text; "changed" is against the merge-base's file (ARCH §4 Authored row)
note:     The columns are the identifiers in the statement that resolve against the head schema (CLM-safe matching,
          ARCH §11.8). Only the statement absent from the merge-base file asks.
```

### QST-79 · an unchanged post-deploy seed asks nothing, though its MERGE reads the table
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      SET NOCOUNT ON;
                     GO
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
          scripts    Data/Seed.sql identical at head and at the merge-base (included by Script.PostDeployment.sql's `:r .\Data\Seed.sql`)
expect:   questions  none
laws:     F4 H2
source:   expected · ARCH §4 (Authored and SeedDrift only for a statement not in the merge-base); the MERGE text is the proving ground's seed as the Script embeds it (scratch run)
note:     An unchanged statement has already run in every environment. The copy still runs it in the publish, so a
          failure it meets after a schema change is observed (Msg 544: blocked authored, CLS-27).
```

### QST-80 · a changed MERGE seed asks SeedDrift
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      MERGE dbo.Status AS target
                     USING (VALUES
                         (1, N'Pending',   1),
                         (2, N'Shipped',   1),
                         (3, N'Cancelled', 1),
                         (4, N'Refunded',  1)
                     ) AS source (Id, Code, IsActive)
                         ON target.Id = source.Id
                     WHEN NOT MATCHED BY TARGET THEN
                         INSERT (Id, Code, IsActive) VALUES (source.Id, source.Code, source.IsActive);
                     GO
          scripts    Data/Seed.sql at head holds the (4, N'Refunded', 1) row; at the merge-base it does not
expect:   questions  SeedDrift(Status) key Id, code Code, from post:Data/Seed.sql:1 · measures Rows(Status)
laws:     F4
source:   verify · a `:r` include is expanded in the Script (scratch run), so the changed file is the included one; the kind is ARCH §4's SeedDrift row (an Authored subset); seed ops always look (TWIN_SUCCESSION §6b: Id/Code per environment, red-team #15)
note:     The count sent to the environment carries the seed's own VALUES (repository values) and returns one integer:
          the rows whose (Id, Code) pair disagrees with them. The tier is T3 (TIER-04).
```

### QST-81 · a counterfeit guard in a changed pre-deploy is Authored, never HasRows
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      GO
                     USE [$(DatabaseName)];
                     GO
                     IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     PRINT 'Pre-deploy complete.';
                     GO
                     GO
                     PRINT N'Creating Foreign Key [dbo].[FK_Order_Customer]...';
                     GO
          scripts    Script.PreDeployment.sql at head: the IF EXISTS / RAISERROR (lines 1–2), GO, the PRINT; at base: the PRINT only
expect:   questions  Authored(pre:Script.PreDeployment.sql:1, {Customer})
laws:     H5 F4
source:   expected · red-team #12 (Msg 50000 state 127 can be counterfeited by a user RAISERROR)
note:     The set of guard batches Get-FxOutcome uses for H5 excludes every authored region, so this batch can never be
          attributed to the guard (CLS-06).
```

### QST-82 · the R1 of a two-release: an authored ALTER … NOT NULL asks Authored only
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      GO
                     USE [$(DatabaseName)];
                     GO
                     IF EXISTS (SELECT 1 FROM dbo.Customer WHERE Email IS NULL)
                         UPDATE dbo.Customer
                             SET Email = N'unknown+' + CAST(Id AS NVARCHAR(20)) + N'@example.invalid'
                             WHERE Email IS NULL;
                     GO
                     IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Customer')
                                AND name = 'Email' AND is_nullable = 1)
                         ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(256) NOT NULL;
                     GO
                     PRINT N'Update complete.';
                     GO
          scripts    Script.PreDeployment.sql at head: the two statements (lines 1 and 6); at base: empty
expect:   questions  Authored(pre:Script.PreDeployment.sql:1, {Customer.Email}) · Authored(pre:Script.PreDeployment.sql:6, {Customer.Email})
laws:     F4 F8
source:   expected · F4/F7 (R1: model unchanged, a pre-deploy backfills and tightens); the statements are proving-ground/Script.PreDeployment.sql's worked example
note:     Authored text is never matched against the vocabulary: its ALTER raises no NotNull. The NotNull it enforces
          comes from the final target (QST-83, F8).
```

### QST-83 · a program asks the final target's questions plus every release's authored ones
```case
kind:     pure
action:   Get-FxQuestions (program R1 R2)
input:    delta      R1: QST-82's delta (its model diff is empty)
                     R2 target: QST-16's delta (head R2 scripted against the environment's schema)
          scripts    as QST-82 for R1; R2 has no pre-deploy
          base       Customer.Email nvarchar(256) NULL
expect:   questions  HasRows(Customer) · NotNull(Customer.Email) · Authored(pre:Script.PreDeployment.sql:1, {Customer.Email})
                     · Authored(pre:Script.PreDeployment.sql:6, {Customer.Email})
laws:     F8 F4
source:   expected · F8; FINDINGS F7 (the two-release R1/R2 shape)
note:     Without F8, R1 would plant nothing for the NULLs its own ALTER must meet.
```

## Unknown statements

### QST-84 · ALTER TABLE … SWITCH stops
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      PRINT N'Switching [dbo].[Order]...';
                     GO
                     ALTER TABLE [dbo].[Order] SWITCH TO [archive].[Order];
                     GO
expect:   outcome    stop unknown-statement · "Delta line 3 reads rows, but no vocabulary row matches it" · exit 9
laws:     F4 H2 H3
source:   expected · a synthetic unknown (ARCH §4 Unknown row)
```

### QST-85 · CREATE FULLTEXT INDEX stops
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE FULLTEXT INDEX ON [dbo].[Product]
                         ([Name] LANGUAGE 1033)
                         KEY INDEX [PK_Product_Id]
                         ON [ftCatalog];
                     GO
expect:   outcome    stop unknown-statement · "Delta line 1 reads rows, but no vocabulary row matches it" · exit 9
laws:     F4 H2
source:   expected · population reads every row; whether LocalDB can host it at all is CLS-49
```

### QST-86 · UPDATE STATISTICS stops
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      UPDATE STATISTICS [dbo].[Customer];
                     GO
expect:   outcome    stop unknown-statement · "Delta line 1 reads rows, but no vocabulary row matches it" · exit 9
laws:     F4 H2
source:   expected · decided: it reads rows and DacFx is not known to write it, so its presence is unexplained and the principal decides
```

### QST-87 · a data-motion INSERT … SELECT outside a rebuild stops
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      INSERT INTO [archive].[OrderArchive] ([Id], [CustomerId])
                     SELECT [Id], [CustomerId]
                     FROM   [dbo].[Order];
                     GO
expect:   outcome    stop unknown-statement · "Delta line 1 reads rows, but no vocabulary row matches it" · exit 9
laws:     F4 H2
source:   expected · only the INSERT into the same transaction's tmp_ms_xx_ table (QST-63) and the __RefactorLog ledger (QST-71) are recognized
```

### QST-88 · ALTER INDEX … REBUILD stops
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER INDEX [IX_Order_StatusId]
                         ON [dbo].[Order] REBUILD WITH(DATA_COMPRESSION = PAGE);
                     GO
expect:   outcome    stop unknown-statement · "Delta line 1 reads rows, but no vocabulary row matches it" · exit 9
laws:     F4
source:   verify · whether DacFx writes this shape for an index option change; if it does, it enters as a no-question row with its case (E1)
```

### QST-89 · turning SYSTEM_VERSIONING on over a history table that holds rows asks Temporal
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Customer]
                         SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE=[dbo].[CustomerHistory], DATA_CONSISTENCY_CHECK=ON));
                     GO
          base       Customer PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo), versioning off · CustomerHistory a table in
                     the catalog
expect:   questions  Temporal(Customer) history CustomerHistory · measures Rows(Customer), Rows(CustomerHistory)
laws:     F4
source:   verify · the exact spelling DacFx writes; the kind is ARCH §4's Temporal row. The engine side passed on the Twin: SamplePr "temporal-convert: turning the existing populated Customer system-versioned — the true outcome under the production gate, existing rows preserved"
note:     The count is the history rows whose period end is not after its start, or that overlap. A history table this
          delta creates is empty (coordinate rule 1), and the question is then discharged.
```

### QST-90 · a smart-default backfill (GenerateSmartDefaults in the effective options) stops
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      UPDATE [dbo].[Customer]
                     SET    [Email] = N''
                     WHERE  [Email] IS NULL;
                     GO
                     ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
                     GO
          base       Customer.Email nvarchar(256) NULL
expect:   outcome    stop unknown-statement · "Delta line 1 reads rows, but no vocabulary row matches it" · exit 9
laws:     F4 F6
source:   verify · whether and how DacFx writes the smart-default backfill; F6 decides whether the effective options can carry GenerateSmartDefaults = True at all
```

### QST-91 · several unknowns: the stop names the first line
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Order] SWITCH TO [archive].[Order];
                     GO
                     UPDATE STATISTICS [dbo].[Customer];
                     GO
expect:   outcome    stop unknown-statement · "Delta line 1 reads rows, but no vocabulary row matches it" · exit 9
laws:     H3 D1 H1
source:   expected · one outcome per run (H1); the first line makes the stop deterministic
```

### QST-92 · any other procedure call stops; the recognized ones do not
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      EXECUTE sp_refreshsqlmodule N'[dbo].[vCustomer]';
                     GO
                     EXECUTE sp_rename @objname = N'[dbo].[Customer].[ContactPhone]', @newname = N'MobileNumber', @objtype = N'COLUMN';
                     GO
                     EXECUTE [dbo].[pReconcileOrders];
                     GO
expect:   outcome    stop unknown-statement · "Delta line 5 reads rows, but no vocabulary row matches it" · exit 9
laws:     F4 H2
source:   expected · sp_refreshsqlmodule is a receipt shape (QST-02); a procedure body can read anything
```

## Properties

### QST-93 · F4 over every generated delta in the corpus
```case
kind:     pure
action:   Get-FxQuestions over every cases/fixtures/*/delta.*.sql the corpus run wrote, and over the red-team deltas (delta1, delta3, delta_narrow, delta_ico_False, delta_ico_True)
expect:   questions  every batch is classified as exactly one of: data-sensitive (one vocabulary row), data-insensitive
                     (Scan), non-reading, an authored region, or the unknown stop with its line; no batch is unclassified
laws:     F4 E1
source:   expected · INVARIANTS F4 property
```

### QST-94 · F4: a synthetic row-reading statement in the main region always stops
```case
kind:     pure
action:   Get-FxQuestions over generated one-batch deltas
input:    delta      one batch each: UPDATE, DELETE, MERGE, TRUNCATE TABLE, SELECT … INTO, INSERT … SELECT,
                     ALTER TABLE … SWITCH, DBCC CHECKIDENT, EXECUTE of a user procedure, a WITH CHECK CHECK without its ADD
expect:   outcome    every one is stop unknown-statement naming its line · exit 9; none yields "questions none"
laws:     F4 H2
source:   expected · INVARIANTS F4 ("a synthetic unknown shape always stops")
```

### QST-95 · M1: deleting any vocabulary row makes its receipt case stop
```case
kind:     pure
action:   the mutation pass (ARCH §9.4) over vocabulary.psd1, one row removed at a time
expect:   questions  removing HasRows fails QST-01; NotNull fails QST-16; Fits QST-17; Unique QST-35; Orphan QST-50;
                     CheckFalse QST-60; KeyLength QST-47; Rebuild QST-63; Computed QST-66; Temporal QST-89; Scan QST-44;
                     Authored QST-78; SeedDrift QST-80 — each now stops unknown-statement or loses its question
laws:     M1 E1
source:   expected · INVARIANTS M1
```

### QST-96 · determinism: the same delta twice, and independent batches reordered
```case
kind:     pure
action:   Get-FxQuestions run twice on QST-02's input, then on the same input with the CREATE UNIQUE INDEX batch and the FK ADD batch swapped
expect:   questions  byte-identical questions JSON in all three runs (line annotations aside)
laws:     D1
source:   expected · INVARIANTS D1; the canonical order makes the list independent of statement order
```

### QST-97 · encoding and line endings never change the questions
```case
kind:     pure
action:   Get-FxQuestions on QST-02's input written four ways: UTF-8 with BOM, without BOM, CRLF throughout, LF throughout, and with tabs for the indent
expect:   questions  identical in all four
laws:     D1
source:   expected · the boundary delta itself mixes CRLF (header comments) and LF (statements) behind a BOM (receipt: TWIN_SUCCESSION §6b's delta_r_meta.sql)
```

### QST-98 · identifiers: brackets stripped, `]]` unescaped, a part with a dot kept quoted
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Order]]Line])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     IF EXISTS (select top 1 1 from [sales.eu].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
expect:   questions  HasRows([Order]]Line]) · HasRows([sales.eu].Customer)
laws:     D1 P2
source:   expected · coordinate parts are kept separately and rendered bracket-quoted only when a part holds `.`, `[`, `]` or whitespace; P2 re-validates each part with OBJECT_ID before any probe
```

### QST-99 · F4: one delta, the three statement classes, each batch in exactly one
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
                     GO
                     CREATE NONCLUSTERED INDEX [IX_Order_StatusId]
                         ON [dbo].[Order]([StatusId] ASC);
                     GO
                     ALTER TABLE [dbo].[Product]
                         ADD [Note] NVARCHAR (50) NULL;
                     GO
                     EXECUTE sp_refreshsqlmodule N'[dbo].[vCustomer]';
                     GO
          base       Customer.Email nvarchar(256) NULL · Order.StatusId int NOT NULL
expect:   questions  HasRows(Customer) · NotNull(Customer.Email) · measures Rows(Customer), Rows(Order) for Scan(Order)
                     · per batch: 1 data-sensitive (HasRows) · 2 data-sensitive (NotNull) · 3 data-insensitive (Scan)
                     · 4 non-reading · 5 non-reading
laws:     F4 D1
source:   expected · ARCH §4 (the three classes); INVARIANTS F4
note:     Product, touched only by a non-reading statement, is not measured. A batch counted in two classes, or in
          none, fails the case.
```

### QST-100 · coordinate rule 1: a unique index on a new defaulted column is an exact duplicate from two rows
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Product]
                         ADD [Sku] NVARCHAR (20) CONSTRAINT [DF_Product_Sku] DEFAULT (N'NONE') NOT NULL;
                     GO
                     CREATE UNIQUE NONCLUSTERED INDEX [UX_Product_Sku]
                         ON [dbo].[Product]([Sku] ASC);
                     GO
          base       Product has no Sku
expect:   questions  Unique(Product,(Sku)) classes {exact}, answered from the delta by Rows(Product) ≥ 2 · measures
                     Rows(Product) for Scan(Product)
laws:     F4 S3
source:   expected · ARCH §4 Coordinates rule 1 ("a unique index on a new defaulted column of a table with two or more
          rows is an exact duplicate")
note:     Nothing is probed under Sku, which the environment lacks (S3); the copy's two presence rows both receive
          N'NONE' from the ADD, so the publish meets the same duplicate the environment would.
```

### QST-101 · coordinate rule 1: an FK from an existing column to a new table, with no script filling it
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      CREATE TABLE [dbo].[OrderType] (
                         [Id]   INT           NOT NULL,
                         [Code] NVARCHAR (20) NOT NULL,
                         CONSTRAINT [PK_OrderType_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
                     );
                     GO
                     ALTER TABLE [dbo].[Order] WITH NOCHECK
                         ADD CONSTRAINT [FK_Order_OrderType] FOREIGN KEY ([OrderTypeId]) REFERENCES [dbo].[OrderType] ([Id]);
                     GO
                     ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_OrderType];
                     GO
          base       Order.OrderTypeId int NULL
          scripts    no pre- or post-deploy statement differs from the merge-base
expect:   questions  Orphan(Order.(OrderTypeId)→OrderType), answered from the delta: violating exactly when an
                     existing OrderTypeId is non-NULL (the probe counts Order.OrderTypeId IS NOT NULL) · measures
                     Rows(Order)
laws:     F4 S3
source:   expected · ARCH §4 Coordinates rule 1 ("an orphan for every non-NULL child value, unless a pre- or
          post-deploy script fills the parent")
note:     The parent is never probed (S3); only the existing child column is. With a seed that fills OrderType the same
          question becomes Authored (QST-59).
```

### QST-102 · coordinate rule 2: a column renamed earlier in the delta is probed under its old name
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      EXECUTE sp_rename @objname = N'[dbo].[Customer].[ContactPhone]', @newname = N'MobileNumber', @objtype = N'COLUMN';
                     GO
                     IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Customer] ALTER COLUMN [MobileNumber] NVARCHAR (40) NOT NULL;
                     GO
          base       Customer.ContactPhone nvarchar(40) NULL · no MobileNumber
expect:   questions  HasRows(Customer) · NotNull(Customer.MobileNumber) probed as Customer.ContactPhone · measures
                     Rows(Customer)
laws:     F4 S3
source:   expected · ARCH §4 Coordinates rule 2; INVARIANTS S3 ("a constraint on a column renamed earlier in the delta
          is probed under the old name")
note:     The probe text names [ContactPhone], which exists in the environment; probing MobileNumber would stop
          missing-object, and reading it as empty would hide every NULL.
```

### QST-103 · coordinate rule 2: a table moved by ALTER SCHEMA … TRANSFER is probed in its old schema
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER SCHEMA [archive] TRANSFER [dbo].[Category];
                     GO
                     ALTER TABLE [archive].[Category] WITH NOCHECK
                         ADD CONSTRAINT [CK_Category_Code] CHECK (LEN([Code])>(0));
                     GO
                     ALTER TABLE [archive].[Category] WITH CHECK CHECK CONSTRAINT [CK_Category_Code];
                     GO
          base       dbo.Category (Code nvarchar(30) NOT NULL) · no archive.Category
expect:   questions  CheckFalse(archive.Category,CK_Category_Code) predicate (LEN([Code])>(0)) probed as dbo.Category
                     · measures Rows(archive.Category) as Rows(dbo.Category)
laws:     F4 S3
source:   expected · ARCH §4 Coordinates rule 2 ("sp_rename and ALTER SCHEMA … TRANSFER in the same delta map the
          coordinate to its old name")
```

### QST-104 · coordinate rule 4: a coordinate the delta neither creates nor renames, absent from the catalog, stops
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      IF EXISTS (select top 1 1 from [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
                     GO
                     ALTER TABLE [dbo].[Customer] ALTER COLUMN [Nickname] NVARCHAR (40) NOT NULL;
                     GO
          base       Customer has no Nickname (the catalog read for Dev)
expect:   outcome    stop missing-object · "Dev has no dbo.Customer.Nickname, and this delta does not create it. Prove
                     against what Dev has, or record Dev as Not checked." · exit 5
laws:     S3 F4 H3
source:   expected · ARCH §4 Coordinates rule 4; INVARIANTS S3 ("never read as empty")
note:     This arises when a delta is paired with another environment's catalog, or the schema moved between read's two
          touches; Rows or a NULL count of zero would be a silent clean.
```

### QST-105 · ADD PERIOD FOR SYSTEM_TIME over existing columns asks Temporal; over new columns it is answered from the delta
```case
kind:     pure
action:   Get-FxQuestions, once per base
input:    delta      ALTER TABLE [dbo].[Account]
                         ADD PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]);
                     GO
          base       (a) Account.ValidFrom datetime2(7) NOT NULL · Account.ValidTo datetime2(7) NOT NULL, both existing
                     (b) neither column exists; the same delta adds them earlier with defaults SYSUTCDATETIME() and
                     '9999-12-31 23:59:59.9999999'
expect:   questions  (a) Temporal(Account) period (ValidFrom, ValidTo) · measures Rows(Account)
                     (b) none · discharged {Temporal(Account): the new period columns hold their defaults, start before
                     end, in every existing row}
laws:     F4 S3
source:   verify · whether DacFx adds a period over existing columns with this statement or rebuilds the table (QST-63's
          shape); the question is ARCH §4's Temporal row
```

### QST-106 · a CHECK whose predicate holds CONVERT is asked, but unmeasured
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[T] WITH NOCHECK
                         ADD CONSTRAINT [CK_T_RefNum] CHECK (CONVERT([int],[Ref])>(0));
                     GO
                     ALTER TABLE [dbo].[T] WITH CHECK CHECK CONSTRAINT [CK_T_RefNum];
                     GO
          base       T.Ref varchar(20) NULL
expect:   questions  CheckFalse(T,CK_T_RefNum) predicate (CONVERT([int],[Ref])>(0)) unmeasured · measures Rows(T)
laws:     F4 P2 P3
source:   expected · ARCH §4 (the predicate exception refuses CONVERT, CAST and PARSE, P3); INVARIANTS P2
note:     The predicate would quote an unconvertible value in its error, so it is never sent; the witness is
          agent-declared and the record carries declared-not-measured.
```

### QST-107 · a clustered PK drop and an index DROP + CREATE are Scans
```case
kind:     pure
action:   Get-FxQuestions
input:    delta      ALTER TABLE [dbo].[Product] DROP CONSTRAINT [PK_Product_Id];
                     GO
                     DROP INDEX [IX_Order_StatusId]
                         ON [dbo].[Order];
                     GO
                     CREATE NONCLUSTERED INDEX [IX_Order_StatusId]
                         ON [dbo].[Order]([StatusId] ASC, [CustomerId] ASC);
                     GO
          base       Product PK_Product_Id CLUSTERED (Id) · Order.StatusId int NOT NULL · Order.CustomerId int NOT NULL
expect:   questions  none · measures Rows(Product) for Scan(Product), Rows(Order) for Scan(Order)
laws:     F4
source:   expected · ARCH §4 Scan row (an index DROP+CREATE; a clustered PK drop or create)
note:     A PK create still asks Unique (QST-42), a data-sensitive question that is measured all the same.
```

### QST-108 · Scan, end to end: a non-unique index on a populated table publishes clean at T0
```case
kind:     corpus
env:      pg · Order 4 rows · Customer 2 rows · Status 3 rows (the lane)
change:   head adds IX_Order_StatusId (non-unique, on Order.StatusId) · scripts unchanged
expect:   questions  none · measures Rows(Order) for Scan(Order)
          answers    Rows(Order)=4
          witnesses  presence(Order)
          outcome    verdict clean · exit 0
          record     tier T0 · flags {} · silence ok
laws:     F4 F3 D6
source:   expected · ARCH §4 Scan row, §7; the engine outcome is SamplePr "add-index: a non-unique index builds clean over a populated table"
note:     The presence row makes the copy's build run over a row, as Dev's does.
```

### QST-109 · Computed, end to end: unmeasured, then declared, then the conversion block
```case
kind:     corpus
env:      pg · Product 3 rows, Code 'P-100', '200', '300'
change:   head adds Product.CodeNum AS (CONVERT (INT, [Code])) PERSISTED · leg2 adds to witness.sql, under `-- witness:
          Computed(Product.CodeNum)`, one Product row with Code N'wABC'
expect:   leg1:      questions  Computed(Product.CodeNum) expression (CONVERT (INT, [Code])) unmeasured · measures
                                Rows(Product)
                     outcome    stop computed-witness · "Write one row into witness.sql whose dbo.Product.CodeNum
                                expression fails, then re-run." · exit 5
          leg2:      witnesses  presence(Product) · declared(Product row, Code N'wABC')
                     outcome    verdict blocked conversion · Msg 245 · dbo.Product.CodeNum · exit 3
                     record     tier T2 · notChecked includes declared-not-measured
laws:     F4 F3 H2 A5
source:   expected · ARCH §4 Computed row; red-team #3 (a PERSISTED computed column reads every row with no guard ✓)
note:     leg1 is a designed stop (F3 scope). In leg2 the oracle publishes onto a copy of env, where 'P-100' fails the
          same way, so both arms block Msg 245.
```

### QST-110 · Temporal, end to end: versioning turned back on over a history table that holds rows
```case
kind:     corpus
env:      pg + Customer system-versioned and then set SYSTEM_VERSIONING = OFF (its PERIOD FOR SYSTEM_TIME (ValidFrom,
          ValidTo) kept; CustomerHistory a plain table) · Customer 3 rows · CustomerHistory 2 rows, each period valid,
          none overlapping
change:   head declares Customer WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.CustomerHistory)) · scripts
          unchanged
expect:   questions  Temporal(Customer) history CustomerHistory · measures Rows(Customer), Rows(CustomerHistory)
          answers    Temporal(Customer)=clean
          witnesses  presence(Customer) · presence(CustomerHistory)
          outcome    verdict clean · exit 0
          record     tier T2
laws:     F4 F3 F9
source:   verify · whether Extract keeps a period whose versioning is off (F9), and the SET (SYSTEM_VERSIONING = ON …)
          DacFx writes (QST-89); the engine fact that converting a populated Customer preserves its rows is SamplePr
          "temporal-convert: turning the existing populated Customer system-versioned — the true outcome under the production gate, existing rows preserved"
note:     The violating leg (an overlapping history row) fails the SET … ON in the main phase with an engine number
          ARCH §6 lists only under `blocked authored`; until a main-phase rule classifies it, H5 makes it stop
          unclassified (reported).
```

### QST-111 · SeedDrift, end to end: QA holds the new seed code under another key
```case
kind:     corpus
env:      Dev = pg · Status 3 rows, the seed's own · QA = pg · Status 4 rows: the seed's 3 plus (5, 'Refunded')
change:   the post-deploy Status MERGE gains (4, N'Refunded', 1) · witness.sql for QA declares, under `-- witness:
          SeedDrift(Status)`, one Status row (5, N'Refunded', 1)
expect:   questions  SeedDrift(Status) key Id, code Code, from post:Data/Seed.sql · measures Rows(Status)
          answers    Dev: SeedDrift(Status)=clean · QA: SeedDrift(Status)=violating
          witnesses  Dev: none (the merge-base lane holds Status's rows) · QA: declared(Status (5, N'Refunded', 1))
          outcome    Dev: verdict clean · exit 0
                     QA: verdict blocked authored · Msg 2601 · post-deploy · exit 3
          record     tier T3 · QA residue {pre-deploy: none · main: none · post-deploy: failed at the seed}
laws:     F4 F3 F7 H5
source:   expected · ARCH §4 SeedDrift row; red-team #15 (seed Ids differ per environment); 'Refunded' is the seed's own
          code, a repository-defined value (P5)
note:     UIX_Status_Code makes the MERGE's INSERT of (4, 'Refunded') a duplicate on QA. The witness carries the
          seed's code under a key the seed does not use, so no QA value reaches the copy.
```
