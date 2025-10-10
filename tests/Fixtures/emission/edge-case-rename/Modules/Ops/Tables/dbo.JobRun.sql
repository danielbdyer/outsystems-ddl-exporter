CREATE TABLE dbo.JobRun (
    Id                INT      NOT NULL,
    TriggeredByUserId INT     ,
    CreatedOn         DATETIME CONSTRAINT DF_JobRun_CreatedOn DEFAULT getutcdate() NOT NULL,
    CONSTRAINT PK_JobRun PRIMARY KEY CLUSTERED (Id)
)
