# 17.3 Pattern: Add/Remove IDENTITY Property

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
