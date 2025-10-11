CREATE TABLE [dbo].[City] (
    [Id]       BIGINT         NOT NULL
        CONSTRAINT [PK_City]
            PRIMARY KEY CLUSTERED,
    [IsActive] BIT            NOT NULL
        DEFAULT ((1)),
    [Name]     NVARCHAR (200) NOT NULL
)
