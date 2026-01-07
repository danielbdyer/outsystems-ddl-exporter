# 17.8 Pattern: Schema Migration with Backward Compatibility

**When to use:** Making a breaking change (rename, restructure) while maintaining backward compatibility during a transition period. Allows old and new code to coexist.

**Scenario:** Rename table `Employee` to `Staff` while existing code continues to work.

---

## The Core Technique: Compatibility Views/Synonyms

Instead of a hard cutover, you:

1. Make the structural change
2. Create an alias at the old name pointing to the new structure
3. Transition consumers gradually
4. Remove the alias when all consumers have migrated

---

## Approach A: Rename Table with Compatibility View

### Phase 1 (Release N): Rename Table

Use SSDT GUI rename to create refactorlog entry:

- `dbo.Employee` → `dbo.Staff`

SSDT generates:

```sql
EXEC sp_rename 'dbo.Employee', 'Staff', 'OBJECT'
```

### Phase 1 (Release N): Create Compatibility View

```sql
-- /Views/dbo/dbo.Employee.sql (new file)
CREATE VIEW [dbo].[Employee]
AS
SELECT
    StaffId AS EmployeeId,      -- Column was also renamed
    FirstName,
    LastName,
    Email,
    Department,
    HireDate
FROM dbo.Staff
```

**Result:**

- New code uses `dbo.Staff`
- Old code uses `dbo.Employee` (the view) — continues to work
- Both see the same data

### Phase 2 (Ongoing): Migrate Consumers

Update application code, reports, ETL to use `dbo.Staff`. Track progress.

### Phase 3 (Release N+X): Drop Compatibility View

When monitoring confirms no queries against `dbo.Employee`:

```sql
-- Declarative: Delete the view file
-- SSDT generates: DROP VIEW [dbo].[Employee]
```

---

## Approach B: Rename Column with Computed Column

**Scenario:** Rename `FirstName` to `GivenName` with backward compatibility.

### Phase 1 (Release N): Rename + Add Computed Column

Rename using SSDT GUI (creates refactorlog):

- `FirstName` → `GivenName`

Add computed column for backward compatibility:

```sql
[GivenName] NVARCHAR(100) NOT NULL,
[FirstName] AS ([GivenName]),  -- Computed column returns same value
```

**Result:**

- New code uses `GivenName`
- Old code uses `FirstName` (computed) — continues to work
- SELECTs work; INSERTs/UPDATEs must use `GivenName`

**Limitation:** Computed columns are read-only. If old code INSERTs specifying `FirstName`, it will fail. This approach works best for SELECT-heavy scenarios.

### Phase 2: Migrate INSERT/UPDATE code to use `GivenName`

### Phase 3: Drop computed column (follow deprecation workflow)

---

## Approach C: Synonym for Cross-Database/Schema Move

**Scenario:** Move `dbo.AuditLog` to `archive.AuditLog` with backward compatibility.

### Phase 1 (Release N): Move Table

```sql
ALTER SCHEMA archive TRANSFER dbo.AuditLog
```

Or use refactorlog if SSDT-managed.

### Phase 1 (Release N): Create Synonym

```sql
-- /Synonyms/dbo.AuditLog.sql
CREATE SYNONYM [dbo].[AuditLog] FOR [archive].[AuditLog]
```

**Result:**

- New code uses `archive.AuditLog`
- Old code uses `dbo.AuditLog` (synonym) — continues to work

### Phase 2: Migrate consumers

### Phase 3: Drop synonym

---

## Approach D: View for Structural Refactoring

**Scenario:** Split `Customer` into `Customer` + `CustomerContact`, but some reports still expect the combined structure.

### Phase 1: Perform the split (see Pattern 17.6)

### Phase 1: Create compatibility view

```sql
-- /Views/dbo/dbo.vw_CustomerLegacy.sql
CREATE VIEW [dbo].[vw_CustomerLegacy]
AS
SELECT
    c.CustomerId,
    c.CompanyName,
    c.Industry,
    cc.FirstName AS ContactFirstName,
    cc.LastName AS ContactLastName,
    cc.Email AS ContactEmail,
    cc.Phone AS ContactPhone
FROM dbo.Customer c
LEFT JOIN dbo.CustomerContact cc ON c.CustomerId = cc.CustomerId
    AND cc.IsPrimary = 1
```

**Result:** Old reports query `vw_CustomerLegacy` and see familiar structure

---

## Key Principles

**The alias must be transparent:**

- SELECTs should work identically
- INSERTs/UPDATEs work if using views with INSTEAD OF triggers (complex) or synonyms (simple)
- JOINs work

**Track migration progress:**

- Query logs for references to old name
- Grep codebase for old name
- Check ETL configurations
- Check report definitions

**Set a sunset date:**

- Compatibility layers aren't permanent
- Communicate deadline to consumers
- Remove after deadline passes

**Document the mapping:**

```
Old Name          → New Name           → Compatibility Mechanism
dbo.Employee      → dbo.Staff          → View dbo.Employee
Employee.EmployeeId → Staff.StaffId    → View column alias
dbo.AuditLog      → archive.AuditLog   → Synonym dbo.AuditLog
```

---

## Rollback Notes

| Phase | Rollback Approach |
|-------|-------------------|
| Phase 1 (rename + alias) | Reverse rename, drop alias |
| Phase 2 (consumer migration) | Revert application code |
| Phase 3 (drop alias) | Recreate alias; no data impact |

The compatibility layer makes rollback much safer — you can always recreate the alias if needed.

---

## When NOT to Use This Pattern

- **Tight deadlines:** Maintaining compatibility layers adds complexity
- **No external consumers:** If you control all the code, just do a synchronized deployment
- **Trivial changes:** Overhead not worth it for low-impact changes
- **Permanent rename:** If there's no transition period needed, just rename

---

## CDC Considerations

If the table is CDC-enabled:

- The rename changes the source table name
- Capture instance still references old table name internally
- You'll likely need to recreate the capture instance anyway
- The compatibility view doesn't affect CDC (CDC tracks the underlying table, not views)

---
