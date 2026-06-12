CREATE TABLE [dbo].[ScopedLookup] (
    [Id]       INT           NOT NULL CONSTRAINT [PK_dbo_ScopedLookup] PRIMARY KEY CLUSTERED,
    [TenantId] INT           NOT NULL,
    [Value]    NVARCHAR (80) NOT NULL
)
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'ScopedLookup', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'ScopedLookup'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:112:ScopedLookup', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'ScopedLookup'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'ScopedLookup', @level2type = N'COLUMN', @level2name = N'Id'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'TenantId', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'ScopedLookup', @level2type = N'COLUMN', @level2name = N'TenantId'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Value', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'ScopedLookup', @level2type = N'COLUMN', @level2name = N'Value'