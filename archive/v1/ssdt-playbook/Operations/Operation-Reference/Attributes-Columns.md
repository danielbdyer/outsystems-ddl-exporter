# 16.2 Working with Attributes (Columns)

*In OutSystems, these were Entity Attributes. Now they're columns you define in the table's `.sql` file.*

---

### Add an Attribute (Nullable)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Add a new column that allows NULL values | 1 | Pure Declarative |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | Existing rows get NULL |
| Reversibility | Symmetric | Remove from definition |
| Dependency Scope | Self-contained | Nothing references it yet |
| Application Impact | Additive | Existing queries still work |

**What you do:**

Edit the table's `.sql` file, add the column:

```sql
-- Add within the CREATE TABLE statement
[MiddleName] NVARCHAR(50) NULL,
```

**What SSDT generates:**
```sql
ALTER TABLE [dbo].[Person] ADD [MiddleName] NVARCHAR(50) NULL;
```

You never write this ALTER. You declare; SSDT transitions.

**Verification:**
- Build succeeds
- Column appears in local database
- Existing rows have NULL for new column

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Position | SSDT may add at end of table. If `IgnoreColumnOrder=False`, could trigger rebuild. Keep `IgnoreColumnOrder=True`. |

---

### Add an Attribute (Required / NOT NULL)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Add a new column that requires a value | 2 | Declarative (with default) |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only (if default provided) | Existing rows get default value |
| Reversibility | Symmetric | Remove from definition |
| Dependency Scope | Self-contained | Nothing references it yet |
| Application Impact | Contractual | New inserts must provide value (or rely on default) |

**What you do:**

Add column with a default constraint:

```sql
[Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Customer_Status] DEFAULT ('Active'),
```

**What SSDT generates:**
```sql
ALTER TABLE [dbo].[Customer] ADD [Status] NVARCHAR(20) NOT NULL
    CONSTRAINT [DF_Customer_Status] DEFAULT ('Active');
```

SQL Server applies the default to existing rows automatically.

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Optimistic NOT NULL** | Adding NOT NULL without a default to a populated table fails at deploy. See [Anti-Pattern 19.2](#192-the-optimistic-not-null). |
| GenerateSmartDefaults | If `True`, SSDT auto-generates defaults. Don't rely on this in production — be explicit. |
| Large tables | Adding NOT NULL with default may cause table rebuild on older SQL Server versions. Modern versions (2012+) are metadata-only for constants. |

**Related:**
- Anti-pattern: [19.2 The Optimistic NOT NULL](#192-the-optimistic-not-null)

---

### Make an Attribute Required (NULL → NOT NULL)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Make an existing nullable column required | 2-3 | Pre-Deployment + Declarative + a logged guard-relaxation (see 17.2) |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-transforming | Existing NULLs must be filled |
| Reversibility | Effortful | Can flip back, but you've altered data |
| Dependency Scope | Intra-table | Constraint is local |
| Application Impact | Breaking | INSERTs/UPDATEs without value will fail |

**What you do:**

**Step 1: Pre-deployment — backfill NULLs**
```sql
PRINT 'Backfilling NULL emails...'

UPDATE dbo.Customer
SET Email = 'unknown@placeholder.com'
WHERE Email IS NULL

PRINT 'Backfilled ' + CAST(@@ROWCOUNT AS VARCHAR) + ' rows.'
```

**Step 2: Declarative — change the definition**
```sql
-- Change NULL to NOT NULL
[Email] NVARCHAR(200) NOT NULL,
```

**What SSDT generates** (under `BlockOnPossibleDataLoss=True` — note the guard checks row presence, not NULL content):
```sql
IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127);

ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR(200) NOT NULL;
```

**Verification before deploying:**
```sql
-- Must return 0
SELECT COUNT(*) FROM dbo.Customer WHERE Email IS NULL
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Deploy is blocked while the table has rows | The guard fires on row presence, not NULL content — a zero-NULL populated table is still blocked. The backfill is necessary, not sufficient; pair it with a logged `BlockOnPossibleDataLoss` relaxation for that deployment, or add-new-column → migrate → drop-old. See 17.2. |
| Concurrent inserts | If app is inserting NULLs while you deploy, backfill won't catch them. Consider adding default first. |
| Index rebuild | May trigger index rebuild if column is in an index. |

**Related:**
- Pattern: [17.2 NULL → NOT NULL on Populated Table](#172-pattern-null--not-null-on-populated-table)

---

### Make an Attribute Optional (NOT NULL → NULL)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Make an existing required column optional | 1-2 | Pure Declarative |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | No data changes needed |
| Reversibility | Effortful | Going back requires handling NULLs that may have appeared |
| Dependency Scope | Intra-table | Local constraint |
| Application Impact | Additive | Existing code still works |

**What you do:**

Change the definition:
```sql
-- Change NOT NULL to NULL
[MiddleName] NVARCHAR(50) NULL,
```

**What SSDT generates:**
```sql
ALTER TABLE [dbo].[Person] ALTER COLUMN [MiddleName] NVARCHAR(50) NULL;
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Application handling | Will your application handle NULLs correctly? It wasn't expecting them before. |
| Reports/analytics | Downstream systems may not handle NULLs well. |

---

### Change an Attribute's Data Type (Implicit Conversion)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Change type when SQL Server can convert automatically | 2 | Pure Declarative |

Implicit conversions are safe widening conversions where no data can be lost:
- `INT` → `BIGINT`
- `VARCHAR(50)` → `VARCHAR(100)`
- `VARCHAR(n)` → `NVARCHAR(n)`
- `DECIMAL(10,2)` → `DECIMAL(18,2)`

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-preserving | SQL Server converts without loss |
| Reversibility | Effortful | Reverse may not be implicit |
| Dependency Scope | Inter-table | Application code may have type expectations |
| Application Impact | Contractual | Usually works, but app type handling may differ |

**What you do:**

Change the definition:
```sql
-- INT to BIGINT
[CustomerId] BIGINT NOT NULL,
```

**What SSDT generates:**
```sql
ALTER TABLE [dbo].[Order] ALTER COLUMN [CustomerId] BIGINT NOT NULL;
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| VARCHAR → NVARCHAR | Doubles storage. May bloat indexes past limits. |
| Index key limits | Non-clustered index keys can't exceed 1700 bytes (900 in older versions). Widening columns in indexes may fail. |

---

### Change an Attribute's Data Type (Explicit Conversion)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Change type when data must be explicitly converted | 3-4 | Multi-Phase |

Explicit conversions require transformation:
- `VARCHAR` → `DATE`
- `INT` → `UNIQUEIDENTIFIER`
- `DATETIME` → `DATE`
- Any narrowing conversion

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-transforming | Values must be parsed/converted |
| Reversibility | Effortful to Lossy | Depends on whether round-trip is possible |
| Dependency Scope | Inter-table | Everything referencing this column |
| Application Impact | Breaking | Queries, parameters, application code affected |

**What you do:**

This requires multi-phase. You cannot simply change the type.

**Phase 1:** Add new column with target type
**Phase 2:** Migrate data with conversion logic
**Phase 3:** Application transitions to new column
**Phase 4:** Drop old column, rename new column

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| SSDT may try single-step | SSDT might attempt a direct ALTER that fails. Own this manually. |
| Conversion failures | Not all values may convert. Handle failures explicitly. |
| Multiple releases | This spans at least 2-3 releases typically. |

**Related:**
- Pattern: [17.1 Explicit Conversion Data Type Change](#171-pattern-explicit-conversion-data-type-change)

---

### Change an Attribute's Length (Widen)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Increase column length/precision | 2 | Pure Declarative |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-preserving | All existing values still fit |
| Reversibility | Effortful | Could narrow back, but must verify data fits |
| Dependency Scope | Intra-table | Indexes may rebuild |
| Application Impact | Additive | Existing code continues to work |

**What you do:**

Change the definition:
```sql
-- VARCHAR(50) to VARCHAR(100)
[Email] NVARCHAR(200) NOT NULL,  -- was NVARCHAR(100)
```

**What SSDT generates:**
```sql
ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR(200) NOT NULL;
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Index key limits | Widening past 900/1700 byte limit fails if column is in index key. |
| Large tables | May trigger metadata operation or rebuild depending on SQL version. |

---

### Change an Attribute's Length (Narrow)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Decrease column length/precision | 4 | Pre-Deployment + Declarative |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-destructive | Values exceeding new length will truncate or fail |
| Reversibility | Lossy | Truncated data is gone forever |
| Dependency Scope | Intra-table | Indexes, plus application expectations |
| Application Impact | Breaking | App may attempt values that no longer fit |

**What you do:**

**Step 1: Validate data fits**
```sql
-- Check current max length
SELECT MAX(LEN(Email)) AS MaxLength FROM dbo.Customer

-- Find values that won't fit
SELECT CustomerId, Email, LEN(Email) AS Length
FROM dbo.Customer
WHERE LEN(Email) > 100  -- New limit
```

**Step 2: Handle violations** (pre-deployment or fix data)

**Step 3: Change definition**
```sql
[Email] NVARCHAR(100) NOT NULL,  -- was NVARCHAR(200)
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Ambitious Narrowing** | SSDT will generate the ALTER. SQL Server will fail or truncate. Validate first. See [Anti-Pattern 19.4](#194-the-ambitious-narrowing). |
| BlockOnPossibleDataLoss | This setting should catch it if data exceeds new length. But validate anyway. |

**Related:**
- Anti-pattern: [19.4 The Ambitious Narrowing](#194-the-ambitious-narrowing)

---

### Rename an Attribute

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Change a column's name while preserving data | 3 | Declarative + Refactorlog |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | Data untouched |
| Reversibility | Symmetric | Rename back |
| Dependency Scope | Inter-table to Cross-boundary | App code, reports, ETL |
| Application Impact | Breaking | All callers must update |

**What you do:**

1. In Visual Studio, open the table file
2. Right-click on the column name
3. Select **Rename**
4. Enter new name
5. Visual Studio updates the file AND creates refactorlog entry

**What SSDT generates (with refactorlog):**
```sql
EXEC sp_rename 'dbo.Person.FirstName', 'GivenName', 'COLUMN'
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Naked Rename** | Without refactorlog, SSDT plans to drop the old column and add a new empty one. Under `BlockOnPossibleDataLoss=true` that drop-and-add is **refused** (the row-presence guard, `Msg 50000`) — the deploy blocks and the data survives; it's lost only if the guard is relaxed. Either way the rename didn't happen. Use the refactorlog / `sp_rename` for a metadata rename. See [Anti-Pattern 19.1](#191-the-naked-rename). |
| Dynamic SQL | Queries building column names as strings won't be caught. Search codebase. |
| ORM mappings | Application ORMs may have column name assumptions. |

**Related:**
- Anti-pattern: [19.1 The Naked Rename](#191-the-naked-rename)
- Section: [9. The Refactorlog and Rename Discipline](#9-the-refactorlog-and-rename-discipline)

---

### Delete an Attribute

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Remove a column and all its data permanently | 3-4 | Declarative (with deprecation workflow) |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-destructive | Column values gone |
| Reversibility | Lossy | Cannot recover without backup |
| Dependency Scope | Inter-table to Cross-boundary | Reports, ETL may reference |
| Application Impact | Breaking | Anything referencing this column will fail |

**What you do:**

Follow the 4-phase deprecation workflow:

**Phase 1: Soft-deprecate** — Document or rename to signal deprecation
**Phase 2: Stop writes** — Application stops using the column
**Phase 3: Verify unused** — Query confirms no recent writes, no dependencies
**Phase 4: Drop** — Remove from table definition

**Verification before Phase 4:**
```sql
-- Check for dependencies
SELECT 
    referencing_entity_name, 
    referencing_class_desc
FROM sys.dm_sql_referencing_entities('dbo.Customer', 'OBJECT')

-- Check for recent data (if tracking exists)
SELECT MAX(UpdatedAt) FROM dbo.Customer WHERE LegacyColumn IS NOT NULL
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| BlockOnPossibleDataLoss | If column has data, deployment halts. This is protection. |
| Index dependencies | If column is in an index, drop index first (or SSDT will). |
| Computed column dependencies | If column is referenced by computed column, drop that first. |

**Related:**
- Pattern: [17.5 Safe Column Removal (4-Phase)](#175-pattern-safe-column-removal-4-phase)

---
