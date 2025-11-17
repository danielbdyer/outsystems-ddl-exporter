CREATE TABLE [dbo].[User] (
    [Id]            BIGINT         IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_User_Id]
            PRIMARY KEY CLUSTERED,
    [Username]      NVARCHAR (250) NOT NULL,
    [EMail]         NVARCHAR (250) NOT NULL,
    [Name]          NVARCHAR (256) NULL,
    [MobilePhone]   NVARCHAR (20)  NULL,
    [Password]      NVARCHAR (256) NULL,
    [External_Id]   NVARCHAR (36)  NULL,
    [Is_Active]     BIT            NULL,
    [Creation_Date] DATETIME       NULL,
    [Last_Login]    DATETIME       NULL
)

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'End-user of the applications. Shared between spaces with the same user provider (defined in Service Studio).',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Unique identifier of the user.',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User',
    @level2type=N'COLUMN',@level2name=N'Id';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Login name of the user.',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User',
    @level2type=N'COLUMN',@level2name=N'Username';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Email contact of the user.',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User',
    @level2type=N'COLUMN',@level2name=N'EMail';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Full name of the user.',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User',
    @level2type=N'COLUMN',@level2name=N'Name';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Mobile phone number of the user.',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User',
    @level2type=N'COLUMN',@level2name=N'MobilePhone';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Login password of the user.',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User',
    @level2type=N'COLUMN',@level2name=N'Password';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'The user identifier in an external system to the Platform.',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User',
    @level2type=N'COLUMN',@level2name=N'External_Id';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Indicates if the user is still active.',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User',
    @level2type=N'COLUMN',@level2name=N'Is_Active';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'The date the user was created.',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User',
    @level2type=N'COLUMN',@level2name=N'Creation_Date';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Last time the user logged in the application.',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'User',
    @level2type=N'COLUMN',@level2name=N'Last_Login';
