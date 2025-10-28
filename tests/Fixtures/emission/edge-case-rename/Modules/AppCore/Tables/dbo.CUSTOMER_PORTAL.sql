CREATE TABLE [dbo].[CUSTOMER_PORTAL] (
    [Id]        BIGINT         IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_CustomerPortal_Id]
            PRIMARY KEY CLUSTERED,
    [Email]     NVARCHAR (255) NOT NULL,
    [FirstName] NVARCHAR (100)
        DEFAULT (''),
    [LastName]  NVARCHAR (100)
        DEFAULT (''),
    [CityId]    BIGINT         NOT NULL
        CONSTRAINT [FK_CUSTOMER_PORTAL_CityId]
            FOREIGN KEY ([CityId]) REFERENCES [dbo].[City] ([Id])
)

GO

CREATE UNIQUE INDEX [IDX_CUSTOMER_PORTAL_Email]
    ON [dbo].[CUSTOMER_PORTAL]([Email]) WHERE ([Email] IS NOT NULL) WITH (FILLFACTOR = 85, IGNORE_DUP_KEY = ON, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
    ON [FG_Customers]

GO

CREATE INDEX [IDX_CUSTOMER_PORTAL_Name]
    ON [dbo].[CUSTOMER_PORTAL]([LastName], [FirstName]) WITH (IGNORE_DUP_KEY = OFF, STATISTICS_NORECOMPUTE = ON, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
    ON [FG_Customers]

GO

ALTER INDEX [IDX_CUSTOMER_PORTAL_Name]
    ON [dbo].[CUSTOMER_PORTAL] DISABLE

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Stores customer records for AppCore',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer identifier',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
    @level2type=N'COLUMN',@level2name=N'Id';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer email',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
    @level2type=N'COLUMN',@level2name=N'Email';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer first name',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
    @level2type=N'COLUMN',@level2name=N'FirstName';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer last name',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
    @level2type=N'COLUMN',@level2name=N'LastName';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'FK to City',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
    @level2type=N'COLUMN',@level2name=N'CityId';
