IF OBJECT_ID(N'[billing].[BillingAccount]', N'U') IS NULL
BEGIN
    CREATE TABLE [billing].[BillingAccount] (
        [AccountNumber] VARCHAR (50) NOT NULL,
        [ExtRef]        VARCHAR (50),
        [Id]            BIGINT       NOT NULL
            CONSTRAINT [PK_BillingAccount]
                PRIMARY KEY CLUSTERED
    )
END

GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IDX_BillingAccount_Acctnum'
      AND object_id = OBJECT_ID(N'[billing].[BillingAccount]', N'U')
)
BEGIN
    CREATE UNIQUE INDEX [IDX_BillingAccount_Acctnum]
        ON [billing].[BillingAccount]([AccountNumber] ASC) WITH (PAD_INDEX = OFF, IGNORE_DUP_KEY = OFF, STATISTICS_NORECOMPUTE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
END
