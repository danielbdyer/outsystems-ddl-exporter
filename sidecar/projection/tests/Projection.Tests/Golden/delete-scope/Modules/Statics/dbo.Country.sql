CREATE TABLE [dbo].[Country] (
    [Code]  NVARCHAR (2)   NOT NULL,
    [Id]    INT            NOT NULL CONSTRAINT [PK_dbo_Country] PRIMARY KEY CLUSTERED,
    [Label] NVARCHAR (100) NOT NULL
)
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Country', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Country'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:17:Country', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Country'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Code', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Country', @level2type = N'COLUMN', @level2name = N'Code'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Country', @level2type = N'COLUMN', @level2name = N'Id'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Label', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Country', @level2type = N'COLUMN', @level2name = N'Label'