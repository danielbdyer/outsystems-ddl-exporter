# 17.5 Pattern: Safe Column Removal (4-Phase)

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
