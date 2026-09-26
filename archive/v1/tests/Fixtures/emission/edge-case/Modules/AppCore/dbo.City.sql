CREATE TABLE [dbo].[City] (
    [Id]       BIGINT         IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_City_Id]
            PRIMARY KEY CLUSTERED,
    [Name]     NVARCHAR (200) NOT NULL,
    [IsActive] BIT            NOT NULL
        DEFAULT 1
)
