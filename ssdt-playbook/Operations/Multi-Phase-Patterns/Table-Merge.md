# 17.7 Pattern: Table Merge (Denormalization)

**When to use:** Combining two related tables into one, typically for performance or simplification.

**Scenario:** Merge `CustomerAddress` back into `Customer` (reversing a previous split, or denormalizing for query performance).

---

## Phase 1 (Release N): Add Columns to Destination

**Declarative:** Add the columns from the source table to the destination table.

```sql
-- In dbo.Customer.sql, add:
[Street] NVARCHAR(200) NULL,
[City] NVARCHAR(100) NULL,
[State] NVARCHAR(50) NULL,
[PostalCode] NVARCHAR(20) NULL,
```

**Why nullable:** Existing Customer rows don't have this data yet. We'll populate in Phase 2.

---

## Phase 2 (Release N): Migrate Data (Post-Deployment)

```sql
-- PostDeployment script
PRINT 'Migrating address data into Customer...'

UPDATE c
SET
    c.Street = ca.Street,
    c.City = ca.City,
    c.State = ca.State,
    c.PostalCode = ca.PostalCode
FROM dbo.Customer c
INNER JOIN dbo.CustomerAddress ca ON c.CustomerId = ca.CustomerId
WHERE c.Street IS NULL  -- Idempotency: only update if not already populated

PRINT '  Migrated ' + CAST(@@ROWCOUNT AS VARCHAR) + ' addresses.'
GO
```

**Verification:**

```sql
-- Confirm migration complete
SELECT
    (SELECT COUNT(*) FROM dbo.CustomerAddress) AS SourceRows,
    (SELECT COUNT(*) FROM dbo.Customer WHERE Street IS NOT NULL) AS MigratedRows
-- These should match (assuming 1:1 relationship)
```

---

## Phase 3 (Release N or N+1): Application Transition

Application code transitions from querying `CustomerAddress` (with JOIN) to reading directly from `Customer`.

**Options:**

- Big bang: Update all code at once (risky)
- Gradual: Update code over multiple releases while both locations have data
- Compatibility shim: Keeping the old `CustomerAddress` name resolvable is out of scope for this project; for backward compatibility during the transition, apply the techniques in Pattern 17.8

---

## Phase 4 (Release N+1 or N+2): Drop Source Table

**Pre-flight checklist:**

- [ ] All application code migrated to use Customer directly
- [ ] No queries against CustomerAddress in logs/monitoring
- [ ] Verification queries confirm all data migrated
- [ ] FKs from other tables to CustomerAddress handled

**If FKs exist from other tables:**

```sql
-- Pre-deployment: Drop FKs pointing to CustomerAddress
ALTER TABLE dbo.Shipment DROP CONSTRAINT FK_Shipment_CustomerAddress
-- These FKs need to be recreated pointing to Customer, or removed entirely
```

**Declarative:** Delete the `CustomerAddress.sql` file. SSDT generates:

```sql
DROP TABLE [dbo].[CustomerAddress]
```

---

## Rollback Notes

| Phase | Rollback Approach |
|-------|-------------------|
| Phase 1 | Remove columns from Customer (data was NULL anyway) |
| Phase 2 | Leave data in both places; no harm |
| Phase 3 | Revert application code |
| Phase 4 | **Requires backup restore** — CustomerAddress is gone |

---

## Variations

**One-to-Many Relationship:**
If CustomerAddress had multiple rows per Customer (multiple addresses), you can't simply merge columns. Options:

- Merge only the "primary" address
- Use JSON column to store multiple addresses
- Don't merge (keep normalized)

**Handling NULLs:**
If some Customers have no address, decide:

- Leave address columns NULL (most common)
- Use placeholder values
- Make columns NOT NULL with defaults (requires Phase 2 to set defaults for orphan customers)

---
