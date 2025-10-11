CREATE TABLE [billing].[BillingAccount] (
    [Id]            BIGINT       NOT NULL
        CONSTRAINT [PK_BillingAccount]
            PRIMARY KEY CLUSTERED,
    [AccountNumber] VARCHAR (50) NOT NULL,
    [ExtRef]        VARCHAR (50)
)

GO

CREATE UNIQUE INDEX [IDX_BillingAccount_Acctnum]
    ON [billing].[BillingAccount]([AccountNumber] ASC) WITH (PAD_INDEX = OFF, IGNORE_DUP_KEY = OFF, STATISTICS_NORECOMPUTE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)

