# 17.2 Pattern: NULL → NOT NULL on Populated Table

*In OutSystems, flipping Is Mandatory to Yes just worked; here, on a table with rows, it takes a backfill and a guarded deployment.*

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

**If you must do it in one release:** combining the pre-deployment backfill with the declarative constraint does **not** work, and neither does Phase 2 alone after a Phase-1 backfill. Under `BlockOnPossibleDataLoss=True`, SSDT guards the change with `IF EXISTS (SELECT TOP 1 1 FROM <table>) RAISERROR(...)` placed above the `ALTER COLUMN` — the guard checks **row presence, not NULL content**, and the deploy script is generated before any pre-deployment script runs. A populated table stays blocked even after every NULL is filled. The paths that actually land:

- **Relax the guard for that one deployment.** After the zero-NULL count is proven (the verification query below), deliberately disable `BlockOnPossibleDataLoss` for the deployment that applies the constraint — a logged, reviewed decision. The backfill is necessary; the logged relaxation is what lets the constraint through.
- **Avoid tightening in place.** Add a new NOT NULL column with a default, migrate values, repoint, drop the old column. The guard never fires because no existing column is tightened.
- **An empty table** applies cleanly with no special handling — the guard's `IF EXISTS` is false.

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
