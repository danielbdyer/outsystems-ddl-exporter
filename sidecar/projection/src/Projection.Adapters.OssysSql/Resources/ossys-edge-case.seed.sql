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

INSERT INTO [dbo].[ossys_Entity_Attr]
    ([Id], [Entity_Id], [Name], [SS_Key], [Data_Type], [Length], [Precision], [Scale], [Default_Value], [Is_Mandatory], [Is_Active], [Is_AutoNumber], [Is_Identifier], [Referenced_Entity_Id], [Original_Name], [External_Column_Type], [Delete_Rule], [Physical_Column_Name], [Database_Name], [Type], [Legacy_Type], [Decimals], [Original_Type], [Description])
VALUES
    (10001, 1000, N'Id', 'cccccccc-0000-0000-0000-000000000001', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID', NULL, NULL, NULL, NULL, NULL, N'Customer identifier'),
    (10002, 1000, N'Email', 'cccccccc-0000-0000-0000-000000000002', N'Text', 255, NULL, NULL, NULL, 1, 1, 0, 0, NULL, N'EmailAddress', NULL, NULL, N'EMAIL', NULL, NULL, NULL, NULL, NULL, N'Customer email'),
    (10003, 1000, N'FirstName', 'cccccccc-0000-0000-0000-000000000003', N'Text', 100, NULL, NULL, N'''''' , 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'FIRSTNAME', NULL, NULL, NULL, NULL, NULL, N'Customer first name'),
    (10004, 1000, N'LastName', 'cccccccc-0000-0000-0000-000000000004', N'Text', 100, NULL, NULL, N'''''' , 0, 1, 0, 0, NULL, NULL, NULL, NULL, N'LASTNAME', NULL, NULL, NULL, NULL, NULL, N'Customer last name'),
    -- CityId: same-module FK (AppCore Customer -> AppCore City). The
    -- `Type` column carries the `bt<EspaceSsKey>*<EntitySsKey>` binding
    -- code and `Referenced_Entity_Id` is NULL, so the rowset CTE must
    -- resolve the target by parsing the bt-code (the real OSSYS shape).
    -- `Data_Type` keeps the resolved scalar (Identifier -> BIGINT).
    (10005, 1000, N'CityId', 'cccccccc-0000-0000-0000-000000000005', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, N'Protect', N'CITYID', NULL, N'bt11111111-1111-1111-1111-111111111111*bbbbbbbb-0000-0000-0000-000000000002', NULL, NULL, NULL, N'FK to City'),
    (10006, 1000, N'LegacyCode', 'cccccccc-0000-0000-0000-000000000006', N'Text', 50, NULL, NULL, NULL, 0, 0, 0, 0, NULL, NULL, NULL, NULL, N'LEGACYCODE', NULL, NULL, NULL, NULL, NULL, NULL),
    (20011, 2001, N'Id', 'dddddddd-0000-0000-0000-000000000001', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID', NULL, NULL, NULL, NULL, NULL, NULL),
    (20012, 2001, N'Name', 'dddddddd-0000-0000-0000-000000000002', N'Text', 200, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'NAME', NULL, NULL, NULL, NULL, NULL, NULL),
    (20013, 2001, N'IsActive', 'dddddddd-0000-0000-0000-000000000003', N'Boolean', NULL, NULL, NULL, N'1', 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'ISACTIVE', NULL, NULL, NULL, NULL, NULL, NULL),
    (30021, 2002, N'Id', 'eeeeeeee-0000-0000-0000-000000000001', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, N'int', NULL, N'ID', NULL, NULL, NULL, NULL, NULL, NULL),
    (30022, 2002, N'AccountNumber', 'eeeeeeee-0000-0000-0000-000000000002', N'Text', 50, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, N'varchar(50)', NULL, N'ACCOUNTNUMBER', NULL, NULL, NULL, NULL, NULL, NULL),
    (30023, 2002, N'ExtRef', 'eeeeeeee-0000-0000-0000-000000000003', N'Text', 50, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, N'varchar(50)', NULL, N'EXTREF', NULL, NULL, NULL, NULL, NULL, NULL),
    (40031, 4000, N'Id', 'ffffffff-0000-0000-0000-000000000001', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID', NULL, NULL, NULL, NULL, NULL, NULL),
    -- TriggeredByUserId: cross-module FK (Ops JobRun -> SystemUsers User).
    -- The `Type` column's bt-code names a DIFFERENT espace (SystemUsers,
    -- 300) than the source (Ops, 200) with a NULL Referenced_Entity_Id,
    -- so resolving it exercises the cross-module reference path end-to-end.
    (40032, 4000, N'TriggeredByUserId', 'ffffffff-0000-0000-0000-000000000002', N'Identifier', NULL, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, N'Ignore', N'TRIGGEREDBYUSERID', NULL, N'bt33333333-3333-3333-3333-333333333333*bbbbbbbb-0000-0000-0000-000000000005', NULL, NULL, NULL, NULL),
    (40033, 4000, N'CreatedOn', 'ffffffff-0000-0000-0000-000000000003', N'DateTime', NULL, NULL, NULL, N'getutcdate()', 1, 1, 0, 0, NULL, NULL, NULL, NULL, N'CREATEDON', NULL, NULL, NULL, NULL, NULL, NULL),
    (50041, 3001, N'Id', '99999999-0000-0000-0000-000000000001', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID', NULL, NULL, NULL, NULL, NULL, NULL);
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

