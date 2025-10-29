CREATE TABLE [dbo].[User] (
    [Id]       BIGINT         IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_User_Id]
            PRIMARY KEY CLUSTERED,
    [Username] NVARCHAR (50)  NOT NULL,
    [Email]    NVARCHAR (255) NOT NULL
)

GO

CREATE UNIQUE INDEX [UIX_User_Username]
    ON [dbo].[User]([Username])

GO

CREATE UNIQUE INDEX [UIX_User_Email]
    ON [dbo].[User]([Email])

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Supplemental representation of the OutSystems user table',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'User identifier',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User',
    @level2type=N'COLUMN',@level2name=N'Id';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Unique username',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User',
    @level2type=N'COLUMN',@level2name=N'Username';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'User email',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User',
    @level2type=N'COLUMN',@level2name=N'Email';
