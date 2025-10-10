CREATE TABLE dbo.JobRun (
    Id                INT      NOT NULL
        CONSTRAINT PK_JobRun
            PRIMARY KEY CLUSTERED,
    TriggeredByUserId INT     ,
    CreatedOn         DATETIME NOT NULL
)

