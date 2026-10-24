CREATE TABLE [dbo].[OperatorProfile] (
    [Id]           BIGINT        NOT NULL
        CONSTRAINT [PK_OperatorProfile_Id]
            PRIMARY KEY CLUSTERED,
    [OperatorCode] NVARCHAR (50) NOT NULL
)

GO

CREATE UNIQUE INDEX [UIX_OperatorProfile_OperatorCode]
    ON [dbo].[OperatorProfile]([OperatorCode])
