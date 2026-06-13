CREATE TABLE [dbo].[RegionB] (
    [Id]        INT           NOT NULL
        CONSTRAINT [PK_dbo_RegionB]
            PRIMARY KEY CLUSTERED,
    [Name]      NVARCHAR (60) NOT NULL,
    [PartnerId] INT           NULL
        CONSTRAINT [FK_RegionB_RegionA_PartnerId]
            FOREIGN KEY ([PartnerId]) REFERENCES [dbo].[RegionA] ([Id])
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'RegionB',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_KIND1:17:RegionB',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB',
    @level2type = N'COLUMN', @level2name = N'Name'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'PartnerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB',
    @level2type = N'COLUMN', @level2name = N'PartnerId'

