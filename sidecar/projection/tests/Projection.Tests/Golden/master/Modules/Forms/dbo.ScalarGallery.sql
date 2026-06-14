CREATE TABLE [dbo].[ScalarGallery] (
    [Id]          INT              IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_ScalarGallery]
            PRIMARY KEY CLUSTERED,
    [AlarmAt]     TIME             NULL
        DEFAULT '08:30:00',
    [Amount]      DECIMAL (18, 4)  NULL
        DEFAULT 3.1400
        CHECK (([Amount] <= (1000000.0000))),
    [Code]        NVARCHAR (20)    NOT NULL
        CONSTRAINT [DF_ScalarGallery_Code] DEFAULT N'Pending',
    [DueDate]     DATE             NULL
        DEFAULT '2020-01-01',
    [ExternalKey] UNIQUEIDENTIFIER NULL
        DEFAULT '00000000-0000-0000-0000-000000000000',
    [FreeText]    NVARCHAR (50)    NULL,
    [IsActive]    BIT              NOT NULL
        CONSTRAINT [DF_ScalarGallery_IsActive] DEFAULT 1,
    [Notes]       NVARCHAR (2000)  NULL
        DEFAULT N'',
    [OccurredOn]  DATETIME2        NULL
        DEFAULT '2020-01-01 00:00:00',
    [Payload]     VARBINARY (512)  NULL
        DEFAULT 0x00,
    [Tally]       INT              NULL
        DEFAULT 42
        CONSTRAINT [CK_ScalarGallery_Tally] CHECK (([Tally] >= (0))),
    CONSTRAINT [CK_ScalarGallery_TallyWithinAmount]
        CHECK (([Tally] <= [Amount]))
)

GO

CREATE INDEX [IX_ScalarGallery_Code_Covering]
    ON [dbo].[ScalarGallery]([Code])
    INCLUDE([Amount])

GO

CREATE INDEX [IX_ScalarGallery_Tally_Desc]
    ON [dbo].[ScalarGallery]([Tally] DESC)

GO

CREATE INDEX [IX_ScalarGallery_Code_Disabled]
    ON [dbo].[ScalarGallery]([Code])

GO

CREATE INDEX [IX_ScalarGallery_Tally_Filtered]
    ON [dbo].[ScalarGallery]([Tally]) WHERE ([Tally] IS NOT NULL)

GO

CREATE INDEX [IX_ScalarGallery_Code]
    ON [dbo].[ScalarGallery]([Code])

GO

CREATE INDEX [OSIDX_GOLD_SCALAR_GALLERY_TALLY]
    ON [dbo].[ScalarGallery]([Tally])

GO

CREATE UNIQUE INDEX [UIX_ScalarGallery_Amount_Tuned]
    ON [dbo].[ScalarGallery]([Amount]) WITH (FILLFACTOR = 80, PAD_INDEX = ON, IGNORE_DUP_KEY = ON, DATA_COMPRESSION = PAGE)

GO

CREATE UNIQUE INDEX [UIX_ScalarGallery_Code]
    ON [dbo].[ScalarGallery]([Code])

GO

ALTER INDEX [IX_ScalarGallery_Code_Disabled]
    ON [dbo].[ScalarGallery] DISABLE

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'The scalar gallery: every primitive realization and every DEFAULT-able literal.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'ScalarGallery',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_KIND1:113:ScalarGallery',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'AlarmAt',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'AlarmAt'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Amount',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Amount'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'Workflow code; defaults to Pending.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Code'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Code',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Code'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'DueDate',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'DueDate'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'ExternalKey',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'ExternalKey'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'No default; the contrast column.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'FreeText'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'FreeText',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'FreeText'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'IsActive',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'IsActive'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Notes',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Notes'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'OccurredOn',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'OccurredOn'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Payload',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Payload'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Tally',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Tally'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'Descending scan support.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'INDEX', @level2name = N'IX_ScalarGallery_Tally_Desc'

GO

-- Trigger: TRG_ScalarGallery_Audit (disabled: false)

GO

CREATE TRIGGER [dbo].[TRG_ScalarGallery_Audit]
    ON [dbo].[GOLD_SCALAR_GALLERY]
    AFTER INSERT
    AS BEGIN
           SET NOCOUNT ON;
       END

