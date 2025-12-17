# 17. Multi-Phase Pattern Templates

---

## How to Use This Section

Each pattern provides:
- **When to use:** Conditions that trigger this pattern
- **Phase sequence:** The ordered steps across releases
- **Code templates:** Actual SQL for each phase
- **Rollback notes:** How to reverse if needed
- **Verification queries:** How to confirm each phase succeeded

---

## 17.1 Pattern: Explicit Conversion Data Type Change

**When to use:** Changing a column's data type when SQL Server can't implicitly convert (e.g., VARCHAR → DATE, INT → UNIQUEIDENTIFIER)

**Scenario:** Convert `PolicyDate` from `VARCHAR(10)` to `DATE`

### Phase 1 (Release N): Add New Column

```sql
-- Declarative: Add to table definition
[PolicyDateNew] DATE NULL,
```

### Phase 2 (Release N): Migrate Data (Post-Deployment)

```sql
-- PostDeployment script
PRINT 'Migrating PolicyDate to DATE type...'

UPDATE dbo.Policy
SET PolicyDateNew = TRY_CONVERT(DATE, PolicyDate, 101)  -- MM/DD/YYYY format
WHERE PolicyDateNew IS NULL
  AND PolicyDate IS NOT NULL

-- Log failures
INSERT INTO dbo.MigrationLog (TableName, ColumnName, FailedValue, FailureReason)
SELECT 'Policy', 'PolicyDate', PolicyDate, 'Invalid date format'
FROM dbo.Policy
WHERE PolicyDateNew IS NULL 
  AND PolicyDate IS NOT NULL

PRINT 'Migration complete. Check MigrationLog for failures.'
```

### Phase 3 (Release N+1): Application Transition

Application code switches from `PolicyDate` to `PolicyDateNew`. Both columns exist during this phase.

### Phase 4 (Release N+2): Remove Old, Rename New

```sql
-- Pre-deployment: Verify migration complete
IF EXISTS (SELECT 1 FROM dbo.Policy WHERE PolicyDate IS NOT NULL AND PolicyDateNew IS NULL)
BEGIN
    RAISERROR('Migration incomplete — some PolicyDate values not converted', 16, 1)
    RETURN
END
```

```sql
-- Declarative: Remove old column, rename new column (use refactorlog for rename)
-- After this release:
[PolicyDate] DATE NULL,  -- This is the renamed PolicyDateNew
```

**Rollback notes:**
- Phase 1-2: Drop new column, no data loss
- Phase 3: Revert application code
- Phase 4: Requires backup restore (old column is gone)

**Verification:**
```sql
-- After Phase 2: Check conversion success rate
SELECT 
    COUNT(*) AS TotalRows,
    SUM(CASE WHEN PolicyDateNew IS NOT NULL THEN 1 ELSE 0 END) AS Converted,
    SUM(CASE WHEN PolicyDateNew IS NULL AND PolicyDate IS NOT NULL THEN 1 ELSE 0 END) AS Failed
FROM dbo.Policy
```

---

## 17.2 Pattern: NULL → NOT NULL on Populated Table

**When to use:** Making an existing nullable column required

**Scenario:** Make `Customer.Email` NOT NULL

### Phase 1 (Release N): Backfill (Pre-Deployment)

```sql
-- PreDeployment script
PRINT 'Backfilling NULL emails...'

-- Option A: Default value
UPDATE dbo.Customer
SET Email = 'unknown@placeholder.com'
WHERE Email IS NULL

-- Option B: Derive from other data
UPDATE dbo.Customer
SET Email = LOWER(FirstName) + '.' + LOWER(LastName) + '@unknown.com'
WHERE Email IS NULL

PRINT 'Backfill complete.'
```

### Phase 2 (Release N): Apply Constraint (Declarative)

```sql
-- Table definition change
[Email] NVARCHAR(200) NOT NULL,  -- Changed from NULL
```

**If you must do it in one release:** Combine pre-deployment backfill with declarative constraint. SSDT will apply the constraint after pre-deployment runs.

**Rollback notes:**
- Change constraint back to NULL
- Backfilled data remains (but that's usually fine)

**Verification:**
```sql
-- Before Phase 2: Confirm no NULLs remain
SELECT COUNT(*) AS NullEmailCount
FROM dbo.Customer
WHERE Email IS NULL
-- Must be 0
```

---

## 17.3 Pattern: Add/Remove IDENTITY Property

**When to use:** Adding auto-increment to an existing column, or removing it

**Scenario:** Convert `PolicyId INT` to `PolicyId INT IDENTITY(1,1)`

You cannot `ALTER TABLE` to add IDENTITY. This requires a table swap.

### Phase 1 (Release N): Create New Table (Pre-Deployment Script)

```sql
-- PreDeployment script
PRINT 'Creating new Policy table with IDENTITY...'

-- Create new table structure
CREATE TABLE dbo.Policy_New
(
    PolicyId INT IDENTITY(1,1) NOT NULL,
    PolicyNumber NVARCHAR(50) NOT NULL,
    CustomerId INT NOT NULL,
    -- ... all other columns ...
    CONSTRAINT PK_Policy_New PRIMARY KEY CLUSTERED (PolicyId)
)

-- Copy data with IDENTITY_INSERT
SET IDENTITY_INSERT dbo.Policy_New ON

INSERT INTO dbo.Policy_New (PolicyId, PolicyNumber, CustomerId /*, ... */)
SELECT PolicyId, PolicyNumber, CustomerId /*, ... */
FROM dbo.Policy

SET IDENTITY_INSERT dbo.Policy_New OFF

-- Reseed to max + 1
DECLARE @MaxId INT = (SELECT MAX(PolicyId) FROM dbo.Policy_New)
DBCC CHECKIDENT ('dbo.Policy_New', RESEED, @MaxId)

PRINT 'Data migrated to new table.'
```

### Phase 2 (Release N): Swap Tables (Pre-Deployment, continued)

```sql
-- Drop FKs pointing to old table
ALTER TABLE dbo.Claim DROP CONSTRAINT FK_Claim_Policy
-- ... other FKs ...

-- Swap
DROP TABLE dbo.Policy
EXEC sp_rename 'dbo.Policy_New', 'Policy'

-- Recreate FKs
ALTER TABLE dbo.Claim ADD CONSTRAINT FK_Claim_Policy
    FOREIGN KEY (PolicyId) REFERENCES dbo.Policy(PolicyId)

PRINT 'Table swap complete.'
```

### Phase 3: Declarative Definition Matches

Your declarative table definition now shows IDENTITY:

```sql
CREATE TABLE [dbo].[Policy]
(
    [PolicyId] INT IDENTITY(1,1) NOT NULL,
    -- ...
)
```

**Rollback notes:**
- This is largely atomic if done in pre-deployment
- Full rollback = restore from backup
- Test thoroughly in lower environments

**Verification:**
```sql
-- Confirm IDENTITY is set
SELECT 
    COLUMNPROPERTY(OBJECT_ID('dbo.Policy'), 'PolicyId', 'IsIdentity') AS IsIdentity
-- Should be 1

-- Confirm row counts match
SELECT 
    (SELECT COUNT(*) FROM dbo.Policy) AS NewCount
-- Should match original
```

---

## 17.4 Pattern: Add FK with Orphan Data

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

## 17.5 Pattern: Safe Column Removal (4-Phase)

**When to use:** Removing a column safely with full verification

**Scenario:** Remove `Customer.LegacyId` that's no longer used

### Phase 1 (Release N): Soft Deprecate

Document the deprecation. Optionally rename:

```sql
-- Declarative: Rename to signal deprecation
[__deprecated_LegacyId] INT NULL,  -- Was LegacyId, use refactorlog
```

Or just add documentation/comments without schema change.

### Phase 2 (Release N): Stop Writes

Application code change — stop writing to this column. No schema change.

### Phase 3 (Release N+1): Verify Unused

```sql
-- Verification query (run manually, not in deployment)
-- Check for recent writes
SELECT MAX(UpdatedAt) AS LastWrite
FROM dbo.Customer
WHERE LegacyId IS NOT NULL

-- Check for code references (search codebase)
-- Check for report/ETL references (ask stakeholders)
```

Only proceed when confident column is truly unused.

### Phase 4 (Release N+2): Drop Column

```sql
-- Declarative: Remove from table definition
-- Column is simply gone from the CREATE TABLE statement
```

**Rollback notes:**
- Phase 1-3: Fully reversible
- Phase 4: Requires backup restore

---

## 17.6 Pattern: Table Split (Vertical Partitioning)

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

## 17.9 Pattern: CDC-Enabled Table Schema Change (Production)

**When to use:** Changing schema on a CDC-enabled table without audit gaps

**Scenario:** Add `MiddleName` column to CDC-enabled `Employee` table

### Phase 1 (Release N): Create New Capture Instance

```sql
-- PostDeployment script
PRINT 'Creating new CDC capture instance for Employee...'

-- Create new instance with new schema (after column is added)
EXEC sys.sp_cdc_enable_table
    @source_schema = 'dbo',
    @source_name = 'Employee',
    @capture_instance = 'dbo_Employee_v2',  -- Versioned name
    @role_name = 'cdc_reader',
    @supports_net_changes = 1

PRINT 'New capture instance created. Both v1 and v2 are now active.'
```

### Phase 2 (Release N): Add Column (Declarative)

```sql
-- Table definition change
[MiddleName] NVARCHAR(50) NULL,
```

The new capture instance (v2) tracks this column. The old instance (v1) doesn't know about it.

### Phase 3 (Release N): Update Consumer Abstraction

Change History code now queries both instances and unions results.

### Phase 4 (Release N+1, after retention): Drop Old Instance

```sql
-- PostDeployment script (after retention period)
PRINT 'Dropping old CDC capture instance...'

EXEC sys.sp_cdc_disable_table
    @source_schema = 'dbo',
    @source_name = 'Employee',
    @capture_instance = 'dbo_Employee_v1'

PRINT 'Old capture instance dropped.'
```

**Rollback notes:**
- Phase 1-3: Drop new instance, keep old
- Phase 4: Cannot restore dropped instance (but data is beyond retention anyway)

---

