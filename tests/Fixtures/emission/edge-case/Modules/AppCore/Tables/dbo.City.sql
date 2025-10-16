CREATE TABLE [dbo].[City] (
    [ID]       BIGINT         IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_City]
            PRIMARY KEY CLUSTERED,
    [NAME]     NVARCHAR (200) NOT NULL,
    [ISACTIVE] BIT            NOT NULL
        DEFAULT ((1))
)
