CREATE TABLE [dbo].[RegionA] (
    [Id]        INT           NOT NULL CONSTRAINT [PK_dbo_RegionA] PRIMARY KEY CLUSTERED,
    [Name]      NVARCHAR (60) NOT NULL,
    [PartnerId] INT           NULL,
    CONSTRAINT [FK_RegionA_RegionB_PartnerId] FOREIGN KEY ([PartnerId]) REFERENCES [dbo].[RegionB] ([Id]) ON DELETE NO ACTION
)
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'RegionA', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RegionA'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:17:RegionA', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RegionA'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RegionA', @level2type = N'COLUMN', @level2name = N'Id'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Name', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RegionA', @level2type = N'COLUMN', @level2name = N'Name'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'PartnerId', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RegionA', @level2type = N'COLUMN', @level2name = N'PartnerId'