CREATE TABLE [dbo].[Audit] (
    [Id]      BIGINT         IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_Audit]
            PRIMARY KEY CLUSTERED,
    [Details] NVARCHAR (200)
)

GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'[dbo].[Audit]')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[Audit]'), N'Id', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Audit primary key',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Audit',
        @level2type=N'COLUMN',@level2name=N'Id';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Audit primary key',
        @level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'Audit',
        @level2type=N'COLUMN',@level2name=N'Id';
