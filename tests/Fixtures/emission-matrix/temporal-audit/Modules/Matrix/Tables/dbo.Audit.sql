CREATE TABLE [dbo].[Audit] (
    [Id]      BIGINT         IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_Audit_Id]
            PRIMARY KEY CLUSTERED,
    [Details] NVARCHAR (200) NULL
)

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Audit primary key',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'Audit',
    @level2type=N'COLUMN',@level2name=N'Id';
