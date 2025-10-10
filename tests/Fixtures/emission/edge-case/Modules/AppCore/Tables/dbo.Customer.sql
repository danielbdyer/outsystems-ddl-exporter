CREATE TABLE [dbo].[Customer] (
    [Id]        BIGINT         IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_Customer]
            PRIMARY KEY CLUSTERED,
    [Email]     NVARCHAR (255) COLLATE Latin1_General_CI_AI NOT NULL,
    [FirstName] NVARCHAR (100)
        DEFAULT (''),
    [LastName]  NVARCHAR (100)
        DEFAULT (''),
    [CityId]    BIGINT         NOT NULL,
    CONSTRAINT [FK_Customer_CityId]
        FOREIGN KEY ([CityId]) REFERENCES [dbo].[City] ([Id])
            ON DELETE NO ACTION
            ON UPDATE NO ACTION
)

GO

CREATE UNIQUE INDEX [IDX_Customer_Email]
    ON [dbo].[Customer]([Email] ASC)

GO

CREATE INDEX [IDX_Customer_Name]
    ON [dbo].[Customer]([LastName] ASC, [FirstName] ASC)

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[Customer]')
      AND minor_id = 0
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Stores customer records for AppCore',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Stores customer records for AppCore',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[Customer]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[Customer]'), N'Id', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Customer identifier',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'Id';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer identifier',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'Id';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[Customer]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[Customer]'), N'Email', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Customer email',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'Email';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer email',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'Email';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[Customer]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[Customer]'), N'FirstName', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Customer first name',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'FirstName';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer first name',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'FirstName';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[Customer]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[Customer]'), N'LastName', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Customer last name',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'LastName';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer last name',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'LastName';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[Customer]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[Customer]'), N'CityId', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'FK to City',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'CityId';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'FK to City',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'CityId';

