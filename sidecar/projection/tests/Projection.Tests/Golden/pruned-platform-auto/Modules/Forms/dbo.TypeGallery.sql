CREATE TABLE [dbo].[TypeGallery] (
    [AlarmAt]     TIME             NULL,
    [Amount]      DECIMAL (18, 4)  NULL DEFAULT 0.0,
    [DueDate]     DATE             NULL,
    [ExternalKey] UNIQUEIDENTIFIER NULL,
    [Id]          INT              IDENTITY (1, 1) NOT NULL CONSTRAINT [PK_dbo_TypeGallery] PRIMARY KEY CLUSTERED,
    [IsActive]    BIT              NOT NULL CONSTRAINT [DF_TypeGallery_IsActive] DEFAULT 1,
    [Label]       NVARCHAR (100)   NOT NULL,
    [Notes]       NVARCHAR (2000)  NULL DEFAULT N'',
    [OccurredOn]  DATETIME2        NULL,
    [Payload]     VARBINARY (512)  NULL
)
EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'The type gallery: every primitive realization.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'TypeGallery', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:111:TypeGallery', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'AlarmAt', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery', @level2type = N'COLUMN', @level2name = N'AlarmAt'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Amount', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery', @level2type = N'COLUMN', @level2name = N'Amount'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'DueDate', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery', @level2type = N'COLUMN', @level2name = N'DueDate'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'ExternalKey', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery', @level2type = N'COLUMN', @level2name = N'ExternalKey'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery', @level2type = N'COLUMN', @level2name = N'Id'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'IsActive', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery', @level2type = N'COLUMN', @level2name = N'IsActive'
EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'A bounded text column.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery', @level2type = N'COLUMN', @level2name = N'Label'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Label', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery', @level2type = N'COLUMN', @level2name = N'Label'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Notes', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery', @level2type = N'COLUMN', @level2name = N'Notes'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'OccurredOn', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery', @level2type = N'COLUMN', @level2name = N'OccurredOn'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Payload', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TypeGallery', @level2type = N'COLUMN', @level2name = N'Payload'