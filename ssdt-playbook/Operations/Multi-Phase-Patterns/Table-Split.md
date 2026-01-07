# 17.6 Pattern: Table Split (Vertical Partitioning)

**When to use:** Extracting columns from one table into a new related table

**Scenario:** Extract address columns from `Customer` into `CustomerAddress`

### Phase 1 (Release N): Create New Table

```sql
-- Declarative: New table file
CREATE TABLE [dbo].[CustomerAddress]
(
    [CustomerAddressId] INT IDENTITY(1,1) NOT NULL,
    [CustomerId] INT NOT NULL,
    [Street] NVARCHAR(200) NULL,
    [City] NVARCHAR(100) NULL,
    [State] NVARCHAR(50) NULL,
    [PostalCode] NVARCHAR(20) NULL,
    
    CONSTRAINT [PK_CustomerAddress] PRIMARY KEY CLUSTERED ([CustomerAddressId]),
    CONSTRAINT [FK_CustomerAddress_Customer] FOREIGN KEY ([CustomerId]) 
        REFERENCES [dbo].[Customer]([CustomerId])
)
```

### Phase 2 (Release N): Migrate Data (Post-Deployment)

```sql
-- PostDeployment script
PRINT 'Migrating address data...'

INSERT INTO dbo.CustomerAddress (CustomerId, Street, City, State, PostalCode)
SELECT CustomerId, AddressStreet, AddressCity, AddressState, AddressPostalCode
FROM dbo.Customer
WHERE AddressStreet IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.CustomerAddress ca WHERE ca.CustomerId = dbo.Customer.CustomerId)

PRINT 'Address data migrated.'
```

### Phase 3 (Multiple Releases): Application Transition

Application gradually shifts from `Customer.AddressX` to `CustomerAddress.X`. This may take multiple releases.

### Phase 4 (Release N+X): Drop Old Columns

```sql
-- Declarative: Remove address columns from Customer table definition
-- Columns are simply gone
```

**Rollback notes:**
- Phase 1-3: Drop new table, data still in original
- Phase 4: Requires backup restore

---
