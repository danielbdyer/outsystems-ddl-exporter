# 17.2 Pattern: NULL → NOT NULL on Populated Table

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
