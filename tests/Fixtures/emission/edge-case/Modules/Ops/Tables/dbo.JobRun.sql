CREATE TABLE dbo.JobRun (
    Id                INT      NOT NULL,
    TriggeredByUserId INT     ,
    CreatedOn         DATETIME NOT NULL,
    CONSTRAINT PK_JobRun PRIMARY KEY CLUSTERED (Id)
)
