CREATE TABLE [dbo].[Tier] (
    [Id]   INT           IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_Tier]
            PRIMARY KEY CLUSTERED,
    [Name] NVARCHAR (40) NOT NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'Static lookup with an IDENTITY PK — the IDENTITY_INSERT bracket case.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Tier'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Tier',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Tier'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_KIND1:14:Tier',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Tier'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Tier',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Tier',
    @level2type = N'COLUMN', @level2name = N'Name'

