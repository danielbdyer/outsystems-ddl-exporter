CREATE TABLE [audit].[ChangeLog] (
    [Id]     INT       IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_audit_ChangeLog]
            PRIMARY KEY CLUSTERED,
    [At]     DATETIME2 NOT NULL,
    [UserId] INT       NOT NULL
        CONSTRAINT [FK_ChangeLog_User_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[User] ([Id])
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'ChangeLog',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.SsKey', @value = N'S9:GOLD_KIND1:19:ChangeLog',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'At',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog',
    @level2type = N'COLUMN', @level2name = N'At'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'Projection.LogicalName', @value = N'UserId',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog',
    @level2type = N'COLUMN', @level2name = N'UserId'

