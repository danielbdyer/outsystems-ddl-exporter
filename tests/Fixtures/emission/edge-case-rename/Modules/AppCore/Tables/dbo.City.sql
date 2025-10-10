CREATE TABLE dbo.City (
    Id       INT            NOT NULL
        CONSTRAINT PK_City
            PRIMARY KEY CLUSTERED,
    Name     NVARCHAR (200) NOT NULL,
    IsActive BIT            NOT NULL
)

