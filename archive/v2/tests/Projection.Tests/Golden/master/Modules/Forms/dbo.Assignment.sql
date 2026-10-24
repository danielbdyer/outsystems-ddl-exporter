CREATE TABLE [dbo].[Assignment] (
    [ProjectId]  INT           NOT NULL,
    [ResourceId] INT           NOT NULL,
    [Role]       NVARCHAR (40) NULL,
    CONSTRAINT [PK_Assignment_ProjectId_ResourceId]
        PRIMARY KEY ([ProjectId], [ResourceId])
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Assignment',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:110:Assignment',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'ProjectId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'ProjectId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:120:Assignment.ProjectId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'ProjectId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'ResourceId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'ResourceId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:121:Assignment.ResourceId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'ResourceId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Role',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'Role';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:115:Assignment.Role',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'Role';

