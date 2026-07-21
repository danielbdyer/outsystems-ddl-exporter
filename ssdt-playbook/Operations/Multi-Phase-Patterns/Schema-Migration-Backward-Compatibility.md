# 17.8 Pattern: Schema Migration with Backward Compatibility

**When to use:** Making a breaking change (rename, restructure) while maintaining backward compatibility during a transition period. Allows old and new code to coexist.

**Scenario:** Rename `FirstName` to `GivenName` while existing code continues to work.

---

## The Core Technique: Computed Column Compatibility

Instead of a hard cutover, you:

1. Rename the column to its new name
2. Expose the old name as a computed column that returns the new column's value
3. Transition consumers gradually
4. Remove the computed column when all consumers have migrated

---

## Rename Column with Computed Column

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

## Key Principles

**The compatibility column must be transparent:**

- SELECTs should work identically
- INSERTs/UPDATEs must target the new column name (computed columns are read-only)
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
Old Name             → New Name              → Compatibility Mechanism
Customer.FirstName   → Customer.GivenName    → Computed column FirstName
```

---

## Rollback Notes

| Phase | Rollback Approach |
|-------|-------------------|
| Phase 1 (rename + computed column) | Reverse rename, drop computed column |
| Phase 2 (consumer migration) | Revert application code |
| Phase 3 (drop computed column) | Recreate computed column; no data impact |

The compatibility layer makes rollback much safer — you can always recreate the computed column if needed.

---

## When NOT to Use This Pattern

- **Tight deadlines:** Maintaining compatibility layers adds complexity
- **No external consumers:** If you control all the code, just do a synchronized deployment
- **Trivial changes:** Overhead not worth it for low-impact changes
- **Permanent rename:** If there's no transition period needed, just rename

---
