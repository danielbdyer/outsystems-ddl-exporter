CREATE TABLE [dbo].[Task] (
    [CreatedBy] INT            NOT NULL,
    [Id]        INT            IDENTITY (1, 1) NOT NULL CONSTRAINT [PK_dbo_Task] PRIMARY KEY CLUSTERED,
    [Title]     NVARCHAR (200) NOT NULL,
    [UpdatedBy] INT            NULL,
    CONSTRAINT [FK_Task_User_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[User] ([Id]) ON DELETE NO ACTION ON UPDATE CASCADE,
    CONSTRAINT [FK_Task_User_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[User] ([Id]) ON DELETE NO ACTION
)
ALTER TABLE [dbo].[Task] NOCHECK CONSTRAINT [FK_Task_User_UpdatedBy]
ALTER TABLE [dbo].[Task] WITH NOCHECK CHECK CONSTRAINT [FK_Task_User_UpdatedBy]
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Task', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Task'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:14:Task', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Task'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'CreatedBy', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Task', @level2type = N'COLUMN', @level2name = N'CreatedBy'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Task', @level2type = N'COLUMN', @level2name = N'Id'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Title', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Task', @level2type = N'COLUMN', @level2name = N'Title'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'UpdatedBy', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Task', @level2type = N'COLUMN', @level2name = N'UpdatedBy'