CREATE TABLE [dbo].[User] (
    [Id]       BIGINT         IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_User_Id]
            PRIMARY KEY CLUSTERED,
    [Username] NVARCHAR (50)  NOT NULL,
    [Email]    NVARCHAR (255) NOT NULL
)
