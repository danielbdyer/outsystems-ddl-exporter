CREATE TABLE [dbo].[RegionB] (
    [Id]        INT           NOT NULL CONSTRAINT [PK_dbo_RegionB] PRIMARY KEY CLUSTERED,
    [Name]      NVARCHAR (60) NOT NULL,
    [PartnerId] INT           NULL,
    CONSTRAINT [FK_RegionB_RegionA_PartnerId] FOREIGN KEY ([PartnerId]) REFERENCES [dbo].[RegionA] ([Id]) ON DELETE NO ACTION
)
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'RegionB', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RegionB'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:17:RegionB', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RegionB'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RegionB', @level2type = N'COLUMN', @level2name = N'Id'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Name', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RegionB', @level2type = N'COLUMN', @level2name = N'Name'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'PartnerId', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RegionB', @level2type = N'COLUMN', @level2name = N'PartnerId'