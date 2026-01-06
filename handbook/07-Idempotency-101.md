# 7. Idempotency 101

---

## Why Idempotency Matters

An idempotent operation produces the same result whether you run it once or many times.

In the context of deployment scripts:
- **Fresh environment:** Script runs for the first time, does the work
- **Existing environment:** Script runs again, recognizes work is done, does nothing harmful
- **Retry after failure:** Script runs again after a mid-deploy crash, completes without errors

**Without idempotency:**
- Fresh deploys work
- Redeployments fail or corrupt data
- You can't safely retry failed deployments

---

## The Core Patterns

### Pattern 1: Existence Check (Most Common)

Check if the work has already been done.

```sql
-- Adding data
IF NOT EXISTS (SELECT 1 FROM dbo.Country WHERE CountryCode = 'US')
BEGIN
    INSERT INTO dbo.Country (CountryCode, CountryName) VALUES ('US', 'United States')
END

-- Updating data
IF EXISTS (SELECT 1 FROM dbo.Person WHERE MiddleName IS NULL)
BEGIN
    UPDATE dbo.Person SET MiddleName = '' WHERE MiddleName IS NULL
END

-- Deleting data
IF EXISTS (SELECT 1 FROM dbo.TempData WHERE ProcessedDate < '2024-01-01')
BEGIN
    DELETE FROM dbo.TempData WHERE ProcessedDate < '2024-01-01'
END
```

### Pattern 2: MERGE (Upsert)

Insert if missing, update if exists — in one atomic statement.

```sql
MERGE INTO dbo.OrderStatus AS target
USING (VALUES
    (1, 'Pending'),
    (2, 'Active'),
    (3, 'Complete')
) AS source (Id, Name)
ON target.StatusId = source.Id
WHEN MATCHED THEN
    UPDATE SET StatusName = source.Name
WHEN NOT MATCHED THEN
    INSERT (StatusId, StatusName) VALUES (source.Id, source.Name);
```

### Pattern 3: Conditional UPDATE with Filtering

Update only rows that need it — don't touch already-correct rows.

```sql
-- Bad: Updates all rows, even if already correct
UPDATE dbo.Customer SET IsActive = 1

-- Good: Only updates rows that need changing
UPDATE dbo.Customer SET IsActive = 1 WHERE IsActive = 0 OR IsActive IS NULL
```

### Pattern 4: Migration Tracking Table

For complex, multi-step migrations that can't be easily checked.

```sql
IF NOT EXISTS (SELECT 1 FROM dbo.MigrationHistory WHERE MigrationId = 'MIG_2025.01_SplitAddress')
BEGIN
    -- Complex migration logic
    INSERT INTO dbo.Address (CustomerId, Street, City, State, PostalCode)
    SELECT CustomerId, AddressLine1, City, StateCode, ZipCode
    FROM dbo.Customer
    WHERE AddressLine1 IS NOT NULL
    
    -- Mark complete
    INSERT INTO dbo.MigrationHistory (MigrationId, ExecutedAt, ExecutedBy)
    VALUES ('MIG_2025.01_SplitAddress', SYSUTCDATETIME(), SYSTEM_USER)
END
```

---

## Testing Your Idempotency

Before committing any script, run this mental test:

1. **Run it once** — Does it do the intended work?
2. **Run it again immediately** — Does it fail? Does it duplicate data? Does it do nothing?
3. **Run it after partial completion** — If it failed mid-way, does re-running complete the work?

**Automated check:** In your local dev process, deploy twice in a row. Both should succeed without errors.

---

## Common Idempotency Failures

| Failure | Symptom | Fix |
|---------|---------|-----|
| Missing existence check | Duplicate key error | Add `IF NOT EXISTS` |
| UPDATE without condition | Data changed on every run | Add `WHERE` clause filtering already-updated rows |
| IDENTITY_INSERT conflicts | Insert fails on second run | Use existence check or MERGE |
| Hardcoded values + auto-increment | Different IDs in different environments | Use explicit IDs for reference data, or use MERGE on natural key |
| Cumulative operations | Values keep growing | Make the operation set to a specific value, not increment |

---

## Idempotency Checklist

Before committing a pre/post-deployment script:

- [ ] Wrapped in existence check or uses MERGE
- [ ] UPDATEs filter to only rows needing change
- [ ] INSERTs check for existing records
- [ ] DELETEs can safely run when rows already gone
- [ ] Tested by running deploy twice locally
- [ ] Includes PRINT statements for observability

---

