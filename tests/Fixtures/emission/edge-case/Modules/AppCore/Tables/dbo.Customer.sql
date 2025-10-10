CREATE TABLE dbo.Customer (
    Id        INT            NOT NULL,
    Email     NVARCHAR (255) NOT NULL,
    FirstName NVARCHAR (100) CONSTRAINT DF_Customer_FirstName DEFAULT '',
    LastName  NVARCHAR (100) CONSTRAINT DF_Customer_LastName DEFAULT '',
    CityId    INT            NOT NULL,
    CONSTRAINT PK_Customer PRIMARY KEY CLUSTERED (Id)
)
