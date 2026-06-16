CREATE TABLE [dbo].[Country] (
    [Id]    INT            NOT NULL
        CONSTRAINT [PK_dbo_Country]
            PRIMARY KEY CLUSTERED,
    [Code]  NVARCHAR (2)   NOT NULL,
    [Label] NVARCHAR (100) NOT NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Country',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_KIND1:17:Country',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_ATTR1:110:Country.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Code',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Code'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_ATTR1:112:Country.Code',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Code'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Label',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Label'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_ATTR1:113:Country.Label',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Label'

