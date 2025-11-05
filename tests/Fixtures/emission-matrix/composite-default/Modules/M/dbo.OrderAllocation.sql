CREATE TABLE [dbo].[OrderAllocation] (
    [Id]         BIGINT NOT NULL
        CONSTRAINT [PK_OrderAllocation_Id]
            PRIMARY KEY CLUSTERED,
    [CountryId]  BIGINT NOT NULL,
    [DocumentId] BIGINT NOT NULL,
    [Quantity]   INT    NULL
        DEFAULT 0
)

GO

CREATE UNIQUE INDEX [UIX_OrderAllocation_CountryId_DocumentId]
    ON [dbo].[OrderAllocation]([CountryId], [DocumentId])
