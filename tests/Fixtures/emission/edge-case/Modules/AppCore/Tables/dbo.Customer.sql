CREATE TABLE dbo.Customer (
    Id        INT            NOT NULL,
    Email     NVARCHAR (255) NOT NULL,
    FirstName NVARCHAR (100),
    LastName  NVARCHAR (100),
    CityId    INT            NOT NULL,
    CONSTRAINT PK_Customer PRIMARY KEY CLUSTERED (Id)
)
