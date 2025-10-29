CREATE TABLE [dbo].[JobRun] (
    [Id]                BIGINT   IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_JobRun_Id]
            PRIMARY KEY CLUSTERED,
    [TriggeredByUserId] BIGINT   NULL,
    [CreatedOn]         DATETIME NOT NULL
        DEFAULT (getutcdate())
)

GO

-- Trigger: TR_OSUSR_XYZ_JOBRUN_AUDIT (disabled: true)
CREATE TRIGGER [dbo].[TR_JobRun_AUDIT] ON [dbo].[JobRun] AFTER INSERT AS BEGIN SET NOCOUNT ON; END
ALTER TABLE [dbo].[JobRun] DISABLE TRIGGER [TR_OSUSR_XYZ_JOBRUN_AUDIT];
