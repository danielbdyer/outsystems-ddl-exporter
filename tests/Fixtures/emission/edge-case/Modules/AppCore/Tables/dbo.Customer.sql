CREATE TABLE [dbo].[Customer] (
    [Id]        BIGINT         IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_Customer]
            PRIMARY KEY CLUSTERED,
    [Email]     NVARCHAR (255) NOT NULL,
    [FirstName] NVARCHAR (100)
        DEFAULT (''),
    [LastName]  NVARCHAR (100)
        DEFAULT (''),
    [CityId]    BIGINT         NOT NULL
        CONSTRAINT [FK_Customer_Osusr_Def_City_CityId]
            FOREIGN KEY ([CityId]) REFERENCES [dbo].[City] ([Id])
)

GO

CREATE UNIQUE INDEX [IDX_Customer_Email]
    ON [dbo].[Customer]([Email]) WHERE ([Email] IS NOT NULL) WITH (FILLFACTOR = 85, IGNORE_DUP_KEY = ON, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
    ON [FG_Customers]

GO

CREATE INDEX [IDX_Customer_Name]
    ON [dbo].[Customer]([LastName], [FirstName]) WITH (IGNORE_DUP_KEY = OFF, STATISTICS_NORECOMPUTE = ON, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
    ON [FG_Customers]

GO

ALTER INDEX [IDX_Customer_Name]
    ON [dbo].[Customer] DISABLE

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Stores customer records for AppCore',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'Customer';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer identifier',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'Customer',
    @level2type=N'COLUMN',@level2name=N'Id';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer email',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'Customer',
    @level2type=N'COLUMN',@level2name=N'Email';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer first name',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'Customer',
    @level2type=N'COLUMN',@level2name=N'FirstName';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer last name',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'Customer',
    @level2type=N'COLUMN',@level2name=N'LastName';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'FK to City',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'Customer',
    @level2type=N'COLUMN',@level2name=N'CityId';
