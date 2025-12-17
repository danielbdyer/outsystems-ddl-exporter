# 17.1 Pattern: Explicit Conversion Data Type Change

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
