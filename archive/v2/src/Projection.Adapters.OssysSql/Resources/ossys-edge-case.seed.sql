-- V2 chapter 5.0 slice γ divergence from V1 source: V1's
-- model.edge-case.seed.sql created its own `OutsystemsIntegration`
-- database + filegroup + ndf file. V2 runs against per-run databases
-- created by `Deploy.withBootstrappedDatabase`, so this header strips
-- the V1 DB / filegroup management. The structural shape (synthetic
-- OSSYS schema + edge-case data) is preserved; the `ON [FG_Customers]`
-- filegroup placements on CREATE INDEX lines are replaced with the
-- default filegroup (PRIMARY) below at slice γ. V1's partition function
-- + scheme + DATA_COMPRESSION REBUILD-PARTITION block (V1 lines
-- 184-215) is also stripped here — partitioning requires Enterprise
-- Edition + adds noise to the canary; structural intent of an audit
-- trigger + index on JobRun is preserved without partitioning.

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'billing')
BEGIN
    EXEC('CREATE SCHEMA [billing]');
END;
GO

CREATE TABLE [dbo].[ossys_Espace]
(
    [Id] INT NOT NULL PRIMARY KEY,
    [Name] NVARCHAR(256) NOT NULL,
    [Is_System] BIT NOT NULL,
    [Is_Active] BIT NOT NULL,
    [EspaceKind] NVARCHAR(50) NULL,
    [SS_Key] UNIQUEIDENTIFIER NULL
);
GO

CREATE TABLE [dbo].[ossys_Entity]
(
    [Id] INT NOT NULL PRIMARY KEY,
    [Name] NVARCHAR(256) NOT NULL,
    [Physical_Table_Name] NVARCHAR(256) NOT NULL,
    [Espace_Id] INT NOT NULL,
    [Is_Active] BIT NOT NULL,
    [Is_System] BIT NOT NULL,
    [Is_External] BIT NOT NULL,
    [Data_Kind] NVARCHAR(50) NULL,
    [PrimaryKey_SS_Key] UNIQUEIDENTIFIER NULL,
    [SS_Key] UNIQUEIDENTIFIER NULL,
    [Description] NVARCHAR(MAX) NULL,
    CONSTRAINT FK_ossys_Entity_Espace FOREIGN KEY ([Espace_Id]) REFERENCES [dbo].[ossys_Espace]([Id])
);
GO

CREATE TABLE [dbo].[ossys_Entity_Attr]
(
    [Id] INT NOT NULL PRIMARY KEY,
    [Entity_Id] INT NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [SS_Key] UNIQUEIDENTIFIER NULL,
    [Data_Type] NVARCHAR(200) NULL,
    [Length] INT NULL,
    [Precision] INT NULL,
    [Scale] INT NULL,
    [Default_Value] NVARCHAR(MAX) NULL,
    [Is_Mandatory] BIT NOT NULL,
    [Is_Active] BIT NOT NULL,
    [Is_AutoNumber] BIT NULL,
    [Is_Identifier] BIT NULL,
    [Referenced_Entity_Id] INT NULL,
    [Original_Name] NVARCHAR(200) NULL,
    [External_Column_Type] NVARCHAR(200) NULL,
    [Delete_Rule] NVARCHAR(50) NULL,
    [Physical_Column_Name] NVARCHAR(200) NULL,
    [Database_Name] NVARCHAR(200) NULL,
    -- `Type` is the OutSystems runtime/binding-type column: scalar attrs
    -- carry `rt`-codes (rtText, ...) and reference attrs carry the
    -- `bt<EspaceSsKey>*<EntitySsKey>` binding code. The extraction reads
    -- it into the bt-resolving CTE (`#RefResolved`).
    [Type] NVARCHAR(200) NULL,
    [Legacy_Type] NVARCHAR(200) NULL,
    [Decimals] INT NULL,
    [Original_Type] NVARCHAR(200) NULL,
    [Description] NVARCHAR(MAX) NULL,
    -- WP8 / NM-72 — Service-Studio authored attribute order. The real
    -- `ossys_Entity_Attr` exposes this column; the extraction reads it
    -- (COALESCEing to `Id` on estates that lack it). The seed populates
    -- it with an order DELIBERATELY DIFFERENT from alphabetical so the
    -- golden + focused tests prove emission follows authored order, not
    -- name order.
    [Order_Num] INT NULL,
    CONSTRAINT FK_ossys_Entity_Attr_Entity FOREIGN KEY ([Entity_Id]) REFERENCES [dbo].[ossys_Entity]([Id])
);
GO

INSERT INTO [dbo].[ossys_Espace] ([Id], [Name], [Is_System], [Is_Active], [EspaceKind], [SS_Key]) VALUES
    (100, N'AppCore', 0, 1, N'eSpace', '11111111-1111-1111-1111-111111111111'),
    (200, N'Ops', 0, 1, N'eSpace', '22222222-2222-2222-2222-222222222222'),
    (300, N'SystemUsers', 1, 1, N'eSpace', '33333333-3333-3333-3333-333333333333');
GO

INSERT INTO [dbo].[ossys_Entity]
    ([Id], [Name], [Physical_Table_Name], [Espace_Id], [Is_Active], [Is_System], [Is_External], [Data_Kind], [PrimaryKey_SS_Key], [SS_Key], [Description])
VALUES
    (1000, N'Customer', N'OSUSR_ABC_CUSTOMER', 100, 1, 0, 0, N'entity', 'aaaaaaaa-0000-0000-0000-000000000001', 'bbbbbbbb-0000-0000-0000-000000000001', N'Stores customer records for AppCore'),
    (2001, N'City', N'OSUSR_DEF_CITY', 100, 1, 0, 0, N'entity', 'aaaaaaaa-0000-0000-0000-000000000002', 'bbbbbbbb-0000-0000-0000-000000000002', NULL),
    (2002, N'BillingAccount', N'BILLING_ACCOUNT', 100, 1, 0, 1, N'entity', 'aaaaaaaa-0000-0000-0000-000000000003', 'bbbbbbbb-0000-0000-0000-000000000003', NULL),
    (4000, N'JobRun', N'OSUSR_XYZ_JOBRUN', 200, 1, 0, 0, N'entity', 'aaaaaaaa-0000-0000-0000-000000000004', 'bbbbbbbb-0000-0000-0000-000000000004', NULL),
    (3001, N'User', N'OSUSR_U_USER', 300, 1, 1, 0, N'entity', 'aaaaaaaa-0000-0000-0000-000000000005', 'bbbbbbbb-0000-0000-0000-000000000005', NULL);
GO

-- WP8 / NM-72 — `Order_Num` (last column) carries the Service-Studio
-- authored order. The values are chosen so the emitted column order
-- DIFFERS from alphabetical on every multi-attribute entity:
--   Customer:       Id(PK), LegacyCode(10), FirstName(20), LastName(30),
--                   Email(40), CityId(50)  [alpha: CityId,Email,First,Last,Legacy]
--   City:           Id(PK), Name(10), IsActive(20)         [alpha: IsActive,Name]
--   BillingAccount: Id(PK), ExtRef(10), AccountNumber(20)  [alpha: AccountNumber,ExtRef]
--   JobRun:         Id(PK), TriggeredByUserId(10), CreatedOn(20)
--                                                  [alpha: CreatedOn,TriggeredByUserId]
INSERT INTO [dbo].[ossys_Entity_Attr]
    ([Id], [Entity_Id], [Name], [SS_Key], [Data_Type], [Length], [Precision], [Scale], [Default_Value], [Is_Mandatory], [Is_Active], [Is_AutoNumber], [Is_Identifier], [Referenced_Entity_Id], [Original_Name], [External_Column_Type], [Delete_Rule], [Physical_Column_Name], [Database_Name], [Type], [Legacy_Type], [Decimals], [Original_Type], [Description], [Order_Num])
VALUES
    (10001, 1000, N'Id', 'cccccccc-0000-0000-0000-000000000001', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID', NULL, NULL, NULL, NULL, NULL, N'Customer identifier', 1),
    (10002, 1000, N'Email', 'cccccccc-0000-0000-0000-000000000002', N'Text', 255, NULL, NULL, NULL, 1, 1, 0, 0, NULL, N'EmailAddress', NULL, NULL, N'EMAIL', NULL, NULL, NULL, NULL, NULL, N'Customer email', 40),
    (10003, 1000, N'FirstName', 'cccccccc-0000-0000-0000-000000000003', N'Text', 100, NULL, NULL, N'''''' , 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'FIRSTNAME', NULL, NULL, NULL, NULL, NULL, N'Customer first name', 20),
    (10004, 1000, N'LastName', 'cccccccc-0000-0000-0000-000000000004', N'Text', 100, NULL, NULL, N'''''' , 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'LASTNAME', NULL, NULL, NULL, NULL, NULL, N'Customer last name', 30),
    -- CityId: same-module FK (AppCore Customer -> AppCore City). The
    -- `Type` column carries the `bt<EspaceSsKey>*<EntitySsKey>` binding
    -- code and `Referenced_Entity_Id` is NULL, so the rowset CTE must
    -- resolve the target by parsing the bt-code (the real OSSYS shape).
    -- `Data_Type` keeps the resolved scalar (Identifier -> BIGINT).
    (10005, 1000, N'CityId', 'cccccccc-0000-0000-0000-000000000005', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, N'Protect', N'CITYID', NULL, N'bt11111111-1111-1111-1111-111111111111*bbbbbbbb-0000-0000-0000-000000000002', NULL, NULL, NULL, N'FK to City', 50),
    (10006, 1000, N'LegacyCode', 'cccccccc-0000-0000-0000-000000000006', N'Text', 50, NULL, NULL, NULL, 0, 0, 0, 0, NULL, NULL, NULL, NULL, N'LEGACYCODE', NULL, NULL, NULL, NULL, NULL, NULL, 10),
    (20011, 2001, N'Id', 'dddddddd-0000-0000-0000-000000000001', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID', NULL, NULL, NULL, NULL, NULL, NULL, 1),
    (20012, 2001, N'Name', 'dddddddd-0000-0000-0000-000000000002', N'Text', 200, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'NAME', NULL, NULL, NULL, NULL, NULL, NULL, 10),
    (20013, 2001, N'IsActive', 'dddddddd-0000-0000-0000-000000000003', N'Boolean', NULL, NULL, NULL, N'1', 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'ISACTIVE', NULL, NULL, NULL, NULL, NULL, NULL, 20),
    (30021, 2002, N'Id', 'eeeeeeee-0000-0000-0000-000000000001', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, N'int', NULL, N'ID', NULL, NULL, NULL, NULL, NULL, NULL, 1),
    (30022, 2002, N'AccountNumber', 'eeeeeeee-0000-0000-0000-000000000002', N'Text', 50, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, N'varchar(50)', NULL, N'ACCOUNTNUMBER', NULL, NULL, NULL, NULL, NULL, NULL, 20),
    (30023, 2002, N'ExtRef', 'eeeeeeee-0000-0000-0000-000000000003', N'Text', 50, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, N'varchar(50)', NULL, N'EXTREF', NULL, NULL, NULL, NULL, NULL, NULL, 10),
    (40031, 4000, N'Id', 'ffffffff-0000-0000-0000-000000000001', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID', NULL, NULL, NULL, NULL, NULL, NULL, 1),
    -- TriggeredByUserId: cross-module FK (Ops JobRun -> SystemUsers User).
    -- The `Type` column's bt-code names a DIFFERENT espace (SystemUsers,
    -- 300) than the source (Ops, 200) with a NULL Referenced_Entity_Id,
    -- so resolving it exercises the cross-module reference path end-to-end.
    (40032, 4000, N'TriggeredByUserId', 'ffffffff-0000-0000-0000-000000000002', N'Identifier', NULL, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, N'Ignore', N'TRIGGEREDBYUSERID', NULL, N'bt33333333-3333-3333-3333-333333333333*bbbbbbbb-0000-0000-0000-000000000005', NULL, NULL, NULL, NULL, 10),
    (40033, 4000, N'CreatedOn', 'ffffffff-0000-0000-0000-000000000003', N'DateTime', NULL, NULL, NULL, N'getutcdate()', 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'CREATEDON', NULL, NULL, NULL, NULL, NULL, NULL, 20),
    (50041, 3001, N'Id', '99999999-0000-0000-0000-000000000001', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID', NULL, NULL, NULL, NULL, NULL, NULL, 1);
GO

CREATE TABLE [dbo].[OSUSR_DEF_CITY]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [NAME] NVARCHAR(200) NOT NULL,
    [ISACTIVE] BIT NOT NULL CONSTRAINT [DF_OSUSR_DEF_CITY_ISACTIVE] DEFAULT 1,
    CONSTRAINT [PK_City_Id] PRIMARY KEY CLUSTERED ([ID])
);
GO

CREATE TABLE [dbo].[OSUSR_ABC_CUSTOMER]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NOT NULL,
    [FIRSTNAME] NVARCHAR(100) NULL CONSTRAINT [DF_OSUSR_ABC_CUSTOMER_FIRSTNAME] DEFAULT (''),
    [LASTNAME] NVARCHAR(100) NULL CONSTRAINT [DF_OSUSR_ABC_CUSTOMER_LASTNAME] DEFAULT (''),
    [CITYID] INT NOT NULL,
    [LEGACYCODE] NVARCHAR(50) NULL,
    CONSTRAINT [PK_Customer_Id] PRIMARY KEY CLUSTERED ([ID]),
    CONSTRAINT [FK_OSUSR_ABC_CUSTOMER_OSUSR_DEF_CITY] FOREIGN KEY ([CITYID]) REFERENCES [dbo].[OSUSR_DEF_CITY]([ID])
);
GO

-- V2 chapter 5.0 slice γ divergence from V1 source: SQL Server (2012+)
-- rejects IGNORE_DUP_KEY=ON on filtered indexes. V1's fixture either
-- predates the constraint or runs against a lax server build; V2 drops
-- the option to land cleanly on the warm Docker SQL Server (2022). The
-- structural intent (filtered unique index on EMAIL) is preserved.
CREATE UNIQUE NONCLUSTERED INDEX [IDX_CUSTOMER_EMAIL]
ON [dbo].[OSUSR_ABC_CUSTOMER]([EMAIL] ASC)
WHERE [EMAIL] IS NOT NULL
WITH (FILLFACTOR = 85, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON);
GO

CREATE NONCLUSTERED INDEX [IDX_CUSTOMER_NAME]
ON [dbo].[OSUSR_ABC_CUSTOMER]([LASTNAME] ASC, [FIRSTNAME] ASC)
WITH (STATISTICS_NORECOMPUTE = ON, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON);
GO

ALTER INDEX [IDX_CUSTOMER_NAME] ON [dbo].[OSUSR_ABC_CUSTOMER] DISABLE;
GO

CREATE TABLE [billing].[BILLING_ACCOUNT]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [ACCOUNTNUMBER] VARCHAR(50) NOT NULL,
    [EXTREF] VARCHAR(50) NULL,
    CONSTRAINT [PK_BillingAccount_Id] PRIMARY KEY CLUSTERED ([ID])
);
GO

CREATE UNIQUE NONCLUSTERED INDEX [IDX_BILLINGACCOUNT_ACCTNUM]
ON [billing].[BILLING_ACCOUNT]([ACCOUNTNUMBER] ASC);
GO

-- V2 chapter 5.0 slice γ divergence: V1's partition function + scheme
-- + DATA_COMPRESSION REBUILD-PARTITION block (lines 184-215 of V1
-- source) require SQL Server Enterprise Edition + add noise the
-- canary doesn't exercise. Replaced with a plain non-partitioned
-- DESC index; structural intent (descending index on CreatedAt) is
-- preserved for slice γ's IndexColumnDirection extraction tests.

CREATE TABLE [dbo].[OSUSR_XYZ_JOBRUN]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [TRIGGEREDBYUSERID] INT NULL,
    [CREATEDON] DATETIME2(7) NOT NULL CONSTRAINT [DF_OSUSR_XYZ_JOBRUN_CREATEDON] DEFAULT getutcdate(),
    CONSTRAINT [PK_JobRun_Id] PRIMARY KEY CLUSTERED ([ID])
);
GO

CREATE NONCLUSTERED INDEX [OSIDX_JOBRUN_CREATEDON]
ON [dbo].[OSUSR_XYZ_JOBRUN]([CREATEDON] DESC)
WITH (ALLOW_ROW_LOCKS = OFF, ALLOW_PAGE_LOCKS = ON);
GO

EXEC (N'CREATE TRIGGER [dbo].[TR_OSUSR_XYZ_JOBRUN_AUDIT] ON [dbo].[OSUSR_XYZ_JOBRUN] AFTER INSERT AS BEGIN SET NOCOUNT ON; END');
GO

DISABLE TRIGGER [dbo].[TR_OSUSR_XYZ_JOBRUN_AUDIT] ON [dbo].[OSUSR_XYZ_JOBRUN];
GO

IF OBJECT_ID(N'dbo.OSUSR_U_USER', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[OSUSR_U_USER]
    (
        [ID] INT IDENTITY(1,1) NOT NULL,
        CONSTRAINT [PK_User_Id] PRIMARY KEY CLUSTERED ([ID])
    );
END;
GO

-- ===================================================================
-- Comprehensive edge-case expansion. Adds four modules (Sales,
-- Inventory, Integration[Extension], RefData) and a variegated entity
-- set that deliberately exercises every extraction-contract surface
-- the original five-entity fixture left untouched:
--   * composite primary key (OrderLine)
--   * self-referencing FK (Category.ParentCategoryId)
--   * multiple FKs on one entity (SalesOrder -> Customer + Category)
--   * referential actions: Cascade ('Delete'), SetNull, NoAction
--     ('Protect'/'Ignore')
--   * untrusted (WITH NOCHECK) FK (StockMovement.SupplierId)
--   * computed column (Product.DisplayLabel PERSISTED)
--   * CHECK constraint (Product.Price >= 0)
--   * included (non-key) index columns (StockItem)
--   * type variety: DECIMAL(p,s), Currency, Date, DateTimeOffset, Time,
--     Float, BinaryData->VARBINARY(MAX), Text->NVARCHAR(MAX), Integer,
--     LongInteger->BIGINT, GUID
--   * Extension module (EspaceKind='Extension') + external entity ->
--     ExternalIndirect origin (SyncLog)
--   * cross-module FK in a second direction (Sales -> AppCore)
--   * static (lookup) entities (Country, CurrencyCode)
--   * multiple triggers on one entity, one enabled + one disabled
-- Every entity carries a consistent physical OSUSR_*/billing table so the
-- column/index/trigger/FK-reality joins enrich it. Reference attributes
-- use the bt<EspaceSsKey>*<EntitySsKey> binding encoding in [Type] with a
-- NULL Referenced_Entity_Id (the real OSSYS shape).
-- ===================================================================

INSERT INTO [dbo].[ossys_Espace] ([Id], [Name], [Is_System], [Is_Active], [EspaceKind], [SS_Key]) VALUES
    (400, N'Sales',       0, 1, N'eSpace',    '44444444-4444-4444-4444-444444444444'),
    (500, N'Inventory',   0, 1, N'eSpace',    '55555555-5555-5555-5555-555555555555'),
    (600, N'Integration', 0, 1, N'Extension', '66666666-6666-6666-6666-666666666666'),
    (700, N'RefData',     0, 1, N'eSpace',    '77777777-7777-7777-7777-777777777777');
GO

INSERT INTO [dbo].[ossys_Entity]
    ([Id], [Name], [Physical_Table_Name], [Espace_Id], [Is_Active], [Is_System], [Is_External], [Data_Kind], [PrimaryKey_SS_Key], [SS_Key], [Description])
VALUES
    (5000, N'Product',       N'OSUSR_SAL_PRODUCT',   400, 1, 0, 0, N'entity',       'aaaaaaaa-0000-0000-0000-000000000010', 'bbbbbbbb-0000-0000-0000-000000000010', N'Sellable product with rich type coverage'),
    (5001, N'Category',      N'OSUSR_SAL_CATEGORY',  400, 1, 0, 0, N'entity',       'aaaaaaaa-0000-0000-0000-000000000011', 'bbbbbbbb-0000-0000-0000-000000000011', N'Self-referencing category tree'),
    (5002, N'SalesOrder',    N'OSUSR_SAL_ORDER',     400, 1, 0, 0, N'entity',       'aaaaaaaa-0000-0000-0000-000000000012', 'bbbbbbbb-0000-0000-0000-000000000012', NULL),
    (5003, N'OrderLine',     N'OSUSR_SAL_ORDERLINE', 400, 1, 0, 0, N'entity',       'aaaaaaaa-0000-0000-0000-000000000013', 'bbbbbbbb-0000-0000-0000-000000000013', N'Composite-PK order line'),
    (6000, N'StockItem',     N'OSUSR_INV_STOCKITEM', 500, 1, 0, 0, N'entity',       'aaaaaaaa-0000-0000-0000-000000000020', 'bbbbbbbb-0000-0000-0000-000000000020', NULL),
    (6001, N'Supplier',      N'OSUSR_INV_SUPPLIER',  500, 1, 0, 0, N'entity',       'aaaaaaaa-0000-0000-0000-000000000021', 'bbbbbbbb-0000-0000-0000-000000000021', NULL),
    (6002, N'StockMovement', N'OSUSR_INV_MOVEMENT',  500, 1, 0, 0, N'entity',       'aaaaaaaa-0000-0000-0000-000000000022', 'bbbbbbbb-0000-0000-0000-000000000022', NULL),
    (6100, N'SyncLog',       N'OSUSR_INT_SYNCLOG',   600, 1, 0, 1, N'entity',       'aaaaaaaa-0000-0000-0000-000000000030', 'bbbbbbbb-0000-0000-0000-000000000030', N'External sync log via Integration Studio'),
    (7000, N'Country',       N'OSUSR_REF_COUNTRY',   700, 1, 0, 0, N'staticEntity', 'aaaaaaaa-0000-0000-0000-000000000040', 'bbbbbbbb-0000-0000-0000-000000000040', N'Static country lookup'),
    (7001, N'CurrencyCode',  N'OSUSR_REF_CURRENCY',  700, 1, 0, 0, N'staticEntity', 'aaaaaaaa-0000-0000-0000-000000000041', 'bbbbbbbb-0000-0000-0000-000000000041', N'Static currency lookup');
GO

INSERT INTO [dbo].[ossys_Entity_Attr]
    ([Id], [Entity_Id], [Name], [SS_Key], [Data_Type], [Length], [Precision], [Scale], [Default_Value], [Is_Mandatory], [Is_Active], [Is_AutoNumber], [Is_Identifier], [Referenced_Entity_Id], [Original_Name], [External_Column_Type], [Delete_Rule], [Physical_Column_Name], [Database_Name], [Type], [Legacy_Type], [Decimals], [Original_Type], [Description])
VALUES
    -- Product (5000): DECIMAL(18,2), Currency, Date, VARBINARY(MAX),
    -- NVARCHAR(MAX), a computed column, and a CHECK constraint.
    (50001, 5000, N'Id',           '00050001-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID',           NULL, NULL, NULL, NULL, NULL, NULL),
    (50002, 5000, N'Sku',          '00050002-0000-0000-0000-000000000000', N'Text',         40, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'SKU',          NULL, NULL, NULL, NULL, NULL, NULL),
    (50003, 5000, N'Price',        '00050003-0000-0000-0000-000000000000', N'Decimal',    NULL,   18,    2, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'PRICE',        NULL, NULL, NULL, NULL, NULL, NULL),
    (50004, 5000, N'Cost',         '00050004-0000-0000-0000-000000000000', N'Currency',   NULL, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'COST',         NULL, NULL, NULL, NULL, NULL, NULL),
    (50005, 5000, N'LaunchDate',   '00050005-0000-0000-0000-000000000000', N'Date',       NULL, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'LAUNCHDATE',   NULL, NULL, NULL, NULL, NULL, NULL),
    (50006, 5000, N'Photo',        '00050006-0000-0000-0000-000000000000', N'BinaryData', NULL, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'PHOTO',        NULL, NULL, NULL, NULL, NULL, NULL),
    (50007, 5000, N'Notes',        '00050007-0000-0000-0000-000000000000', N'Text',       NULL, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'NOTES',        NULL, NULL, NULL, NULL, NULL, NULL),
    (50008, 5000, N'DisplayLabel', '00050008-0000-0000-0000-000000000000', N'Text',         80, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'DISPLAYLABEL', NULL, NULL, NULL, NULL, NULL, N'Computed display label'),
    -- Category (5001): self-referencing FK ParentCategoryId -> Category.
    (50011, 5001, N'Id',               '00050011-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL,        N'ID',               NULL, NULL, NULL, NULL, NULL, NULL),
    (50012, 5001, N'Name',             '00050012-0000-0000-0000-000000000000', N'Text',        100, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL,        N'NAME',             NULL, NULL, NULL, NULL, NULL, NULL),
    (50013, 5001, N'ParentCategoryId', '00050013-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, N'Protect', N'PARENTCATEGORYID', NULL, N'bt44444444-4444-4444-4444-444444444444*bbbbbbbb-0000-0000-0000-000000000011', NULL, NULL, NULL, NULL),
    -- SalesOrder (5002): cross-module FK -> Customer (Cascade) + same-module FK -> Category (Protect).
    (50021, 5002, N'Id',         '00050021-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL,        N'ID',         NULL, NULL, NULL, NULL, NULL, NULL),
    (50022, 5002, N'CustomerId', '00050022-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, N'Delete',  N'CUSTOMERID', NULL, N'bt11111111-1111-1111-1111-111111111111*bbbbbbbb-0000-0000-0000-000000000001', NULL, NULL, NULL, N'FK to AppCore Customer'),
    (50023, 5002, N'CategoryId', '00050023-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, N'Protect', N'CATEGORYID', NULL, N'bt44444444-4444-4444-4444-444444444444*bbbbbbbb-0000-0000-0000-000000000011', NULL, NULL, NULL, NULL),
    (50024, 5002, N'OrderTotal', '00050024-0000-0000-0000-000000000000', N'Currency',   NULL, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL,        N'ORDERTOTAL', NULL, NULL, NULL, NULL, NULL, NULL),
    (50025, 5002, N'PlacedAt',   '00050025-0000-0000-0000-000000000000', N'DateTime',   NULL, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL,        N'PLACEDAT',   NULL, NULL, NULL, NULL, NULL, NULL),
    -- OrderLine (5003): COMPOSITE PRIMARY KEY (OrderId + LineNo); OrderId also FK -> SalesOrder.
    (50031, 5003, N'OrderId',  '00050031-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 0, 1, NULL, NULL, NULL, N'Delete', N'ORDERID',  NULL, N'bt44444444-4444-4444-4444-444444444444*bbbbbbbb-0000-0000-0000-000000000012', NULL, NULL, NULL, N'Composite-PK part + FK'),
    (50032, 5003, N'LineNo',   '00050032-0000-0000-0000-000000000000', N'Integer',    NULL, NULL, NULL, NULL, 1, 1, 0, 1, NULL, NULL, NULL, NULL,       N'LINENO',   NULL, NULL, NULL, NULL, NULL, N'Composite-PK part'),
    (50033, 5003, N'Quantity', '00050033-0000-0000-0000-000000000000', N'Integer',    NULL, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL,       N'QUANTITY', NULL, NULL, NULL, NULL, NULL, NULL),
    -- StockItem (6000): Integer, LongInteger->BIGINT, GUID, DateTimeOffset, Time, Float.
    (60001, 6000, N'Id',           '00060001-0000-0000-0000-000000000000', N'Identifier',     NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID',           NULL, NULL, NULL, NULL, NULL, NULL),
    (60002, 6000, N'ReorderLevel', '00060002-0000-0000-0000-000000000000', N'Integer',        NULL, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'REORDERLEVEL', NULL, NULL, NULL, NULL, NULL, NULL),
    (60003, 6000, N'WarehouseQty', '00060003-0000-0000-0000-000000000000', N'LongInteger',    NULL, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'WAREHOUSEQTY', NULL, NULL, NULL, NULL, NULL, NULL),
    (60004, 6000, N'PublicGuid',   '00060004-0000-0000-0000-000000000000', N'guid',           NULL, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'PUBLICGUID',   NULL, NULL, NULL, NULL, NULL, NULL),
    (60005, 6000, N'LastSyncedAt', '00060005-0000-0000-0000-000000000000', N'datetimeoffset', NULL, NULL,    7, NULL, 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'LASTSYNCEDAT', NULL, NULL, NULL, NULL, NULL, NULL),
    (60006, 6000, N'OpenTime',     '00060006-0000-0000-0000-000000000000', N'time',           NULL, NULL,    7, NULL, 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'OPENTIME',     NULL, NULL, NULL, NULL, NULL, NULL),
    (60007, 6000, N'AvgCost',      '00060007-0000-0000-0000-000000000000', N'Float',          NULL, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'AVGCOST',      NULL, NULL, NULL, NULL, NULL, NULL),
    -- Supplier (6001).
    (60011, 6001, N'Id',   '00060011-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID',   NULL, NULL, NULL, NULL, NULL, NULL),
    (60012, 6001, N'Name', '00060012-0000-0000-0000-000000000000', N'Text',        150, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'NAME', NULL, NULL, NULL, NULL, NULL, NULL),
    -- StockMovement (6002): FK -> StockItem (Protect) + nullable FK -> Supplier (SetNull, untrusted).
    (60021, 6002, N'Id',          '00060021-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL,         N'ID',          NULL, NULL, NULL, NULL, NULL, NULL),
    (60022, 6002, N'StockItemId', '00060022-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, N'Protect',  N'STOCKITEMID', NULL, N'bt55555555-5555-5555-5555-555555555555*bbbbbbbb-0000-0000-0000-000000000020', NULL, NULL, NULL, NULL),
    (60023, 6002, N'SupplierId',  '00060023-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, N'SetNull',  N'SUPPLIERID',  NULL, N'bt55555555-5555-5555-5555-555555555555*bbbbbbbb-0000-0000-0000-000000000021', NULL, NULL, NULL, NULL),
    (60024, 6002, N'MovedAt',     '00060024-0000-0000-0000-000000000000', N'DateTime',   NULL, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL,         N'MOVEDAT',     NULL, NULL, NULL, NULL, NULL, NULL),
    -- SyncLog (6100): external entity inside an Extension module.
    (61001, 6100, N'Id',      '00061001-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID',      NULL, NULL, NULL, NULL, NULL, NULL),
    (61002, 6100, N'Payload', '00061002-0000-0000-0000-000000000000', N'Text',       NULL, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'PAYLOAD', NULL, NULL, NULL, NULL, NULL, NULL),
    (61003, 6100, N'RawXml',  '00061003-0000-0000-0000-000000000000', N'xml',        NULL, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'RAWXML',  NULL, NULL, NULL, NULL, NULL, NULL),
    -- Country (7000), CurrencyCode (7001): static lookup entities.
    (70001, 7000, N'Id',   '00070001-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID',   NULL, NULL, NULL, NULL, NULL, NULL),
    (70002, 7000, N'Code', '00070002-0000-0000-0000-000000000000', N'Text',          3, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'CODE', NULL, NULL, NULL, NULL, NULL, NULL),
    (70003, 7000, N'Name', '00070003-0000-0000-0000-000000000000', N'Text',        100, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'NAME', NULL, NULL, NULL, NULL, NULL, NULL),
    (70011, 7001, N'Id',   '00070011-0000-0000-0000-000000000000', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID',   NULL, NULL, NULL, NULL, NULL, NULL),
    (70012, 7001, N'Code', '00070012-0000-0000-0000-000000000000', N'Text',          3, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'CODE', NULL, NULL, NULL, NULL, NULL, NULL);
GO

-- Physical tables for the comprehensive expansion. Ordered so FK
-- targets precede their referencers.

CREATE TABLE [dbo].[OSUSR_SAL_CATEGORY]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [NAME] NVARCHAR(100) NOT NULL,
    [PARENTCATEGORYID] INT NULL,
    CONSTRAINT [PK_Category_Id] PRIMARY KEY CLUSTERED ([ID]),
    -- Self-referencing FK (category tree).
    CONSTRAINT [FK_OSUSR_SAL_CATEGORY_PARENT] FOREIGN KEY ([PARENTCATEGORYID]) REFERENCES [dbo].[OSUSR_SAL_CATEGORY]([ID])
);
GO

CREATE TABLE [dbo].[OSUSR_SAL_PRODUCT]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [SKU] NVARCHAR(40) NOT NULL,
    [PRICE] DECIMAL(18,2) NOT NULL,
    [COST] DECIMAL(37,8) NULL,
    [LAUNCHDATE] DATETIME NULL,
    [PHOTO] VARBINARY(MAX) NULL,
    [NOTES] NVARCHAR(MAX) NULL,
    -- Persisted computed column.
    [DISPLAYLABEL] AS (CONCAT([SKU], N'-', CONVERT(NVARCHAR(20), [PRICE]))) PERSISTED,
    CONSTRAINT [PK_Product_Id] PRIMARY KEY CLUSTERED ([ID]),
    -- Column-level CHECK constraint.
    CONSTRAINT [CK_OSUSR_SAL_PRODUCT_PRICE] CHECK ([PRICE] >= 0)
);
GO

CREATE TABLE [dbo].[OSUSR_SAL_ORDER]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [CUSTOMERID] INT NOT NULL,
    [CATEGORYID] INT NOT NULL,
    [ORDERTOTAL] DECIMAL(37,8) NOT NULL,
    [PLACEDAT] DATETIME2(7) NOT NULL,
    CONSTRAINT [PK_SalesOrder_Id] PRIMARY KEY CLUSTERED ([ID]),
    -- Cross-module FK to AppCore Customer, ON DELETE CASCADE.
    CONSTRAINT [FK_OSUSR_SAL_ORDER_CUSTOMER] FOREIGN KEY ([CUSTOMERID]) REFERENCES [dbo].[OSUSR_ABC_CUSTOMER]([ID]) ON DELETE CASCADE,
    -- Same-module FK to Category, NO ACTION.
    CONSTRAINT [FK_OSUSR_SAL_ORDER_CATEGORY] FOREIGN KEY ([CATEGORYID]) REFERENCES [dbo].[OSUSR_SAL_CATEGORY]([ID])
);
GO

-- Unique COMPOSITE (multi-column) index — the one index combination the
-- fixture otherwise lacked. Complements the unique single-column
-- (IDX_CUSTOMER_EMAIL / IDX_BILLINGACCOUNT_ACCTNUM) and the non-unique
-- composite (IDX_CUSTOMER_NAME). Both key columns are NOT NULL.
CREATE UNIQUE NONCLUSTERED INDEX [IDX_ORDER_CUSTOMER_CATEGORY]
ON [dbo].[OSUSR_SAL_ORDER]([CUSTOMERID] ASC, [CATEGORYID] ASC);
GO

CREATE TABLE [dbo].[OSUSR_SAL_ORDERLINE]
(
    [ORDERID] INT NOT NULL,
    [LINENO] INT NOT NULL,
    [QUANTITY] INT NOT NULL,
    -- Composite PRIMARY KEY.
    CONSTRAINT [PK_OrderLine] PRIMARY KEY CLUSTERED ([ORDERID], [LINENO]),
    CONSTRAINT [FK_OSUSR_SAL_ORDERLINE_ORDER] FOREIGN KEY ([ORDERID]) REFERENCES [dbo].[OSUSR_SAL_ORDER]([ID]) ON DELETE CASCADE
);
GO

CREATE TABLE [dbo].[OSUSR_INV_SUPPLIER]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [NAME] NVARCHAR(150) NOT NULL,
    CONSTRAINT [PK_Supplier_Id] PRIMARY KEY CLUSTERED ([ID])
);
GO

CREATE TABLE [dbo].[OSUSR_INV_STOCKITEM]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [REORDERLEVEL] INT NOT NULL,
    [WAREHOUSEQTY] BIGINT NOT NULL,
    [PUBLICGUID] UNIQUEIDENTIFIER NULL,
    [LASTSYNCEDAT] DATETIMEOFFSET(7) NULL,
    [OPENTIME] DATETIME NULL,
    [AVGCOST] FLOAT NULL,
    CONSTRAINT [PK_StockItem_Id] PRIMARY KEY CLUSTERED ([ID])
);
GO

-- Covering index with an INCLUDE (non-key) column.
CREATE NONCLUSTERED INDEX [IDX_STOCKITEM_REORDER]
ON [dbo].[OSUSR_INV_STOCKITEM]([REORDERLEVEL] ASC)
INCLUDE ([WAREHOUSEQTY]);
GO

CREATE TABLE [dbo].[OSUSR_INV_MOVEMENT]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [STOCKITEMID] INT NOT NULL,
    [SUPPLIERID] INT NULL,
    [MOVEDAT] DATETIME2(7) NOT NULL,
    CONSTRAINT [PK_StockMovement_Id] PRIMARY KEY CLUSTERED ([ID]),
    -- FK with ON UPDATE CASCADE (exercises #FkReality.UpdateAction).
    CONSTRAINT [FK_OSUSR_INV_MOVEMENT_STOCKITEM] FOREIGN KEY ([STOCKITEMID]) REFERENCES [dbo].[OSUSR_INV_STOCKITEM]([ID]) ON UPDATE CASCADE
);
GO

-- Nullable FK added WITH NOCHECK so it lands untrusted (is_not_trusted=1),
-- ON DELETE SET NULL.
ALTER TABLE [dbo].[OSUSR_INV_MOVEMENT] WITH NOCHECK
    ADD CONSTRAINT [FK_OSUSR_INV_MOVEMENT_SUPPLIER]
    FOREIGN KEY ([SUPPLIERID]) REFERENCES [dbo].[OSUSR_INV_SUPPLIER]([ID]) ON DELETE SET NULL;
GO

-- Two triggers on one entity; the UPDATE trigger is disabled.
EXEC (N'CREATE TRIGGER [dbo].[TR_OSUSR_INV_MOVEMENT_INS] ON [dbo].[OSUSR_INV_MOVEMENT] AFTER INSERT AS BEGIN SET NOCOUNT ON; END');
GO
EXEC (N'CREATE TRIGGER [dbo].[TR_OSUSR_INV_MOVEMENT_UPD] ON [dbo].[OSUSR_INV_MOVEMENT] AFTER UPDATE AS BEGIN SET NOCOUNT ON; END');
GO
DISABLE TRIGGER [dbo].[TR_OSUSR_INV_MOVEMENT_UPD] ON [dbo].[OSUSR_INV_MOVEMENT];
GO

CREATE TABLE [dbo].[OSUSR_INT_SYNCLOG]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [PAYLOAD] NVARCHAR(MAX) NULL,
    [RAWXML] XML NULL,
    CONSTRAINT [PK_SyncLog_Id] PRIMARY KEY CLUSTERED ([ID])
);
GO

CREATE TABLE [dbo].[OSUSR_REF_COUNTRY]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [CODE] NVARCHAR(3) NOT NULL,
    [NAME] NVARCHAR(100) NOT NULL,
    CONSTRAINT [PK_Country_Id] PRIMARY KEY CLUSTERED ([ID])
);
GO

CREATE TABLE [dbo].[OSUSR_REF_CURRENCY]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [CODE] NVARCHAR(3) NOT NULL,
    CONSTRAINT [PK_Currency_Id] PRIMARY KEY CLUSTERED ([ID])
);
GO

