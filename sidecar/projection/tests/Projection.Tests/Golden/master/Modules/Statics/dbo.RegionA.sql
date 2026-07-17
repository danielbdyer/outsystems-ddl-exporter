CREATE TABLE [dbo].[RegionA] (
    [Id]        INT           NOT NULL
        CONSTRAINT [PK_RegionA_Id]
            PRIMARY KEY CLUSTERED,
    [Name]      NVARCHAR (60) NOT NULL,
    [PartnerId] INT           NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'RegionA',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_KIND1:17:RegionA',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_ATTR1:110:RegionA.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'Name'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_ATTR1:112:RegionA.Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'Name'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'PartnerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'PartnerId'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_ATTR1:117:RegionA.PartnerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'PartnerId'

