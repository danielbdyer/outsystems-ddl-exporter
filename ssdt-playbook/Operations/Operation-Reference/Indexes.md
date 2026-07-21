# 16.4 Working with Indexes

*In OutSystems, indexes were configured in Entity Properties. Now you define them explicitly.*

---

### Add an Index

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Create an index to improve query performance | 1 (small table) / 2 (large table) | Pure Declarative |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-preserving | Scans table to build index |
| Reversibility | Symmetric | Drop the index |
| Dependency Scope | Intra-table | Tied to table structure |
| Application Impact | Additive | Only affects performance |

**What you do:**

```sql
-- Basic
CREATE INDEX [IX_Order_CustomerId]
ON [dbo].[Order]([CustomerId])

-- Covering index
CREATE INDEX [IX_Order_CustomerId_Covering]
ON [dbo].[Order]([CustomerId])
INCLUDE ([OrderDate], [TotalAmount])

-- Filtered index
CREATE INDEX [IX_Order_Active]
ON [dbo].[Order]([OrderDate])
WHERE [Status] = 'Active'

-- Unique index
CREATE UNIQUE INDEX [UIX_Customer_Email]
ON [dbo].[Customer]([Email])
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Build time | Large tables take time to index. Consider maintenance window. |
| Blocking | Default index creation is offline (blocks writes). Enterprise Edition supports ONLINE. |
| Filtered index limitations | Only helps queries with matching predicates. Parameterized queries often don't benefit. |
| Too many indexes | Each index adds write overhead. Balance read vs. write performance. |

---

### Modify an Index

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Change index columns or properties | 2 | Pure Declarative (DROP + CREATE) |

**Common modifications:**
- Add/remove columns from key
- Add/remove INCLUDE columns
- Change from non-unique to unique (or vice versa)
- Add/remove filter

**What SSDT generates:** DROP existing index, CREATE new index

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Rebuild time | Modification = full rebuild. Same timing concerns as creation. |
| Unique → non-unique | Safe (less restrictive) |
| Non-unique → unique | May fail if duplicates exist |

---

### Remove an Index

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Drop an index | 2 | Pure Declarative |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | Index structure removed, data unchanged |
| Reversibility | Effortful | Recreating requires rebuild time |
| Dependency Scope | Intra-table | May affect query performance |
| Application Impact | Additive (structurally) | May cause performance regression |

**What you do:**

Remove the index definition. SSDT generates:
```sql
DROP INDEX [IX_Order_CustomerId] ON [dbo].[Order]
```

**Before dropping, check usage:**
```sql
SELECT 
    i.name AS IndexName,
    s.user_seeks,
    s.user_scans,
    s.user_lookups,
    s.last_user_seek
FROM sys.indexes i
LEFT JOIN sys.dm_db_index_usage_stats s 
    ON i.object_id = s.object_id AND i.index_id = s.index_id
WHERE i.object_id = OBJECT_ID('dbo.Order')
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Performance regression | Queries won't break, but may get much slower. Review query plans. |
| Unused indexes | Low usage stats may indicate safe to drop. But beware infrequent but critical queries. |

---

### Rebuild / Reorganize Index

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Maintenance operation for index health | 2-3 | Script-Only (not declarative) |

This is **operational maintenance**, not schema change. SSDT doesn't manage it.

**Reorganize (online, less impactful):**
```sql
ALTER INDEX [IX_Order_CustomerId] ON [dbo].[Order] REORGANIZE
```

**Rebuild (offline by default, more effective):**
```sql
ALTER INDEX [IX_Order_CustomerId] ON [dbo].[Order] REBUILD

-- Online (Enterprise Edition)
ALTER INDEX [IX_Order_CustomerId] ON [dbo].[Order] REBUILD WITH (ONLINE = ON)
```

**Rule of thumb:**
- 10-30% fragmentation: REORGANIZE
- >30% fragmentation: REBUILD

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Not declarative | This goes in maintenance jobs, not SSDT project. |
| Offline blocking | Default REBUILD blocks writes. Use ONLINE for production. |
| Transaction log | Rebuilds generate log. Plan accordingly. |

---
