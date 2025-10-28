CREATE TABLE [billing].[BillingAccount] (
    [Id]            BIGINT       IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_BillingAccount_Id]
            PRIMARY KEY CLUSTERED,
    [AccountNumber] VARCHAR (50) NOT NULL,
    [ExtRef]        VARCHAR (50)
)

GO

CREATE UNIQUE INDEX [IDX_BillingAccount_Acctnum]
    ON [billing].[BillingAccount]([AccountNumber]) WITH (IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
