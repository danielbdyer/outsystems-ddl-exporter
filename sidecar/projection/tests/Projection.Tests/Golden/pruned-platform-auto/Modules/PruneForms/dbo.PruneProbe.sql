CREATE TABLE [dbo].[PruneProbe] (
    [Code] NVARCHAR (20) NOT NULL,
    [Id]   INT           IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_PruneProbe]
            PRIMARY KEY CLUSTERED
)

GO

CREATE INDEX [IX_PruneProbe_Code]
    ON [dbo].[PruneProbe]([Code])

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'Prune one-off probe: a platform-auto index beside a normal one.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'PruneProbe'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'PruneProbe',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'PruneProbe'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_KIND1:110:PruneProbe',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'PruneProbe'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Code',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'PruneProbe',
    @level2type = N'COLUMN', @level2name = N'Code'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'PruneProbe',
    @level2type = N'COLUMN', @level2name = N'Id'

