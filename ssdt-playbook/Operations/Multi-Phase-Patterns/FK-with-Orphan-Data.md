# 17.4 Pattern: Add FK with Orphan Data

**When to use:** Adding a foreign key when orphan records exist that you can't immediately delete

**Scenario:** Add `FK_Order_Customer` but some orders have invalid `CustomerId` values

### Phase 1 (Release N): Add FK as Untrusted

```sql
-- PostDeployment script (not declarative — SSDT doesn't support NOCHECK directly)
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Order_Customer')
BEGIN
    ALTER TABLE dbo.[Order] WITH NOCHECK
    ADD CONSTRAINT FK_Order_Customer 
        FOREIGN KEY (CustomerId) REFERENCES dbo.Customer(CustomerId)
    
    PRINT 'FK created as untrusted.'
END
```

### Phase 2 (Release N or N+1): Clean Orphan Data

```sql
-- PostDeployment script
PRINT 'Cleaning orphan orders...'

-- Option A: Delete orphans
DELETE FROM dbo.[Order]
WHERE CustomerId NOT IN (SELECT CustomerId FROM dbo.Customer)

-- Option B: Create placeholder customer for orphans
IF NOT EXISTS (SELECT 1 FROM dbo.Customer WHERE CustomerId = -1)
BEGIN
    SET IDENTITY_INSERT dbo.Customer ON
    INSERT INTO dbo.Customer (CustomerId, FirstName, LastName, Email)
    VALUES (-1, 'Unknown', 'Customer', 'orphan@placeholder.com')
    SET IDENTITY_INSERT dbo.Customer OFF
END

UPDATE dbo.[Order]
SET CustomerId = -1
WHERE CustomerId NOT IN (SELECT CustomerId FROM dbo.Customer)

PRINT 'Orphans handled.'
```

### Phase 3 (Release N+1 or N+2): Enable Trust

```sql
-- PostDeployment script
PRINT 'Enabling FK trust...'

ALTER TABLE dbo.[Order] WITH CHECK CHECK CONSTRAINT FK_Order_Customer

PRINT 'FK is now trusted.'
```

### Phase 4: Declarative Definition

Add the FK to your declarative table definition. SSDT will see it already exists and matches.

```sql
CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) 
    REFERENCES [dbo].[Customer]([CustomerId])
```

**Verification:**
```sql
-- Check trust status
SELECT name, is_not_trusted
FROM sys.foreign_keys
WHERE name = 'FK_Order_Customer'
-- is_not_trusted should be 0 after Phase 3
```

---
