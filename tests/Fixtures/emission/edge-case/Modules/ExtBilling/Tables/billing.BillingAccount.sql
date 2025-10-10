CREATE TABLE billing.BillingAccount (
    Id            INT          NOT NULL
        CONSTRAINT PK_BillingAccount
            PRIMARY KEY CLUSTERED,
    AccountNumber VARCHAR (50) NOT NULL,
    ExtRef        VARCHAR (50)
)

GO

CREATE UNIQUE INDEX IDX_BillingAccount_Acctnum
    ON billing.BillingAccount(AccountNumber ASC)

