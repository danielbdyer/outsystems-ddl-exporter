# 16.3 Working with Identifiers and References (Keys)

*In OutSystems, the Identifier was automatic and References were drawn as lines. Now you define them explicitly.*

---

### Define the Identifier (Create Primary Key)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Define the unique identifier for a table | 1 (new table) / 2 (existing) | Pure Declarative |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only (new) / Data-touching (existing — index creation scans rows) | |
| Reversibility | Symmetric | Remove constraint |
| Dependency Scope | Inter-table | FKs from other tables reference this |
| Application Impact | Additive | Enforces uniqueness going forward |

**What you do:**

```sql
-- Inline, laddered beneath the column
[CustomerId] INT NOT NULL
    CONSTRAINT [PK_Customer_CustomerId]
        PRIMARY KEY CLUSTERED,
```

For composite keys, the constraint goes at table level (after the columns):
```sql
CONSTRAINT [PK_OrderLine_OrderId_LineNumber]
    PRIMARY KEY ([OrderId], [LineNumber])
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Existing table with data | Adding PK builds clustered index. Large table = time and blocking. |
| Duplicate values | If data has duplicates, PK creation fails. Clean first. |
| Identity vs. natural key | IDENTITY columns are auto-incrementing. Natural keys must be managed by application. |

---

### Create a Reference to Another Entity (Foreign Key)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Link a column to a parent table's primary key | 2 (clean data) / 3 (orphans exist) | Declarative / Multi-Phase |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-preserving (validates existing) | SQL Server checks all existing rows |
| Reversibility | Symmetric | Remove constraint |
| Dependency Scope | Inter-table | Creates dependency between tables |
| Application Impact | Contractual | Inserts/updates now validated |

**What you do (clean data):**

```sql
[CustomerId] INT NOT NULL
    CONSTRAINT [FK_Order_Customer_CustomerId]
        FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([CustomerId]),
```

**Pre-flight check:**
```sql
-- Find orphans
SELECT o.OrderId, o.CustomerId
FROM dbo.[Order] o
LEFT JOIN dbo.Customer c ON o.CustomerId = c.CustomerId
WHERE c.CustomerId IS NULL
-- Must return 0 rows
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Forgotten FK Check** | An orphan doesn't cleanly block: SSDT adds the FK **`WITH NOCHECK`** (so it lands), then **`WITH CHECK CHECK`** fails on the orphan (`Msg 547`) and leaves the constraint present but **untrusted** (`is_not_trusted=1`) — the orphan survives and the optimizer ignores the key. Reconcile the orphans first, then `WITH CHECK CHECK` to end trusted (`is_not_trusted=0`); drop or re-validate any untrusted FK a prior attempt left behind. Always check first. See [Anti-Pattern 19.3](#193-the-forgotten-fk-check). |
| WITH NOCHECK | Can add FK without validation, but it's untrusted. See pattern for proper handling. |
| Large tables | FK validation scans the table. May take time. |

**Related:**
- Anti-pattern: [19.3 The Forgotten FK Check](#193-the-forgotten-fk-check)
- Pattern: [17.4 Add FK with Orphan Data](#174-pattern-add-fk-with-orphan-data)

---

### Change Cascade Behavior

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Change what happens when parent record is deleted/updated | 3 | Pure Declarative (DROP + ADD) |

**Options:**
| Setting | On DELETE | On UPDATE |
|---------|-----------|-----------|
| `NO ACTION` (default) | Fail if children exist | Fail if children reference old value |
| `CASCADE` | Delete all children automatically | Update all children automatically |
| `SET NULL` | Set FK column to NULL | Set FK column to NULL |
| `SET DEFAULT` | Set FK column to default | Set FK column to default |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | Behavior change, not data change |
| Reversibility | Symmetric | Change back |
| Dependency Scope | Inter-table | Affects delete/update behavior across tables |
| Application Impact | Contractual to Breaking | Deletes now cascade — could be surprising |

**What you do:**

```sql
[CustomerId] INT NOT NULL
    CONSTRAINT [FK_Order_Customer_CustomerId]
        FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([CustomerId])
            ON DELETE CASCADE
            ON UPDATE NO ACTION,
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| CASCADE danger | Adding CASCADE means deletes propagate silently. A delete that previously failed now removes child records. |
| Audit implications | Cascaded deletes may not be captured the way direct deletes are. |
| Multi-level cascade | CASCADE can chain through multiple tables. Understand the full graph. |

---

### Remove a Reference (Drop Foreign Key)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Remove the link between tables | 2 | Pure Declarative |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | Data unchanged |
| Reversibility | Effortful | Adding back requires data validation |
| Dependency Scope | Inter-table | Removes linkage |
| Application Impact | Additive | Less restrictive |

**What you do:**

Remove the constraint from the table definition. SSDT generates:
```sql
ALTER TABLE [dbo].[Order] DROP CONSTRAINT [FK_Order_Customer_CustomerId]
```

Unlike deleting a whole table (a phantom under the production posture), a foreign key removed from the model **does** drop on publish — DacFx's `DropConstraintsNotInSource` defaults to True, so the granular removal happens even with `DropObjectsNotInSource=false`.

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Why are you dropping? | If it's blocking something (type change, table drop), document that. If permanent, understand the data integrity implications. |
| Query optimizer | Trusted FKs help the optimizer. Dropping may affect query plans. |

---
