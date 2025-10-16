CREATE TABLE [billing].[BillingAccount] (
    [ID]            BIGINT       IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_BillingAccount]
            PRIMARY KEY CLUSTERED,
    [ACCOUNTNUMBER] VARCHAR (50) NOT NULL,
    [EXTREF]        VARCHAR (50)
)

GO

CREATE UNIQUE INDEX [IDX_BillingAccount_Acctnum]
    ON [billing].[BillingAccount]([ACCOUNTNUMBER]) WITH (IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
