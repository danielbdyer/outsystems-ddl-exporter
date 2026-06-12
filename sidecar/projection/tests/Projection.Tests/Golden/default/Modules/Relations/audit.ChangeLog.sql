CREATE TABLE [audit].[ChangeLog] (
    [At]     DATETIME2 NOT NULL,
    [Id]     INT       IDENTITY (1, 1) NOT NULL CONSTRAINT [PK_audit_ChangeLog] PRIMARY KEY CLUSTERED,
    [UserId] INT       NOT NULL,
    CONSTRAINT [FK_ChangeLog_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[User] ([Id]) ON DELETE NO ACTION
)
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'ChangeLog', @level0type = N'SCHEMA', @level0name = N'audit', @level1type = N'TABLE', @level1name = N'ChangeLog'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:19:ChangeLog', @level0type = N'SCHEMA', @level0name = N'audit', @level1type = N'TABLE', @level1name = N'ChangeLog'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'At', @level0type = N'SCHEMA', @level0name = N'audit', @level1type = N'TABLE', @level1name = N'ChangeLog', @level2type = N'COLUMN', @level2name = N'At'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id', @level0type = N'SCHEMA', @level0name = N'audit', @level1type = N'TABLE', @level1name = N'ChangeLog', @level2type = N'COLUMN', @level2name = N'Id'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'UserId', @level0type = N'SCHEMA', @level0name = N'audit', @level1type = N'TABLE', @level1name = N'ChangeLog', @level2type = N'COLUMN', @level2name = N'UserId'