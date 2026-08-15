-- The data-sink chapter's lifecycle seed (S1, 2026-08-15; CHAPTER_SINK_OPEN.md).
-- Authored fresh for V2 (no V1 donor — no ADMIRE row). A SEPARATE resource from
-- ossys-edge-case.seed.sql (14 consumers pin that seed's exact counts; this one
-- is never combined with it — each runs on its own bootstrapped database).
--
-- The estate shape this seed exists to carry — the post-cutover lifecycle the
-- edge-case seed deliberately lacks:
--   * a TOMBSTONED entity (Is_Active=0) whose physical table survives with
--     data-bearing columns (Invoice — the operator's original incident;
--     platform behavior: deletion is a metadata event, DbCleaner alone drops)
--   * a tombstone ↔ extension RE-REGISTRATION pair claiming ONE physical
--     table under two SS_Keys (Shipment — the K9 correspondence case; the
--     adjudication's Adopted case: active-extension outranks tombstone)
--   * two ACTIVE claims on one physical table (Carrier — the Contested
--     refusal case; espace-owned vs extension-owned, distinct SS_Keys, so
--     the inactive-shadow normalization does NOT collapse them)
--   * an ORPHAN physical table with no ossys_Entity row at all
--     (OSUSR_FUL_ARCHIVE — the K6 present-but-unclaimed case; invisible to
--     the OSSYS read by construction, visible only to the residue sweep)
--   * one healthy active entity (Order) as the unremarkable baseline.

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'dbo')
BEGIN
    EXEC('CREATE SCHEMA [dbo]');
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
    [Type] NVARCHAR(200) NULL,
    [Legacy_Type] NVARCHAR(200) NULL,
    [Decimals] INT NULL,
    [Original_Type] NVARCHAR(200) NULL,
    [Description] NVARCHAR(MAX) NULL,
    [Order_Num] INT NULL,
    CONSTRAINT FK_ossys_Entity_Attr_Entity FOREIGN KEY ([Entity_Id]) REFERENCES [dbo].[ossys_Entity]([Id])
);
GO

-- Espace 800: the pre-cutover application module. Espace 900: the
-- Integration Studio extension module (the case-insensitive 'Extension'
-- IS-marker the Origin three-way discriminates on).
INSERT INTO [dbo].[ossys_Espace] ([Id], [Name], [Is_System], [Is_Active], [EspaceKind], [SS_Key]) VALUES
    (800, N'Fulfillment', 0, 1, N'eSpace', '4f000000-0000-0000-0000-000000000800'),
    (900, N'FulfillmentExtension', 0, 1, N'Extension', '4f000000-0000-0000-0000-000000000900');
GO

INSERT INTO [dbo].[ossys_Entity]
    ([Id], [Name], [Physical_Table_Name], [Espace_Id], [Is_Active], [Is_System], [Is_External], [Data_Kind], [PrimaryKey_SS_Key], [SS_Key], [Description])
VALUES
    -- the healthy baseline
    (8000, N'Order',    N'OSUSR_FUL_ORDER',    800, 1, 0, 0, N'entity', '5a000000-0000-0000-0000-000000008000', '5e000000-0000-0000-0000-000000008000', N'Active espace-owned baseline'),
    -- the tombstone-only entity: deleted from the module, table intact
    (8001, N'Invoice',  N'OSUSR_FUL_INVOICE',  800, 0, 0, 0, N'entity', '5a000000-0000-0000-0000-000000008001', '5e000000-0000-0000-0000-000000008001', N'Tombstoned: deleted from the eSpace; the physical table survives'),
    -- the re-registration pair: tombstone (Native) + active extension
    -- (ExternalIndirect) claiming ONE physical table under two SS_Keys
    (8002, N'Shipment', N'OSUSR_FUL_SHIPMENT', 800, 0, 0, 0, N'entity', '5a000000-0000-0000-0000-000000008002', '5e000000-0000-0000-0000-000000008002', N'Tombstoned half of the cutover pair'),
    (9002, N'Shipment', N'OSUSR_FUL_SHIPMENT', 900, 1, 0, 1, N'entity', '5a000000-0000-0000-0000-000000009002', '5e000000-0000-0000-0000-000000009002', N'Extension re-registration of Shipment'),
    -- the contested pair: BOTH active, one table, distinct SS_Keys
    (8003, N'Carrier',  N'OSUSR_FUL_CARRIER',  800, 1, 0, 0, N'entity', '5a000000-0000-0000-0000-000000008003', '5e000000-0000-0000-0000-000000008003', N'Espace-owned active claim'),
    (9003, N'Carrier',  N'OSUSR_FUL_CARRIER',  900, 1, 0, 1, N'entity', '5a000000-0000-0000-0000-000000009003', '5e000000-0000-0000-0000-000000009003', N'Extension-owned active claim');
GO

-- Attribute rows. Tombstoned entities carry tombstoned attributes
-- (Is_Active=0 rides the entity's deletion — the honest worst case the
-- recoverability path must survive under OnlyActiveAttributes=0).
-- Order_Num values are deliberately non-alphabetical (WP8 / NM-72).
INSERT INTO [dbo].[ossys_Entity_Attr]
    ([Id], [Entity_Id], [Name], [SS_Key], [Data_Type], [Length], [Precision], [Scale], [Default_Value], [Is_Mandatory], [Is_Active], [Is_AutoNumber], [Is_Identifier], [Referenced_Entity_Id], [Original_Name], [External_Column_Type], [Delete_Rule], [Physical_Column_Name], [Database_Name], [Type], [Legacy_Type], [Decimals], [Original_Type], [Description], [Order_Num])
VALUES
    -- Order (active)
    (80001, 8000, N'Id',           '5c000000-0000-0000-0000-000000080001', N'Identifier', NULL, NULL, NULL, NULL,           1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID',           NULL, NULL, NULL, NULL, NULL, NULL, 1),
    (80002, 8000, N'OrderedOn',    '5c000000-0000-0000-0000-000000080002', N'DateTime',   NULL, NULL, NULL, N'getutcdate()',1, 1, 0, 0, NULL, NULL, NULL, NULL, N'ORDEREDON',    NULL, NULL, NULL, NULL, NULL, NULL, 20),
    (80003, 8000, N'Status',       '5c000000-0000-0000-0000-000000080003', N'Text',       50,   NULL, NULL, NULL,           0, 1, 0, 0, NULL, NULL, NULL, NULL, N'STATUS',       NULL, NULL, NULL, NULL, NULL, NULL, 10),
    -- Invoice (tombstoned with its attributes)
    (80011, 8001, N'Id',           '5c000000-0000-0000-0000-000000080011', N'Identifier', NULL, NULL, NULL, NULL,           1, 0, 1, 1, NULL, NULL, NULL, NULL, N'ID',           NULL, NULL, NULL, NULL, NULL, NULL, 1),
    (80012, 8001, N'Total',        '5c000000-0000-0000-0000-000000080012', N'Currency',   NULL, NULL, NULL, NULL,           1, 0, 0, 0, NULL, NULL, NULL, NULL, N'TOTAL',        NULL, NULL, NULL, NULL, NULL, NULL, 20),
    (80013, 8001, N'IssuedOn',     '5c000000-0000-0000-0000-000000080013', N'DateTime',   NULL, NULL, NULL, NULL,           1, 0, 0, 0, NULL, NULL, NULL, NULL, N'ISSUEDON',     NULL, NULL, NULL, NULL, NULL, NULL, 10),
    -- Shipment tombstone half
    (80021, 8002, N'Id',           '5c000000-0000-0000-0000-000000080021', N'Identifier', NULL, NULL, NULL, NULL,           1, 0, 1, 1, NULL, NULL, NULL, NULL, N'ID',           NULL, NULL, NULL, NULL, NULL, NULL, 1),
    (80022, 8002, N'TrackingCode', '5c000000-0000-0000-0000-000000080022', N'Text',       100,  NULL, NULL, NULL,           1, 0, 0, 0, NULL, NULL, NULL, NULL, N'TRACKINGCODE', NULL, NULL, NULL, NULL, NULL, NULL, 10),
    -- Shipment extension half (external column types per the IS import)
    (90021, 9002, N'Id',           '5c000000-0000-0000-0000-000000090021', N'Identifier', NULL, NULL, NULL, NULL,           1, 1, 1, 1, NULL, NULL, N'int',           NULL, N'ID',           NULL, NULL, NULL, NULL, NULL, NULL, 1),
    (90022, 9002, N'TrackingCode', '5c000000-0000-0000-0000-000000090022', N'Text',       100,  NULL, NULL, NULL,           1, 1, 0, 0, NULL, NULL, N'nvarchar(100)', NULL, N'TRACKINGCODE', NULL, NULL, NULL, NULL, NULL, NULL, 10),
    -- Carrier espace-owned active claim
    (80031, 8003, N'Id',           '5c000000-0000-0000-0000-000000080031', N'Identifier', NULL, NULL, NULL, NULL,           1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID',           NULL, NULL, NULL, NULL, NULL, NULL, 1),
    (80032, 8003, N'CarrierName',  '5c000000-0000-0000-0000-000000080032', N'Text',       200,  NULL, NULL, NULL,           1, 1, 0, 0, NULL, NULL, NULL, NULL, N'CARRIERNAME',  NULL, NULL, NULL, NULL, NULL, NULL, 10),
    -- Carrier extension-owned active claim
    (90031, 9003, N'Id',           '5c000000-0000-0000-0000-000000090031', N'Identifier', NULL, NULL, NULL, NULL,           1, 1, 1, 1, NULL, NULL, N'int',           NULL, N'ID',           NULL, NULL, NULL, NULL, NULL, NULL, 1),
    (90032, 9003, N'CarrierName',  '5c000000-0000-0000-0000-000000090032', N'Text',       200,  NULL, NULL, NULL,           1, 1, 0, 0, NULL, NULL, N'nvarchar(200)', NULL, N'CARRIERNAME',  NULL, NULL, NULL, NULL, NULL, NULL, 10);
GO

-- Physical reality. Every metadata claim above resolves to a real table;
-- OSUSR_FUL_ARCHIVE has NO metadata row at all (the orphan).
CREATE TABLE [dbo].[OSUSR_FUL_ORDER]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [ORDEREDON] DATETIME2(7) NOT NULL CONSTRAINT [DF_OSUSR_FUL_ORDER_ORDEREDON] DEFAULT getutcdate(),
    [STATUS] NVARCHAR(50) NULL,
    CONSTRAINT [PK_Order_Id] PRIMARY KEY CLUSTERED ([ID])
);
GO

CREATE TABLE [dbo].[OSUSR_FUL_INVOICE]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [TOTAL] DECIMAL(37,8) NOT NULL,
    [ISSUEDON] DATETIME2(7) NOT NULL,
    CONSTRAINT [PK_Invoice_Id] PRIMARY KEY CLUSTERED ([ID])
);
GO

-- The surviving table's data proves recoverability is about more than DDL.
INSERT INTO [dbo].[OSUSR_FUL_INVOICE] ([TOTAL], [ISSUEDON]) VALUES
    (199.5, '2026-07-01T09:30:00'),
    (42.0,  '2026-07-02T14:00:00');
GO

CREATE TABLE [dbo].[OSUSR_FUL_SHIPMENT]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [TRACKINGCODE] NVARCHAR(100) NOT NULL,
    CONSTRAINT [PK_Shipment_Id] PRIMARY KEY CLUSTERED ([ID])
);
GO

CREATE TABLE [dbo].[OSUSR_FUL_CARRIER]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [CARRIERNAME] NVARCHAR(200) NOT NULL,
    CONSTRAINT [PK_Carrier_Id] PRIMARY KEY CLUSTERED ([ID])
);
GO

CREATE TABLE [dbo].[OSUSR_FUL_ARCHIVE]
(
    [ID] INT IDENTITY(1,1) NOT NULL,
    [PAYLOAD] NVARCHAR(MAX) NULL,
    [ARCHIVEDON] DATETIME2(7) NOT NULL,
    CONSTRAINT [PK_Archive_Id] PRIMARY KEY CLUSTERED ([ID])
);
GO
