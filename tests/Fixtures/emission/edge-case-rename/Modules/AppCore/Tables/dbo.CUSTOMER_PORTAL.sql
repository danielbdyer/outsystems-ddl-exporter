CREATE TABLE [dbo].[CUSTOMER_PORTAL] (
    [CityId]    BIGINT         NOT NULL,
    [Email]     NVARCHAR (255) COLLATE [Latin1_General_CI_AI] NOT NULL,
    [FirstName] NVARCHAR (100)
        DEFAULT (''),
    [Id]        BIGINT         IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_CUSTOMER_PORTAL]
            PRIMARY KEY CLUSTERED,
    [LastName]  NVARCHAR (100)
        DEFAULT (''),
    CONSTRAINT [FK_CUSTOMER_PORTAL_CityId]
        FOREIGN KEY ([CityId]) REFERENCES [dbo].[City] ([Id])
            ON DELETE NO ACTION ON UPDATE NO ACTION
)

GO

CREATE UNIQUE INDEX [IDX_CUSTOMER_PORTAL_Email]
    ON [dbo].[CUSTOMER_PORTAL]([Email] ASC) WHERE ([EMAIL] IS NOT NULL) WITH (FILLFACTOR = 85, PAD_INDEX = OFF, IGNORE_DUP_KEY = ON, STATISTICS_NORECOMPUTE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
    ON [FG_Customers]

GO

CREATE INDEX [IDX_CUSTOMER_PORTAL_Name]
    ON [dbo].[CUSTOMER_PORTAL]([LastName] ASC, [FirstName] ASC) WITH (PAD_INDEX = OFF, IGNORE_DUP_KEY = OFF, STATISTICS_NORECOMPUTE = ON, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
    ON [FG_Customers]

GO

ALTER INDEX [IDX_CUSTOMER_PORTAL_Name]
    ON [dbo].[CUSTOMER_PORTAL] DISABLE

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[CUSTOMER_PORTAL]')
      AND minor_id = 0
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Stores customer records for AppCore',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Stores customer records for AppCore',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[CUSTOMER_PORTAL]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[CUSTOMER_PORTAL]'), N'Id', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Customer identifier',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
        @level2type=N'COLUMN',@level2name=N'Id';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer identifier',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
        @level2type=N'COLUMN',@level2name=N'Id';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[CUSTOMER_PORTAL]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[CUSTOMER_PORTAL]'), N'Email', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Customer email',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
        @level2type=N'COLUMN',@level2name=N'Email';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer email',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
        @level2type=N'COLUMN',@level2name=N'Email';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[CUSTOMER_PORTAL]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[CUSTOMER_PORTAL]'), N'FirstName', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Customer first name',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
        @level2type=N'COLUMN',@level2name=N'FirstName';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer first name',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
        @level2type=N'COLUMN',@level2name=N'FirstName';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[CUSTOMER_PORTAL]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[CUSTOMER_PORTAL]'), N'LastName', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Customer last name',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
        @level2type=N'COLUMN',@level2name=N'LastName';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer last name',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
        @level2type=N'COLUMN',@level2name=N'LastName';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[CUSTOMER_PORTAL]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[CUSTOMER_PORTAL]'), N'CityId', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'FK to City',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
        @level2type=N'COLUMN',@level2name=N'CityId';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'FK to City',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'CUSTOMER_PORTAL',
        @level2type=N'COLUMN',@level2name=N'CityId';
