# 16.7 Structural Changes

*These are significant refactorings that change how data is organized. Almost always multi-phase.*

---

### Split an Entity (Vertical Partitioning)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Extract columns into a new related table | 4 | Multi-Phase |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-transforming | Data moves between tables |
| Reversibility | Effortful | Can merge back, but requires scripted work |
| Dependency Scope | Cross-boundary | All queries referencing those columns |
| Application Impact | Breaking | Query patterns must change |

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Application coordination | This is application-level refactoring. SSDT handles each step; you own orchestration. |
| Drop timing | Don't drop columns until application is fully transitioned. |

**Related:**
- Pattern: [17.6 Table Split](#176-pattern-table-split)

---

### Merge Entities (Denormalization)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Combine two tables into one | 4 | Multi-Phase |

Reverse of split. Same tier, same concerns.

**Phase sequence:**
1. Add columns to target table
2. Migrate data from source table — **prove cardinality (1:1) before the copy** (`absorbed rows == distinct parents`); a 1:many copy silently drops rows a value hash won't flag. See [17.7 Table Merge](#177-pattern-table-merge).
3. Application transitions
4. Drop source table

---

### Move an Attribute Between Entities

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Move a column from one table to another | 3-4 | Multi-Phase |

**Phase sequence:**
1. Add column to destination table
2. Migrate data — but **prove cardinality first**: the source must be 1:1 with the destination (`moved rows == distinct destination keys`) *before* the copy. A one-to-many copy silently keeps one row per parent and drops the rest with no error, and a value hash won't catch it (it only compares the rows that survived). If it's 1:many, stop — that's a design decision (which row wins?), not a matter of how it ships.
3. Application transitions
4. Drop from source table

---

### Move an Entity Between Schemas

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Move a table to a different schema namespace | 3 | Declarative + Refactorlog OR Script |

**SSDT approach:**

Change the schema in the file:
```sql
CREATE TABLE [archive].[AuditLog]  -- was [dbo].[AuditLog]
```

Use refactorlog to express the move (or script the transfer). Without it, under the production posture (`DropObjectsNotInSource=false`) the header edit is a silent **phantom move**: the publish returns `Ok`, creates an **empty** table under the new schema, and leaves the populated original stranded under the old schema (same `object_id`) — a green deploy that moved nothing. Under a drop-enabled posture the same edit drops the original and loses its rows. Either way the move didn't happen.

**Script approach (preserves object_id):**
```sql
ALTER SCHEMA archive TRANSFER dbo.AuditLog
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Refactorlog | Without it, the header edit is a phantom move under the production posture (empty new table, populated original stranded) — or a drop-and-lose under a drop-enabled posture. Not a real move either way. |
| ALTER SCHEMA TRANSFER | Single operation, preserves object_id and data. May be preferable. |
| References | All fully-qualified references break. |

**Related:**
- Pattern: [17.8 Schema Migration with Backward Compatibility](#178-pattern-schema-migration-with-backward-compatibility)

---
