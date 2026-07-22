# 17.3 Pattern: Add/Remove IDENTITY Property

*This roughly corresponds to toggling Auto Number on an Identifier in OutSystems, but there's no exact parallel — here it means a full table swap, since you can't ALTER a column into an IDENTITY.*

**When to use:** Adding auto-increment to an existing column, or removing it

**Scenario:** Convert `PolicyId INT` to `PolicyId INT IDENTITY(1,1)`

You cannot `ALTER TABLE` to add or remove IDENTITY. This requires a table swap — but **SSDT generates that swap itself from the one-line `.sql` edit**; you don't have to hand-write it. From the single edit (adding or removing `IDENTITY(1,1)`), the production publish emits a shadow `tmp_ms_xx_` table, copies every row across with its key preserved, `sp_rename`s it into place, and drops and recreates every foreign key touching the table (re-validated trusted). And `BlockOnPossibleDataLoss=true` **allows** it: a rebuild *moves* rows rather than dropping them, so it's data-preserving — the setting's name misleads here. So the true outcome under the production gate is a clean, atomic, key-preserving rebuild in a single publish. Preview the generated delta and confirm it's a rebuild (a `tmp_ms_xx_` shadow table and `sp_rename`) before promising anything.

The hand-scripted swap below is an **optional control**, not a requirement — reach for it when the row-by-row copy needs a scheduled window at production scale, or when you want to stage the FK drop/recreate yourself. (Mind the direction: *adding* IDENTITY needs `SET IDENTITY_INSERT ON` on the shadow to force existing keys into the new identity column; *removing* it copies into a plain `INT` and needs no `IDENTITY_INSERT`.)

### Phase 1 (Release N): Create New Table (Pre-Deployment Script)

```sql
-- PreDeployment script
PRINT 'Creating new Policy table with IDENTITY...'

-- Create new table structure
CREATE TABLE dbo.Policy_New
(
    PolicyId INT IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_Policy_New_PolicyId
            PRIMARY KEY CLUSTERED,
    PolicyNumber NVARCHAR(50) NOT NULL,
    CustomerId INT NOT NULL,
    -- ... all other columns ...
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
