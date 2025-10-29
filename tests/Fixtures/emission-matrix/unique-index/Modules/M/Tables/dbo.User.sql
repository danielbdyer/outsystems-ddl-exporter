CREATE TABLE [dbo].[User] (
    [Id]          BIGINT          NOT NULL
        CONSTRAINT [PK_User_Id]
            PRIMARY KEY CLUSTERED,
    [Email]       NVARCHAR (255)  NOT NULL,
    [BirthDate]   DATE            NULL,
    [CreditLimit] DECIMAL (37, 8) NULL
)

GO

CREATE UNIQUE INDEX [UIX_User_Email]
    ON [dbo].[User]([Email])
