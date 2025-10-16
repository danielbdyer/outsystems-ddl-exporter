CREATE TABLE [dbo].[Customer] (
    [ID]        BIGINT         IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_Customer]
            PRIMARY KEY CLUSTERED,
    [EMAIL]     NVARCHAR (255) COLLATE [Latin1_General_CI_AI] NOT NULL,
    [FIRSTNAME] NVARCHAR (100)
        DEFAULT (''),
    [LASTNAME]  NVARCHAR (100)
        DEFAULT (''),
    [CITYID]    BIGINT         NOT NULL
        CONSTRAINT [FK_Customer_CityId]
            FOREIGN KEY ([CITYID]) REFERENCES [dbo].[City] ([ID])
)

GO

CREATE UNIQUE INDEX [IDX_Customer_Email]
    ON [dbo].[Customer]([EMAIL]) WHERE ([EMAIL] IS NOT NULL) WITH (FILLFACTOR = 85, IGNORE_DUP_KEY = ON, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
    ON [FG_Customers]

GO

CREATE INDEX [IDX_Customer_Name]
    ON [dbo].[Customer]([LASTNAME], [FIRSTNAME]) WITH (IGNORE_DUP_KEY = OFF, STATISTICS_NORECOMPUTE = ON, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
    ON [FG_Customers]

GO

ALTER INDEX [IDX_Customer_Name]
    ON [dbo].[Customer] DISABLE

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
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[Customer]'), N'ID', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Customer identifier',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'ID';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer identifier',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'ID';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[Customer]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[Customer]'), N'EMAIL', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Customer email',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'EMAIL';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer email',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'EMAIL';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[Customer]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[Customer]'), N'FIRSTNAME', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Customer first name',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'FIRSTNAME';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer first name',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'FIRSTNAME';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[Customer]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[Customer]'), N'LASTNAME', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Customer last name',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'LASTNAME';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer last name',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'LASTNAME';

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[Customer]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[Customer]'), N'CITYID', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'FK to City',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'CITYID';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'FK to City',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Customer',
        @level2type=N'COLUMN',@level2name=N'CITYID';
