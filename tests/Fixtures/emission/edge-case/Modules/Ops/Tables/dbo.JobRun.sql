CREATE TABLE dbo.JobRun (
    Id                BIGINT   NOT NULL
        CONSTRAINT PK_JobRun
            PRIMARY KEY CLUSTERED,
    TriggeredByUserId BIGINT  ,
    CreatedOn         DATETIME NOT NULL
        DEFAULT (getutcdate())
)

