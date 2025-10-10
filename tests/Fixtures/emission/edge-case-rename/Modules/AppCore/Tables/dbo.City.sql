CREATE TABLE dbo.City (
    Id       INT            NOT NULL,
    Name     NVARCHAR (200) NOT NULL,
    IsActive BIT            CONSTRAINT DF_City_IsActive DEFAULT 1 NOT NULL,
    CONSTRAINT PK_City PRIMARY KEY CLUSTERED (Id)
)
