CREATE TABLE [dbo].[User] (
    [Email] NVARCHAR (250) NOT NULL,
    [Id]    INT            IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_User]
            PRIMARY KEY CLUSTERED
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'The platform user kind (pure reference target).',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'User',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:14:User',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Email',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User',
    @level2type = N'COLUMN', @level2name = N'Email'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User',
    @level2type = N'COLUMN', @level2name = N'Id'

