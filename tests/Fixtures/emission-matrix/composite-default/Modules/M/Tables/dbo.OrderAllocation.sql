CREATE TABLE [dbo].[OrderAllocation] (
    [Id]         BIGINT NOT NULL
        CONSTRAINT [PK_OrderAllocation_Id]
            PRIMARY KEY CLUSTERED,
    [CountryId]  BIGINT NOT NULL,
    [DocumentId] BIGINT NOT NULL,
    [Quantity]   INT
        DEFAULT (0)
)

GO

CREATE UNIQUE INDEX [UX_Orderalloc_Country_Document]
    ON [dbo].[OrderAllocation]([CountryId], [DocumentId]) WITH (IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
