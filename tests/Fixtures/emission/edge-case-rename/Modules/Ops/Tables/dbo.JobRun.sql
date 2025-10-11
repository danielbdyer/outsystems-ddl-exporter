CREATE TABLE [dbo].[JobRun] (
    [Id]                BIGINT   NOT NULL
        CONSTRAINT [PK_JobRun]
            PRIMARY KEY CLUSTERED,
    [TriggeredByUserId] BIGINT,
    [CreatedOn]         DATETIME NOT NULL
        DEFAULT (getutcdate())
)

-- Trigger: TR_OSUSR_XYZ_JOBRUN_AUDIT (disabled: true)
CREATE TRIGGER [dbo].[TR_OSUSR_XYZ_JOBRUN_AUDIT] ON [dbo].[OSUSR_XYZ_JOBRUN] AFTER INSERT AS BEGIN SET NOCOUNT ON; END
ALTER TABLE [dbo].[JobRun] DISABLE TRIGGER [TR_OSUSR_XYZ_JOBRUN_AUDIT];

