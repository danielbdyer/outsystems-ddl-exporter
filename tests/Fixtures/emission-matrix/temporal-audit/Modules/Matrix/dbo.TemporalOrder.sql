CREATE TABLE [dbo].[TemporalOrder] (
    [Id]        BIGINT        NOT NULL
        CONSTRAINT [PK_TemporalOrder_Id]
            PRIMARY KEY CLUSTERED,
    [Status]    NVARCHAR (50) NOT NULL
        DEFAULT ('PENDING')
        CONSTRAINT [CK_TEMPORALORDER_STATUS] CHECK ([Status] IN ('PENDING', 'APPROVED', 'ARCHIVED')),
    [AuditId]   BIGINT        NULL
        CONSTRAINT [FK_TemporalOrder_Audit_AuditId]
            FOREIGN KEY ([AuditId]) REFERENCES [dbo].[Audit] ([Id]),
    [ValidFrom] DATETIME2     NOT NULL,
    [ValidTo]   DATETIME2     NOT NULL
)

GO

CREATE INDEX [IX_TemporalOrder_Status_AuditId]
    ON [dbo].[TemporalOrder]([Status], [AuditId])
    INCLUDE([ValidFrom]) WHERE ([Status] <> 'DELETED') WITH (FILLFACTOR = 90)

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Temporal order entity',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'TemporalOrder';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Temporal order identifier',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'TemporalOrder',
    @level2type=N'COLUMN',@level2name=N'Id';

GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Workflow status',
    @level0type=N'SCHEMA',@level0name=N'dbo',
    @level1type=N'TABLE',@level1name=N'TemporalOrder',
    @level2type=N'COLUMN',@level2name=N'Status';

GO

-- Trigger: TR_TEMPORALORDER_AUDIT (disabled: false)
CREATE TRIGGER TR_TEMPORALORDER_AUDIT ON [dbo].[TemporalOrder] AFTER INSERT AS BEGIN SET NOCOUNT ON; END
