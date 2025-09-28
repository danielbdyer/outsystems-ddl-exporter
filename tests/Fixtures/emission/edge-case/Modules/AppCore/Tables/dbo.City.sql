CREATE TABLE dbo.City (
    Id       INT            NOT NULL,
    Name     NVARCHAR (200) NOT NULL,
    IsActive BIT            NOT NULL,
    CONSTRAINT PK_City PRIMARY KEY CLUSTERED (Id)
)
