CREATE TABLE [dbo].[User] (
    [Id]    BIGINT         NOT NULL
        CONSTRAINT [PK_User]
            PRIMARY KEY CLUSTERED,
    [Email] NVARCHAR (255) NOT NULL
)

GO

CREATE UNIQUE INDEX [UX_User_Email]
    ON [dbo].[User]([Email]) WITH (IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
