CREATE TABLE billing.BillingAccount (
    Id            INT          NOT NULL,
    AccountNumber VARCHAR (50) NOT NULL,
    ExtRef        VARCHAR (50),
    CONSTRAINT PK_BillingAccount PRIMARY KEY CLUSTERED (Id)
)
