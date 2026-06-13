CREATE TABLE [dbo].[Assignment] (
    [ProjectId]  INT           NOT NULL,
    [ResourceId] INT           NOT NULL,
    [Role]       NVARCHAR (40) NULL,
    CONSTRAINT [PK_dbo_Assignment]
        PRIMARY KEY ([ProjectId], [ResourceId])
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Assignment',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_KIND1:110:Assignment',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'ProjectId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'ProjectId'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'ResourceId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'ResourceId'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Role',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'Role'

