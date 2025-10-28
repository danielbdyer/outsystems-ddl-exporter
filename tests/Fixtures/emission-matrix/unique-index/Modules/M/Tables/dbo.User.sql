CREATE TABLE [dbo].[User] (
    [Id]          BIGINT          NOT NULL
        CONSTRAINT [PK_User_Id]
            PRIMARY KEY CLUSTERED,
    [Email]       NVARCHAR (255)  NOT NULL,
    [BirthDate]   DATE,
    [CreditLimit] DECIMAL (37, 8)
)

GO

CREATE UNIQUE INDEX [UX_User_Email]
    ON [dbo].[User]([Email]) WITH (IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
