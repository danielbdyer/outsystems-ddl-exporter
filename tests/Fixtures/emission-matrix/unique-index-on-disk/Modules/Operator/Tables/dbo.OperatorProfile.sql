CREATE TABLE [dbo].[OperatorProfile] (
    [Id]           BIGINT        NOT NULL
        CONSTRAINT [PK_OperatorProfile]
            PRIMARY KEY CLUSTERED,
    [OperatorCode] NVARCHAR (50)
)

GO

CREATE UNIQUE INDEX [UIX_OperatorProfile_OperatorCode]
    ON [dbo].[OperatorProfile]([OperatorCode]) WITH (IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
