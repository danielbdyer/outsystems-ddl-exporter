# 13. The Dimension Framework

---

## What This Section Covers

Every database change can be classified along four dimensions. Understanding these dimensions is how you determine the tier, identify risks, and know what to watch for.

This isn't abstract theory — it's practical risk assessment. When you can quickly answer the four dimension questions, you can classify any change.

---

## The Four Dimensions

```
┌─────────────────────────────────────────────────────────────────────────┐
│                                                                         │
│   DATA INVOLVEMENT                                                      │
│   What happens to existing data?                                        │
│                                                                         │
│   Schema-only ──── Data-preserving ──── Data-transforming ──── Data-destructive
│   (safest)                                                    (most dangerous)
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   REVERSIBILITY                                                         │
│   How hard is it to undo?                                               │
│                                                                         │
│   Symmetric ──────────────── Effortful ──────────────────────── Lossy   │
│   (trivial)                                                   (backup only)
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   DEPENDENCY SCOPE                                                      │
│   What else is affected?                                                │
│                                                                         │
│   Self-contained ──── Intra-table ──── Inter-table ──── Cross-boundary  │
│   (isolated)                                              (external systems)
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   APPLICATION IMPACT                                                    │
│   Can existing code keep working?                                       │
│                                                                         │
│   Additive ────────────────── Contractual ───────────────────── Breaking│
│   (nothing breaks)                                          (must coordinate)
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Dimension 1: Data Involvement

**Question:** What happens to existing data as a result of this change?

### Schema-Only

The change affects structure but doesn't touch existing data values.

**Examples:**
- Adding a nullable column (existing rows get NULL)
- Adding an index (structure built from data, data unchanged)
- Adding a constraint to an empty table
- Creating a new table
- Creating a view

**Risk profile:** Lowest. If something goes wrong, data is safe.

### Data-Preserving

The change affects structure, and existing data must accommodate, but no values are modified or lost.

**Examples:**
- Adding a NOT NULL column with a default (existing rows get the default value)
- Adding a FK constraint (validates existing data, doesn't change it)
- Widening a column (existing values still fit)

**Risk profile:** Low-moderate. Deployment may fail if data violates new constraints, but no data loss occurs.

### Data-Transforming

Existing data values must be converted, moved, or derived as part of the change.

**Examples:**
- Changing a column's data type (VARCHAR to DATE — values must parse)
- Splitting a table (data moves from one table to two)
- Backfilling a column from computed values
- Normalizing inline values to a lookup table

**Risk profile:** Moderate-high. Transformation logic could be wrong. Original values may be altered. Usually requires multi-phase approach.

### Data-Destructive

Information will be permanently lost as a result of this change.

**Examples:**
- Dropping a column that contains data
- Dropping a table with rows
- Narrowing a column below current max length (truncation)
- Changing type in a way that loses precision

**Risk profile:** Highest. Once done, only backup restore can recover. `BlockOnPossibleDataLoss=True` is your last line of defense.

---

## Dimension 2: Reversibility

**Question:** If we need to undo this change, how hard is it?

### Symmetric

The reverse operation is trivial and structurally identical to the forward operation.

**Examples:**
- Add nullable column → Remove column (if no data written yet)
- Create table → Drop table (if empty)
- Add index → Drop index
- NOT NULL → NULL

**Rollback:** Just reverse the declarative change and deploy again.

### Effortful

The change can be undone, but requires scripted work, coordination, or non-trivial effort.

**Examples:**
- Add NOT NULL column with default → Can remove, but default values remain in data
- Widen column → Narrowing back requires verification that data fits
- Add FK → Can remove, but orphan data may have been created since
- Rename (with refactorlog) → Rename back requires another refactorlog entry

**Rollback:** Possible, but needs planning. May need scripted migration to restore previous state.

### Lossy

The change cannot be undone without restoring from backup. Information or state has been permanently altered.

**Examples:**
- Drop column with data → Data is gone
- Drop table with rows → Data is gone
- Narrow column (truncation occurred) → Truncated data cannot be recovered
- Explicit type conversion with precision loss → Original values gone

**Rollback:** Backup restore is the only path. This is why we verify backups before Tier 4 operations.

---

## Dimension 3: Dependency Scope

**Question:** What other objects or systems are affected by this change?

### Self-Contained

The change affects only the object being modified. Nothing else references it or depends on it.

**Examples:**
- Adding a new table (nothing references it yet)
- Adding a nullable column (nothing uses it yet)
- Adding an index (improves queries but doesn't change them)

**Coordination required:** None. You can make this change in isolation.

### Intra-Table

The change affects other objects within the same table.

**Examples:**
- Changing a column that's part of an index
- Modifying a column referenced by a computed column
- Dropping a column that has a default constraint

**Coordination required:** SSDT usually handles dependencies within a table. Review generated script to ensure dependent objects are handled correctly.

### Inter-Table

The change affects objects in other tables within the same database.

**Examples:**
- Changing a column that's referenced by FKs from other tables
- Renaming a table that's joined in views
- Modifying a column used in stored procedures

**Coordination required:** Must identify and update all dependent objects. SSDT's build will catch missing references in declarative objects; search codebase for dynamic SQL.

### Cross-Boundary

The change affects systems outside the SSDT project's scope.

**Examples:**
- Renaming a column used by ETL pipelines
- Changing a table consumed by reporting tools
- Modifying structure that external APIs depend on
- Anything with CDC implications (Change History feature depends on capture instances)

**Coordination required:** Must communicate with external system owners. Timeline coordination. May require phased rollout with backward compatibility.

---

## Dimension 4: Application Impact

**Question:** Can existing application code continue to work without modification?

### Additive

The change adds capability without affecting existing behavior. Code that worked before continues to work.

**Examples:**
- Adding a nullable column (existing queries ignore it)
- Adding a new table (nothing references it yet)
- Adding an index (queries run faster, but don't break)
- Adding a default constraint (behavior on INSERT changes, but existing code still works)

**Deployment coordination:** None. SSDT can deploy independently of application changes.

### Contractual

Old and new code can coexist, but there's a contract change that must eventually be honored.

**Examples:**
- Adding a NOT NULL column (existing INSERTs must provide value, or rely on default)
- Adding a FK constraint (existing INSERTs must satisfy referential integrity)
- Changing column width (existing code must respect new limits)

**Deployment coordination:** SSDT deploys first, but application code must be updated to fully comply. A transition period where both work is possible.

### Breaking

Existing code will fail if deployed without coordinated application changes.

**Examples:**
- Removing a column that's SELECTed
- Renaming a column used in queries
- Changing a type that application code depends on
- Removing a table that's referenced

**Deployment coordination:** Must deploy SSDT and application changes together, or use multi-phase approach with backward compatibility (e.g., create view with old name pointing to new table).

---

## Recognition Heuristics

Use these question chains to quickly classify a change.

### Data Involvement Chain

```
Will any existing values be permanently lost?
  │
  ├─► YES → Data-destructive
  │
  └─► NO → Will existing values be modified or moved?
              │
              ├─► YES → Data-transforming
              │
              └─► NO → Will existing rows be validated against new constraints?
                          │
                          ├─► YES → Data-preserving
                          │
                          └─► NO → Schema-only
```

### Reversibility Chain

```
Can I simply reverse the declarative change and deploy?
  │
  ├─► YES, trivially → Symmetric
  │
  └─► NO → Is there a path back without restoring from backup?
              │
              ├─► YES, but it requires work → Effortful
              │
              └─► NO → Lossy
```

### Dependency Scope Chain

```
Does anything outside this database reference what I'm changing?
  │
  ├─► YES → Cross-boundary
  │
  └─► NO → Do other tables reference what I'm changing?
              │
              ├─► YES → Inter-table
              │
              └─► NO → Do other objects in the same table depend on this?
                          │
                          ├─► YES → Intra-table
                          │
                          └─► NO → Self-contained
```

### Application Impact Chain

```
Will any existing query or application code fail?
  │
  ├─► YES → Breaking
  │
  └─► NO → Will existing code need to change eventually to fully comply?
              │
              ├─► YES → Contractual
              │
              └─► NO → Additive
```

---

## Putting It Together: Classification Examples

### Example 1: Add Nullable Column to Customer

| Dimension | Assessment | Reasoning |
|-----------|------------|-----------|
| Data Involvement | Schema-only | Existing rows get NULL; no values touched |
| Reversibility | Symmetric | Remove column from definition, deploy |
| Dependency Scope | Self-contained | Nothing references this column yet |
| Application Impact | Additive | Existing queries continue to work |

**Conclusion:** All dimensions at lowest risk → **Tier 1**

---

### Example 2: Add NOT NULL Column to Populated Table

| Dimension | Assessment | Reasoning |
|-----------|------------|-----------|
| Data Involvement | Data-preserving | Existing rows get default value |
| Reversibility | Effortful | Can remove column, but default values persist |
| Dependency Scope | Self-contained | New column, nothing references it |
| Application Impact | Contractual | New INSERTs must provide value (or use default) |

**Conclusion:** Data-preserving + Effortful + Contractual → **Tier 2**

---

### Example 3: Rename Column

| Dimension | Assessment | Reasoning |
|-----------|------------|-----------|
| Data Involvement | Schema-only | Data values unchanged |
| Reversibility | Effortful | Need another refactorlog entry to rename back |
| Dependency Scope | Inter-table to Cross-boundary | Views, procs, app code, possibly ETL |
| Application Impact | Breaking | All queries using old name will fail |

**Conclusion:** Cross-boundary + Breaking → **Tier 3** minimum

---

### Example 4: Drop Column with Data

| Dimension | Assessment | Reasoning |
|-----------|------------|-----------|
| Data Involvement | Data-destructive | Column values will be gone |
| Reversibility | Lossy | Only backup restore can recover |
| Dependency Scope | Inter-table | Must verify nothing references it |
| Application Impact | Breaking | Any code using this column fails |

**Conclusion:** Data-destructive + Lossy → **Tier 4**

---

## The Golden Rule

**Your tier is determined by your highest-risk dimension.**

If you have three dimensions at Tier 1 levels and one at Tier 3, you're Tier 3.

This is conservative by design. The most dangerous aspect of a change determines its handling, not the average.

---

## Connecting to Tiers

The next section ([14. Ownership Tiers](#)) explains what each tier means in terms of process, review, and ownership. The dimensions tell you *where* you land; the tiers tell you *what to do* about it.

---

