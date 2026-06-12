CREATE TABLE [audit].[ChangeLog] (
    [At]     DATETIME2 NOT NULL,
    [Id]     INT       IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_audit_ChangeLog]
            PRIMARY KEY CLUSTERED,
    [UserId] INT       NOT NULL,
    CONSTRAINT [FK_ChangeLog_User_UserId]
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[User] ([Id])
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'ChangeLog',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:19:ChangeLog',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'At',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog',
    @level2type = N'COLUMN', @level2name = N'At'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'UserId',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog',
    @level2type = N'COLUMN', @level2name = N'UserId'

GO

CREATE TABLE [dbo].[Country] (
    [Code]  NVARCHAR (2)   NOT NULL,
    [Id]    INT            NOT NULL
        CONSTRAINT [PK_dbo_Country]
            PRIMARY KEY CLUSTERED,
    [Label] NVARCHAR (100) NOT NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Country',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:17:Country',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Code',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Code'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Label',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Label'

GO

CREATE TABLE [dbo].[Customer] (
    [Id]   INT            IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_Customer]
            PRIMARY KEY CLUSTERED,
    [Name] NVARCHAR (120) NOT NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Customer',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Customer'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:18:Customer',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Customer'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Customer',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Customer',
    @level2type = N'COLUMN', @level2name = N'Name'

GO

CREATE TABLE [dbo].[Guarded] (
    [Id]  INT IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_Guarded]
            PRIMARY KEY CLUSTERED,
    [Qty] INT NOT NULL,
    CONSTRAINT [CK_Guarded_Qty]
        CHECK (([QTY] >= (0))),
    CHECK (([QTY] <= (1000000)))
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Guarded',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Guarded'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:17:Guarded',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Guarded'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Guarded',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Qty',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Guarded',
    @level2type = N'COLUMN', @level2name = N'Qty'

GO

-- Trigger: TRG_Guarded_Audit (disabled: false)

GO

CREATE TRIGGER [dbo].[TRG_Guarded_Audit]
    ON [dbo].[GOLD_GUARDED]
    AFTER INSERT
    AS BEGIN
           SET NOCOUNT ON;
       END

GO

CREATE TABLE [dbo].[Heap] (
    [LoggedAt] DATETIME2      NOT NULL,
    [Message]  NVARCHAR (500) NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Heap',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Heap'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:14:Heap',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Heap'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'LoggedAt',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Heap',
    @level2type = N'COLUMN', @level2name = N'LoggedAt'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Message',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Heap',
    @level2type = N'COLUMN', @level2name = N'Message'

GO

CREATE TABLE [dbo].[IndexGallery] (
    [Alpha] NVARCHAR (50) NULL,
    [Beta]  INT           NULL,
    [Gamma] INT           NULL,
    [Id]    INT           IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_IndexGallery]
            PRIMARY KEY CLUSTERED
)

GO

CREATE INDEX [IX_IndexGallery_Alpha_Covering]
    ON [dbo].[IndexGallery]([Alpha])
    INCLUDE([Gamma])

GO

CREATE INDEX [IX_IndexGallery_Beta_Desc]
    ON [dbo].[IndexGallery]([Beta] DESC)

GO

CREATE INDEX [IX_IndexGallery_Alpha_Disabled]
    ON [dbo].[IndexGallery]([Alpha])

GO

CREATE INDEX [IX_IndexGallery_Beta_Filtered]
    ON [dbo].[IndexGallery]([Beta]) WHERE ([BETA] IS NOT NULL)

GO

CREATE INDEX [IX_IndexGallery_Alpha]
    ON [dbo].[IndexGallery]([Alpha])

GO

CREATE UNIQUE INDEX [UIX_IndexGallery_Gamma_Tuned]
    ON [dbo].[IndexGallery]([Gamma]) WITH (FILLFACTOR = 80, PAD_INDEX = ON, IGNORE_DUP_KEY = ON, DATA_COMPRESSION = PAGE)

GO

CREATE UNIQUE INDEX [UIX_IndexGallery_Beta]
    ON [dbo].[IndexGallery]([Beta])

GO

ALTER INDEX [IX_IndexGallery_Alpha_Disabled]
    ON [dbo].[IndexGallery] DISABLE

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'IndexGallery',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'IndexGallery'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:112:IndexGallery',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'IndexGallery'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Alpha',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'IndexGallery',
    @level2type = N'COLUMN', @level2name = N'Alpha'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Beta',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'IndexGallery',
    @level2type = N'COLUMN', @level2name = N'Beta'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Gamma',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'IndexGallery',
    @level2type = N'COLUMN', @level2name = N'Gamma'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'IndexGallery',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'Descending scan support.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'IndexGallery',
    @level2type = N'INDEX', @level2name = N'IX_IndexGallery_Beta_Desc'

GO

CREATE TABLE [dbo].[Order] (
    [AltCustomerId] INT NULL,
    [CustomerId]    INT NOT NULL,
    [Id]            INT IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_Order]
            PRIMARY KEY CLUSTERED,
    CONSTRAINT [FK_Order_Customer_AltCustomerId]
        FOREIGN KEY ([AltCustomerId]) REFERENCES [dbo].[Customer] ([Id])
            ON DELETE SET NULL
            ON UPDATE NO ACTION,
    CONSTRAINT [FK_Order_Customer_CustomerId]
        FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])
            ON DELETE CASCADE
            ON UPDATE NO ACTION
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Order',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Order'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:15:Order',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Order'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'AltCustomerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Order',
    @level2type = N'COLUMN', @level2name = N'AltCustomerId'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'CustomerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Order',
    @level2type = N'COLUMN', @level2name = N'CustomerId'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Order',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

CREATE TABLE [dbo].[RegionA] (
    [Id]        INT           NOT NULL
        CONSTRAINT [PK_dbo_RegionA]
            PRIMARY KEY CLUSTERED,
    [Name]      NVARCHAR (60) NOT NULL,
    [PartnerId] INT           NULL,
    CONSTRAINT [FK_RegionA_RegionB_PartnerId]
        FOREIGN KEY ([PartnerId]) REFERENCES [dbo].[RegionB] ([Id])
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'RegionA',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:17:RegionA',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'Name'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'PartnerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'PartnerId'

GO

CREATE TABLE [dbo].[RegionB] (
    [Id]        INT           NOT NULL
        CONSTRAINT [PK_dbo_RegionB]
            PRIMARY KEY CLUSTERED,
    [Name]      NVARCHAR (60) NOT NULL,
    [PartnerId] INT           NULL,
    CONSTRAINT [FK_RegionB_RegionA_PartnerId]
        FOREIGN KEY ([PartnerId]) REFERENCES [dbo].[RegionA] ([Id])
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'RegionB',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:17:RegionB',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB',
    @level2type = N'COLUMN', @level2name = N'Name'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'PartnerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB',
    @level2type = N'COLUMN', @level2name = N'PartnerId'

GO

CREATE TABLE [dbo].[ScopedLookup] (
    [Id]       INT           NOT NULL
        CONSTRAINT [PK_dbo_ScopedLookup]
            PRIMARY KEY CLUSTERED,
    [TenantId] INT           NOT NULL,
    [Value]    NVARCHAR (80) NOT NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'ScopedLookup',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScopedLookup'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:112:ScopedLookup',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScopedLookup'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScopedLookup',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'TenantId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScopedLookup',
    @level2type = N'COLUMN', @level2name = N'TenantId'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Value',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScopedLookup',
    @level2type = N'COLUMN', @level2name = N'Value'

GO

CREATE TABLE [dbo].[Task] (
    [CreatedBy] INT            NOT NULL,
    [Id]        INT            IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_Task]
            PRIMARY KEY CLUSTERED,
    [Title]     NVARCHAR (200) NOT NULL,
    [UpdatedBy] INT            NULL,
    CONSTRAINT [FK_Task_User_CreatedBy]
        FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[User] ([Id])
            ON DELETE NO ACTION
            ON UPDATE CASCADE,
    CONSTRAINT [FK_Task_User_UpdatedBy]
        FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[User] ([Id])
)

GO

ALTER TABLE [dbo].[Task] NOCHECK CONSTRAINT [FK_Task_User_UpdatedBy]

GO

ALTER TABLE [dbo].[Task] WITH NOCHECK CHECK CONSTRAINT [FK_Task_User_UpdatedBy]

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Task',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Task'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:14:Task',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Task'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'CreatedBy',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Task',
    @level2type = N'COLUMN', @level2name = N'CreatedBy'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Task',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Title',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Task',
    @level2type = N'COLUMN', @level2name = N'Title'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'UpdatedBy',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Task',
    @level2type = N'COLUMN', @level2name = N'UpdatedBy'

GO

CREATE TABLE [dbo].[TypeGallery] (
    [AlarmAt]     TIME             NULL,
    [Amount]      DECIMAL (18, 4)  NULL
        DEFAULT 0.0,
    [DueDate]     DATE             NULL,
    [ExternalKey] UNIQUEIDENTIFIER NULL,
    [Id]          INT              IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_TypeGallery]
            PRIMARY KEY CLUSTERED,
    [IsActive]    BIT              NOT NULL
        CONSTRAINT [DF_TypeGallery_IsActive] DEFAULT 1,
    [Label]       NVARCHAR (100)   NOT NULL,
    [Notes]       NVARCHAR (2000)  NULL
        DEFAULT N'',
    [OccurredOn]  DATETIME2        NULL,
    [Payload]     VARBINARY (512)  NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'The type gallery: every primitive realization.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'TypeGallery',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:111:TypeGallery',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'AlarmAt',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery',
    @level2type = N'COLUMN', @level2name = N'AlarmAt'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Amount',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery',
    @level2type = N'COLUMN', @level2name = N'Amount'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'DueDate',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery',
    @level2type = N'COLUMN', @level2name = N'DueDate'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'ExternalKey',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery',
    @level2type = N'COLUMN', @level2name = N'ExternalKey'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'IsActive',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery',
    @level2type = N'COLUMN', @level2name = N'IsActive'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'A bounded text column.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery',
    @level2type = N'COLUMN', @level2name = N'Label'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Label',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery',
    @level2type = N'COLUMN', @level2name = N'Label'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Notes',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery',
    @level2type = N'COLUMN', @level2name = N'Notes'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'OccurredOn',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery',
    @level2type = N'COLUMN', @level2name = N'OccurredOn'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Payload',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TypeGallery',
    @level2type = N'COLUMN', @level2name = N'Payload'

GO

CREATE TABLE [dbo].[User] (
    [Email] NVARCHAR (250) NOT NULL,
    [Id]    INT            IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_User]
            PRIMARY KEY CLUSTERED
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'The platform user kind (pure reference target).',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'User',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:14:User',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Email',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User',
    @level2type = N'COLUMN', @level2name = N'Email'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User',
    @level2type = N'COLUMN', @level2name = N'Id'

GO


