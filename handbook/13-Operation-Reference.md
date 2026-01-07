# 16. Operation Reference

---

## How to Use This Section

This is your lookup reference for any database operation. It's organized by **what you're trying to do**, mirroring how you thought about changes in OutSystems.

**Each operation has three layers:**

| Layer | What It Gives You | When to Use |
|-------|-------------------|-------------|
| **Layer 1** | One-line summary, tier, mechanism | Quick classification; you've done this before |
| **Layer 2** | Full details: dimensions, steps, SSDT behavior | First time doing this operation; need specifics |
| **Layer 3** | Gotchas, edge cases, anti-patterns, related patterns | Troubleshooting; complex scenarios |

**Start with Layer 1. Go deeper only when you need to.**

---

## 16.1 Working with Entities (Tables)

*In OutSystems, these were your Entities. Now they're tables you define explicitly.*

---

### Create a New Entity

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Add a new table to the database | 1 | Pure Declarative | Enable separately if needed |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | No existing data |
| Reversibility | Symmetric | Delete the file |
| Dependency Scope | Self-contained | Nothing references it yet |
| Application Impact | Additive | Existing code unaffected |

**What you do:**

Create a new `.sql` file in `/Tables/{schema}/`:

```sql
-- /Tables/dbo/dbo.CustomerPreference.sql

CREATE TABLE [dbo].[CustomerPreference]
(
    [CustomerPreferenceId] INT IDENTITY(1,1) NOT NULL,
    [CustomerId] INT NOT NULL,
    [PreferenceKey] NVARCHAR(100) NOT NULL,
    [PreferenceValue] NVARCHAR(500) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_CustomerPreference_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [CreatedBy] NVARCHAR(128) NOT NULL CONSTRAINT [DF_CustomerPreference_CreatedBy] DEFAULT (SYSTEM_USER),
    
    CONSTRAINT [PK_CustomerPreference] PRIMARY KEY CLUSTERED ([CustomerPreferenceId]),
    CONSTRAINT [FK_CustomerPreference_Customer] FOREIGN KEY ([CustomerId]) 
        REFERENCES [dbo].[Customer]([CustomerId])
)
```

**What SSDT generates:**
```sql
CREATE TABLE [dbo].[CustomerPreference] (...)
```

Verbatim — the table doesn't exist, so SSDT creates it.

**Verification:**
- Build succeeds
- Table appears in local database after publish
- Constraints and FKs are in place

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| FK to non-existent table | If you reference a table not in your project, build fails. Add the parent table first, or add it to the same PR. |
| Missing from project | If you create the file but don't add it to the project, it won't deploy. Verify in Solution Explorer. |
| CDC enablement | New tables aren't CDC-enabled automatically. If this table needs audit tracking, add CDC enablement to post-deployment. |

**Related:**
- Template: [28.1 New Table Template](#281-new-table-template)
- If CDC needed: [12. CDC and Schema Evolution](#12-cdc-and-schema-evolution)

---

### Rename an Entity

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Change a table's name while preserving data | 3 | Declarative + Refactorlog | Instance recreation required |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | Data untouched |
| Reversibility | Symmetric | Rename back |
| Dependency Scope | Cross-boundary | Everything references tables by name |
| Application Impact | Breaking | All callers must update |

**What you do:**

1. In Visual Studio Solution Explorer, right-click the table file
2. Select **Rename**
3. Enter the new name
4. Visual Studio updates the file AND creates a refactorlog entry

**What SSDT generates (with refactorlog):**
```sql
EXEC sp_rename 'dbo.OldTableName', 'NewTableName', 'OBJECT'
```

**What SSDT generates (WITHOUT refactorlog):**
```sql
DROP TABLE [dbo].[OldTableName]
CREATE TABLE [dbo].[NewTableName] (...)
-- ALL DATA LOST
```

**Verification:**
- Check `.refactorlog` file was modified
- Schema Compare shows rename, not drop+create
- Generated script uses `sp_rename`

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Naked Rename** | Editing the file directly without refactorlog causes data loss. See [Anti-Pattern 19.1](#191-the-naked-rename). |
| Dynamic SQL | Any code that constructs table names as strings won't be caught by SSDT's analysis. Search codebase manually. |
| External systems | ETL, reports, and other systems won't update automatically. Coordinate with stakeholders. |
| CDC impact | Capture instance references old table name. Must recreate. |

**Related:**
- Anti-pattern: [19.1 The Naked Rename](#191-the-naked-rename)
- Pattern: [17.8 Schema Migration with Backward Compatibility](#178-pattern-schema-migration-with-backward-compatibility)
- Section: [9. The Refactorlog and Rename Discipline](#9-the-refactorlog-and-rename-discipline)

---

### Delete an Entity

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Remove a table and all its data permanently | 4 | Declarative (guarded by BlockOnPossibleDataLoss) | Disable before drop |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-destructive | All rows gone |
| Reversibility | Lossy | Backup restore only path back |
| Dependency Scope | Cross-boundary | FKs, views, procs, ETL, reports |
| Application Impact | Breaking | Catastrophic if anything still references |

**What you do:**

1. Follow the deprecation workflow first (soft-deprecate → verify unused → soft-delete)
2. Delete the `.sql` file from the project
3. If `DropObjectsNotInSource=True`, SSDT generates the DROP
4. If `DropObjectsNotInSource=False`, you need a pre-deployment script

**What SSDT generates:**
```sql
DROP TABLE [dbo].[TableName]
```

**Protection:** `BlockOnPossibleDataLoss=True` will halt deployment if table has rows.

**Pre-flight checklist:**
- [ ] Table has been soft-deprecated for defined period
- [ ] Query confirms zero rows or data has been archived
- [ ] `sys.dm_sql_referencing_entities` returns empty
- [ ] Text search across codebase for table name
- [ ] ETL/reporting team confirms no dependencies
- [ ] Backup verified and restoration tested
- [ ] CDC disabled on table (if enabled)

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Foreign keys | If other tables have FKs pointing to this table, drop will fail. Drop those FKs first. |
| DropObjectsNotInSource=False | In production, this setting is usually False. You'll need explicit pre-deployment script to drop. |
| CDC | Disable CDC before dropping table, otherwise CDC objects become orphaned. |
| Views/Procs | Dependent objects will break. SSDT build should catch these if they're in the project. |

**Related:**
- Pattern: [17.5 Safe Column Removal (4-Phase)](#175-pattern-safe-column-removal-4-phase) — same workflow applies to tables

---

### Archive an Entity

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Move old data to archive while preserving it | 3-4 | Multi-Phase | Affects both source and destination |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-transforming | Data moves between tables |
| Reversibility | Effortful | Can restore, but requires scripted work |
| Dependency Scope | Inter-table to Cross-boundary | FKs must be handled; queries need awareness |
| Application Impact | Breaking for archived data | Active data unaffected if partitioned correctly |

**What you do:**

This is a multi-phase operation. SSDT doesn't have "move data" — you script it.

**Phase 1: Create archive destination**
```sql
-- Declarative: Create archive table (if new)
CREATE TABLE [archive].[Order_Pre2024] (...)
```

**Phase 2: Migrate data (post-deployment)**
```sql
-- Batch to manage transaction log
WHILE 1=1
BEGIN
    DELETE TOP (10000) FROM dbo.[Order]
    OUTPUT DELETED.* INTO archive.Order_Pre2024
    WHERE OrderDate < '2024-01-01'
    
    IF @@ROWCOUNT = 0 BREAK
END
```

**Phase 3: Verify**
```sql
-- Confirm counts match
SELECT 'Source' AS Location, COUNT(*) FROM dbo.[Order] WHERE OrderDate < '2024-01-01'
UNION ALL
SELECT 'Archive', COUNT(*) FROM archive.Order_Pre2024
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Transaction log | Large data movements bloat the log. Batch operations. |
| FKs | Child records must be archived first, or FKs disabled. |
| Cross-database | If archiving to different database, no FK enforcement. Consider different backup/retention policies. |
| CDC | Both tables may need CDC consideration. Archive table typically doesn't need CDC. |

---

## 16.2 Working with Attributes (Columns)

*In OutSystems, these were Entity Attributes. Now they're columns you define in the table's `.sql` file.*

---

### Add an Attribute (Nullable)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Add a new column that allows NULL values | 1 | Pure Declarative | Instance recreation if tracking needed |

---

**Layer 2**

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

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Position | SSDT may add at end of table. If `IgnoreColumnOrder=False`, could trigger rebuild. Keep `IgnoreColumnOrder=True`. |
| CDC | If table is CDC-enabled and you want this column tracked, you must recreate the capture instance. |

**Related:**
- CDC: [18.5 CDC Impact Checker](#185-cdc-impact-checker)

---

### Add an Attribute (Required / NOT NULL)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Add a new column that requires a value | 2 | Declarative (with default) | Instance recreation if tracking needed |

---

**Layer 2**

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

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Optimistic NOT NULL** | Adding NOT NULL without a default to a populated table fails at deploy. See [Anti-Pattern 19.2](#192-the-optimistic-not-null). |
| GenerateSmartDefaults | If `True`, SSDT auto-generates defaults. Don't rely on this in production — be explicit. |
| Large tables | Adding NOT NULL with default may cause table rebuild on older SQL Server versions. Modern versions (2012+) are metadata-only for constants. |

**Related:**
- Anti-pattern: [19.2 The Optimistic NOT NULL](#192-the-optimistic-not-null)

---

### Make an Attribute Required (NULL → NOT NULL)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Make an existing nullable column required | 2-3 | Pre-Deployment + Declarative | No instance recreation needed |

---

**Layer 2**

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

**What SSDT generates:**
```sql
ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR(200) NOT NULL;
```

**Verification before deploying:**
```sql
-- Must return 0
SELECT COUNT(*) FROM dbo.Customer WHERE Email IS NULL
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Deploy fails if NULLs exist | SSDT will fail deployment, not build. Pre-validate. |
| Concurrent inserts | If app is inserting NULLs while you deploy, backfill won't catch them. Consider adding default first. |
| Index rebuild | May trigger index rebuild if column is in an index. |

**Related:**
- Pattern: [17.2 NULL → NOT NULL on Populated Table](#172-pattern-null--not-null-on-populated-table)

---

### Make an Attribute Optional (NOT NULL → NULL)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Make an existing required column optional | 1-2 | Pure Declarative | No instance recreation needed |

---

**Layer 2**

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

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Application handling | Will your application handle NULLs correctly? It wasn't expecting them before. |
| Reports/analytics | Downstream systems may not handle NULLs well. |

---

### Change an Attribute's Data Type (Implicit Conversion)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Change type when SQL Server can convert automatically | 2 | Pure Declarative | Instance recreation required |

---

**Layer 2**

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
| Dependency Scope | Inter-table | Views, procs may have type expectations |
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

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| VARCHAR → NVARCHAR | Doubles storage. May bloat indexes past limits. |
| Index key limits | Non-clustered index keys can't exceed 1700 bytes (900 in older versions). Widening columns in indexes may fail. |
| CDC | Capture instance has old type. Must recreate. |

---

### Change an Attribute's Data Type (Explicit Conversion)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Change type when data must be explicitly converted | 3-4 | Multi-Phase | Instance recreation required |

---

**Layer 2**

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

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| SSDT may try single-step | SSDT might attempt a direct ALTER that fails. Own this manually. |
| Conversion failures | Not all values may convert. Handle failures explicitly. |
| Multiple releases | This spans at least 2-3 releases typically. |

**Related:**
- Pattern: [17.1 Explicit Conversion Data Type Change](#171-pattern-explicit-conversion-data-type-change)

---

### Change an Attribute's Length (Widen)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Increase column length/precision | 2 | Pure Declarative | No instance recreation needed |

---

**Layer 2**

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

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Index key limits | Widening past 900/1700 byte limit fails if column is in index key. |
| Large tables | May trigger metadata operation or rebuild depending on SQL version. |

---

### Change an Attribute's Length (Narrow)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Decrease column length/precision | 4 | Pre-Deployment + Declarative | No instance recreation needed |

---

**Layer 2**

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

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Ambitious Narrowing** | SSDT will generate the ALTER. SQL Server will fail or truncate. Validate first. See [Anti-Pattern 19.4](#194-the-ambitious-narrowing). |
| BlockOnPossibleDataLoss | This setting should catch it if data exceeds new length. But validate anyway. |

**Related:**
- Anti-pattern: [19.4 The Ambitious Narrowing](#194-the-ambitious-narrowing)

---

### Rename an Attribute

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Change a column's name while preserving data | 3 | Declarative + Refactorlog | Instance recreation required |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | Data untouched |
| Reversibility | Symmetric | Rename back |
| Dependency Scope | Inter-table to Cross-boundary | Views, procs, app code, reports, ETL |
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

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Naked Rename** | Without refactorlog, SSDT drops column and creates new one. Data loss. See [Anti-Pattern 19.1](#191-the-naked-rename). |
| Dynamic SQL | Queries building column names as strings won't be caught. Search codebase. |
| ORM mappings | Application ORMs may have column name assumptions. |
| CDC | Capture instance references old column name. Must recreate. |

**Related:**
- Anti-pattern: [19.1 The Naked Rename](#191-the-naked-rename)
- Section: [9. The Refactorlog and Rename Discipline](#9-the-refactorlog-and-rename-discipline)

---

### Delete an Attribute

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Remove a column and all its data permanently | 3-4 | Declarative (with deprecation workflow) | Instance recreation required |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-destructive | Column values gone |
| Reversibility | Lossy | Cannot recover without backup |
| Dependency Scope | Inter-table to Cross-boundary | Views, procs, reports, ETL may reference |
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

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| BlockOnPossibleDataLoss | If column has data, deployment halts. This is protection. |
| Index dependencies | If column is in an index, drop index first (or SSDT will). |
| Computed column dependencies | If column is referenced by computed column, drop that first. |
| CDC | Even dropped columns affect capture instance. Must recreate. |

**Related:**
- Pattern: [17.5 Safe Column Removal (4-Phase)](#175-pattern-safe-column-removal-4-phase)

---

## 16.3 Working with Identifiers and References (Keys)

*In OutSystems, the Identifier was automatic and References were drawn as lines. Now you define them explicitly.*

---

### Define the Identifier (Create Primary Key)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Define the unique identifier for a table | 1 (new table) / 2 (existing) | Pure Declarative | No impact |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only (new) / Data-touching (existing — index creation scans rows) | |
| Reversibility | Symmetric | Remove constraint |
| Dependency Scope | Inter-table | FKs from other tables reference this |
| Application Impact | Additive | Enforces uniqueness going forward |

**What you do:**

```sql
-- Inline with table definition
CONSTRAINT [PK_Customer] PRIMARY KEY CLUSTERED ([CustomerId])
```

For composite keys:
```sql
CONSTRAINT [PK_OrderLine] PRIMARY KEY CLUSTERED ([OrderId], [LineNumber])
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Existing table with data | Adding PK builds clustered index. Large table = time and blocking. |
| Duplicate values | If data has duplicates, PK creation fails. Clean first. |
| Identity vs. natural key | IDENTITY columns are auto-incrementing. Natural keys must be managed by application. |

---

### Create a Reference to Another Entity (Foreign Key)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Link a column to a parent table's primary key | 2 (clean data) / 3 (orphans exist) | Declarative / Multi-Phase | No impact |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-preserving (validates existing) | SQL Server checks all existing rows |
| Reversibility | Symmetric | Remove constraint |
| Dependency Scope | Inter-table | Creates dependency between tables |
| Application Impact | Contractual | Inserts/updates now validated |

**What you do (clean data):**

```sql
CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) 
    REFERENCES [dbo].[Customer]([CustomerId])
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

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Forgotten FK Check** | If orphans exist, deploy fails. Always check first. See [Anti-Pattern 19.3](#193-the-forgotten-fk-check). |
| WITH NOCHECK | Can add FK without validation, but it's untrusted. See pattern for proper handling. |
| Large tables | FK validation scans the table. May take time. |

**Related:**
- Anti-pattern: [19.3 The Forgotten FK Check](#193-the-forgotten-fk-check)
- Pattern: [17.4 Add FK with Orphan Data](#174-pattern-add-fk-with-orphan-data)

---

### Change Cascade Behavior

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Change what happens when parent record is deleted/updated | 3 | Pure Declarative (DROP + ADD) | No impact |

---

**Layer 2**

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
CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) 
    REFERENCES [dbo].[Customer]([CustomerId])
    ON DELETE CASCADE
    ON UPDATE NO ACTION
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| CASCADE danger | Adding CASCADE means deletes propagate silently. A delete that previously failed now removes child records. |
| Audit implications | Cascaded deletes may not be captured the way direct deletes are. |
| Multi-level cascade | CASCADE can chain through multiple tables. Understand the full graph. |

---

### Remove a Reference (Drop Foreign Key)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Remove the link between tables | 2 | Pure Declarative | No impact |

---

**Layer 2**

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
ALTER TABLE [dbo].[Order] DROP CONSTRAINT [FK_Order_Customer]
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Why are you dropping? | If it's blocking something (type change, table drop), document that. If permanent, understand the data integrity implications. |
| Query optimizer | Trusted FKs help the optimizer. Dropping may affect query plans. |

---

## 16.4 Working with Indexes

*In OutSystems, indexes were configured in Entity Properties. Now you define them explicitly.*

---

### Add an Index

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Create an index to improve query performance | 1 (small table) / 2 (large table) | Pure Declarative | No impact |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-preserving | Scans table to build index |
| Reversibility | Symmetric | Drop the index |
| Dependency Scope | Intra-table | Tied to table structure |
| Application Impact | Additive | Only affects performance |

**What you do:**

```sql
-- Basic non-clustered
CREATE NONCLUSTERED INDEX [IX_Order_CustomerId]
ON [dbo].[Order]([CustomerId])

-- Covering index
CREATE NONCLUSTERED INDEX [IX_Order_CustomerId_Covering]
ON [dbo].[Order]([CustomerId])
INCLUDE ([OrderDate], [TotalAmount])

-- Filtered index
CREATE NONCLUSTERED INDEX [IX_Order_Active]
ON [dbo].[Order]([OrderDate])
WHERE [Status] = 'Active'

-- Unique index
CREATE UNIQUE NONCLUSTERED INDEX [UX_Customer_Email]
ON [dbo].[Customer]([Email])
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Build time | Large tables take time to index. Consider maintenance window. |
| Blocking | Default index creation is offline (blocks writes). Enterprise Edition supports ONLINE. |
| Filtered index limitations | Only helps queries with matching predicates. Parameterized queries often don't benefit. |
| Too many indexes | Each index adds write overhead. Balance read vs. write performance. |

---

### Modify an Index

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Change index columns or properties | 2 | Pure Declarative (DROP + CREATE) | No impact |

---

**Layer 2**

**Common modifications:**
- Add/remove columns from key
- Add/remove INCLUDE columns
- Change from non-unique to unique (or vice versa)
- Add/remove filter

**What SSDT generates:** DROP existing index, CREATE new index

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Rebuild time | Modification = full rebuild. Same timing concerns as creation. |
| Unique → non-unique | Safe (less restrictive) |
| Non-unique → unique | May fail if duplicates exist |

---

### Remove an Index

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Drop an index | 2 | Pure Declarative | No impact |

---

**Layer 2**

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

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Performance regression | Queries won't break, but may get much slower. Review query plans. |
| Unused indexes | Low usage stats may indicate safe to drop. But beware infrequent but critical queries. |

---

### Rebuild / Reorganize Index

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Maintenance operation for index health | 2-3 | Script-Only (not declarative) | No impact |

---

**Layer 2**

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

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Not declarative | This goes in maintenance jobs, not SSDT project. |
| Offline blocking | Default REBUILD blocks writes. Use ONLINE for production. |
| Transaction log | Rebuilds generate log. Plan accordingly. |

---

## 16.5 Working with Static Entities (Lookup Tables)

*In OutSystems, Static Entities had their data built in. Now you have a table structure (declarative) plus seed data (post-deployment script).*

---

### Create a Lookup Table with Seed Data

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Create a reference/code table with fixed values | 1-2 | Declarative (structure) + Post-Deployment (data) | Usually not CDC-enabled |

---

**Layer 2**

**Two pieces:**
1. Table structure (declarative `.sql` file)
2. Seed data (idempotent post-deployment script)

**Structure:**
```sql
-- /Tables/dbo/dbo.OrderStatus.sql
CREATE TABLE [dbo].[OrderStatus]
(
    [StatusId] INT NOT NULL,
    [StatusCode] NVARCHAR(20) NOT NULL,
    [StatusName] NVARCHAR(50) NOT NULL,
    [SortOrder] INT NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_OrderStatus_IsActive] DEFAULT (1),
    
    CONSTRAINT [PK_OrderStatus] PRIMARY KEY CLUSTERED ([StatusId]),
    CONSTRAINT [UQ_OrderStatus_Code] UNIQUE ([StatusCode])
)
```

**Seed data:**
```sql
-- /Scripts/PostDeployment/ReferenceData/SeedOrderStatus.sql

MERGE INTO [dbo].[OrderStatus] AS target
USING (VALUES
    (1, 'PENDING', 'Pending', 1, 1),
    (2, 'PROCESSING', 'Processing', 2, 1),
    (3, 'SHIPPED', 'Shipped', 3, 1),
    (4, 'DELIVERED', 'Delivered', 4, 1),
    (5, 'CANCELLED', 'Cancelled', 5, 1)
) AS source ([StatusId], [StatusCode], [StatusName], [SortOrder], [IsActive])
ON target.[StatusId] = source.[StatusId]
WHEN MATCHED THEN
    UPDATE SET 
        [StatusCode] = source.[StatusCode],
        [StatusName] = source.[StatusName],
        [SortOrder] = source.[SortOrder],
        [IsActive] = source.[IsActive]
WHEN NOT MATCHED THEN
    INSERT ([StatusId], [StatusCode], [StatusName], [SortOrder], [IsActive])
    VALUES (source.[StatusId], source.[StatusCode], source.[StatusName], source.[SortOrder], source.[IsActive]);
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| IDENTITY vs. explicit IDs | For lookup tables, usually use explicit IDs (no IDENTITY) so values are consistent across environments. |
| Idempotency | Use MERGE for upsert. Don't use plain INSERT. |
| FK dependencies | Seed parent tables before child tables. |

**Related:**
- Template: [28.3 Idempotent Seed Data Template](#283-idempotent-seed-data-template)

---

### Add/Modify Seed Data

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Add or update values in a lookup table | 1-2 | Post-Deployment Script | Usually not CDC-enabled |

---

**Layer 2**

**What you do:**

Edit the seed data script. Add new values or modify existing:

```sql
-- Add to the VALUES list
    (6, 'RETURNED', 'Returned', 6, 1),  -- New value
```

MERGE handles both insert (new) and update (existing).

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Deleting values | MERGE doesn't delete by default. If you need to deactivate, set `IsActive = 0` rather than deleting. |
| FK references | Can't delete values that are referenced by FKs. |

---

### Extract Values to a Lookup Table

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Convert inline values to a normalized lookup table | 3 | Multi-Phase | May affect both tables |

---

**Layer 2**

**Scenario:** `Order.Status` is `VARCHAR(20)` with values like 'Pending', 'Active'. Extract to `OrderStatus` table.

**Phase 1:** Create lookup table, populate with distinct values
**Phase 2:** Add `Order.StatusId` column
**Phase 3:** Post-deployment: populate `StatusId` from `Status` text
**Phase 4:** Application transitions to FK
**Phase 5:** Next release: drop `Status` column, add FK constraint

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Data quality | What if `Status` has typos or variations? Clean before extracting. |
| Multi-release | This spans multiple releases. Plan the sequence. |

---

## 16.6 Constraints and Validation

*OutSystems enforced some constraints automatically. Now you have explicit control.*

---

### Add a Default Value

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Set a value to use when none is provided | 1 | Pure Declarative | No impact |

---

**Layer 2**

**What you do:**
```sql
[Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Order_Status] DEFAULT ('Pending'),
```

Or for existing column:
```sql
-- Add as separate statement
ALTER TABLE [dbo].[Order] ADD CONSTRAINT [DF_Order_Status] DEFAULT ('Pending') FOR [Status]
```

SSDT generates the appropriate ALTER.

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Always name constraints | Use `DF_TableName_ColumnName`. Auto-generated names are ugly and vary. |
| Existing default | If changing, SSDT drops old and creates new. Brief window with no default. |

---

### Add a Uniqueness Rule (Unique Constraint)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Enforce distinct values in a column | 2 | Pure Declarative | No impact |

---

**Layer 2**

**What you do:**
```sql
CONSTRAINT [UQ_Customer_Email] UNIQUE ([Email])
```

**Pre-flight check:**
```sql
-- Find duplicates
SELECT Email, COUNT(*) AS Count
FROM dbo.Customer
GROUP BY Email
HAVING COUNT(*) > 1
-- Must return 0 rows
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Duplicates | Deploy fails if duplicates exist. Clean first. |
| NULLs | Standard unique constraint allows one NULL. For multiple NULLs, use filtered unique index. |

---

### Add a Validation Rule (Check Constraint)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Enforce business rules at the database level | 1-2 | Pure Declarative | No impact |

---

**Layer 2**

**What you do:**
```sql
CONSTRAINT [CK_OrderLine_PositiveQuantity] CHECK ([Quantity] > 0)
CONSTRAINT [CK_Order_ValidDates] CHECK ([EndDate] >= [StartDate])
```

**Pre-flight check:**
```sql
-- Find violations
SELECT * FROM dbo.OrderLine WHERE Quantity <= 0
-- Must return 0 rows
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Existing violations | Deploy fails if existing data violates. Clean or use WITH NOCHECK (not recommended). |
| Complex checks | Very complex checks can impact performance. Keep them simple. |

---

### Enable/Disable Constraint

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Temporarily suspend constraint enforcement | 3 | Script-Only | No impact |

---

**Layer 2**

This is **operational**, not declarative. SSDT manages existence, not enabled state.

**Disable:**
```sql
ALTER TABLE dbo.[Order] NOCHECK CONSTRAINT FK_Order_Customer
```

**Re-enable WITH validation:**
```sql
ALTER TABLE dbo.[Order] WITH CHECK CHECK CONSTRAINT FK_Order_Customer
```

**Note:** `NOCHECK` leaves the constraint untrusted. `WITH CHECK CHECK` validates and restores trust.

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Untrusted constraints | Optimizer ignores them. Always restore trust. |
| Use case | Typically for bulk loads or multi-phase migrations. |

---

## 16.7 Structural Changes

*These are significant refactorings that change how data is organized. Almost always multi-phase.*

---

### Split an Entity (Vertical Partitioning)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Extract columns into a new related table | 4 | Multi-Phase | Both tables affected |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-transforming | Data moves between tables |
| Reversibility | Effortful | Can merge back, but requires scripted work |
| Dependency Scope | Cross-boundary | All queries/procs referencing those columns |
| Application Impact | Breaking | Query patterns must change |

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Application coordination | This is application-level refactoring. SSDT handles each step; you own orchestration. |
| Drop timing | Don't drop columns until application is fully transitioned. |

**Related:**
- Pattern: [17.6 Table Split](#176-pattern-table-split)

---

### Merge Entities (Denormalization)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Combine two tables into one | 4 | Multi-Phase | Both tables affected |

---

**Layer 2**

Reverse of split. Same tier, same concerns.

**Phase sequence:**
1. Add columns to target table
2. Migrate data from source table
3. Application transitions
4. Drop source table

---

### Move an Attribute Between Entities

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Move a column from one table to another | 3-4 | Multi-Phase | Both tables affected |

---

**Layer 2**

**Phase sequence:**
1. Add column to destination table
2. Migrate data
3. Application transitions
4. Drop from source table

---

### Move an Entity Between Schemas

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Move a table to a different schema namespace | 3 | Declarative + Refactorlog OR Script | Instance recreation required |

---

**Layer 2**

**SSDT approach:**

Change the schema in the file:
```sql
CREATE TABLE [archive].[AuditLog]  -- was [dbo].[AuditLog]
```

Use refactorlog to express the move, otherwise SSDT drops and recreates.

**Script approach (preserves object_id):**
```sql
ALTER SCHEMA archive TRANSFER dbo.AuditLog
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Refactorlog | Without it, SSDT interprets as drop + create. |
| ALTER SCHEMA TRANSFER | Single operation, preserves object_id and data. May be preferable. |
| References | All fully-qualified references break. |

**Related:**
- Pattern: [17.8 Schema Migration with Backward Compatibility](#178-pattern-schema-migration-with-backward-compatibility)

---

## 16.8 Views, Synonyms, and Abstraction

*These create stable interfaces over changing structures.*

---

### Create a View

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Create a named query as a virtual table | 1 | Pure Declarative | N/A |

---

**Layer 2**

**What you do:**
```sql
-- /Views/dbo/dbo.vw_ActiveCustomer.sql
CREATE VIEW [dbo].[vw_ActiveCustomer]
AS
SELECT 
    CustomerId,
    FirstName,
    LastName,
    Email,
    CreatedAt
FROM dbo.Customer
WHERE IsActive = 1
```

**Always enumerate columns explicitly.** Never use `SELECT *`.

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The SELECT * View** | View schema is fixed at creation. New columns won't appear. See [Anti-Pattern 19.7](#197-the-select--view). |
| Dependency chain | If underlying table changes, view may break. SSDT catches this at build. |

**Related:**
- Anti-pattern: [19.7 The SELECT * View](#197-the-select--view)

---

### Create a View for Backward Compatibility

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Maintain old interface during migration | 2-3 | Part of Multi-Phase pattern | N/A |

---

**Layer 2**

**Scenario:** Renamed table `Employee` to `Staff`. Create view to maintain old name.

```sql
CREATE VIEW [dbo].[Employee]
AS
SELECT 
    StaffId AS EmployeeId,
    FirstName,
    LastName,
    Email
FROM dbo.Staff
```

Old code referencing `dbo.Employee` continues to work.

---

### Create a Synonym

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Create an alias for another object | 1-2 | Pure Declarative | N/A |

---

**Layer 2**

**What you do:**
```sql
-- Same database, different schema
CREATE SYNONYM [dbo].[Customer] FOR [sales].[Customer]

-- Cross-database
CREATE SYNONYM [dbo].[RemoteCustomer] FOR [LinkedServer].[OtherDB].[dbo].[Customer]
```

**Use cases:**
- Schema migration: leave synonym at old location
- Environment abstraction: synonym points to different targets
- Encapsulate linked server complexity

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Runtime resolution | Synonyms resolve at execution, not compile time. If target doesn't exist, runtime error. |
| Cross-database | Requires database reference in SSDT project. |

---

### Create an Indexed View (Materialized)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Physically store view results for performance | 2-3 | Pure Declarative | N/A |

---

**Layer 2**

**Requirements:**
- `WITH SCHEMABINDING` required
- Deterministic expressions only
- `COUNT_BIG(*)` not `COUNT(*)`
- No OUTER JOIN, subqueries, DISTINCT

```sql
CREATE VIEW [dbo].[vw_CustomerOrderSummary]
WITH SCHEMABINDING
AS
SELECT 
    CustomerId,
    COUNT_BIG(*) AS OrderCount,
    SUM(TotalAmount) AS TotalSpend
FROM dbo.[Order]
GROUP BY CustomerId
GO

CREATE UNIQUE CLUSTERED INDEX [IX_vw_CustomerOrderSummary]
ON [dbo].[vw_CustomerOrderSummary]([CustomerId])
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Write overhead | Every INSERT/UPDATE/DELETE on base tables updates the view. |
| Enterprise Edition | Standard Edition requires `NOEXPAND` hint to use indexed view. |

---

## 16.9 Audit and Temporal

*These patterns track changes over time — the foundation of your Change History feature.*

---

### Add System-Versioned Temporal Table

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Enable automatic history tracking | 2 (new) / 3 (existing) | Pure Declarative (new) / Multi-Phase (existing) | Different mechanism than CDC |

---

**Layer 2**

**For new table:**
```sql
CREATE TABLE [dbo].[Employee]
(
    [EmployeeId] INT NOT NULL PRIMARY KEY,
    [Name] NVARCHAR(100) NOT NULL,
    [Department] NVARCHAR(50) NOT NULL,
    
    [ValidFrom] DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo] DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.EmployeeHistory))
```

**Querying:**
```sql
-- Current state
SELECT * FROM dbo.Employee

-- Point in time
SELECT * FROM dbo.Employee FOR SYSTEM_TIME AS OF '2024-06-15'

-- Full history
SELECT * FROM dbo.Employee FOR SYSTEM_TIME ALL WHERE EmployeeId = 42
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Existing table | Converting existing table to temporal requires multi-phase approach. |
| History table | System-managed; can't directly modify. |
| SSDT table rebuilds | If SSDT needs to rebuild table (column reorder), it disables/re-enables versioning. |

---

### Add Manual Audit Columns

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Add CreatedAt, UpdatedAt, CreatedBy, UpdatedBy columns | 1 (new) / 2 (existing) | Pure Declarative / Post-Deployment for backfill | No CDC impact |

---

**Layer 2**

**Standard audit columns:**
```sql
[CreatedAt] DATETIME2(7) NOT NULL 
    CONSTRAINT [DF_Order_CreatedAt] DEFAULT (SYSUTCDATETIME()),
[CreatedBy] NVARCHAR(128) NOT NULL 
    CONSTRAINT [DF_Order_CreatedBy] DEFAULT (SYSTEM_USER),
[UpdatedAt] DATETIME2(7) NULL,
[UpdatedBy] NVARCHAR(128) NULL
```

**For existing table — backfill:**
```sql
UPDATE dbo.[Order]
SET 
    CreatedAt = ISNULL(OrderDate, '2020-01-01'),
    CreatedBy = 'MIGRATION'
WHERE CreatedAt IS NULL
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| UpdatedAt/UpdatedBy | Database won't auto-populate. Requires trigger or application code. |
| Triggers | Can add trigger for auto-update, but adds overhead. |

---

### Enable Change Data Capture

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Track row-level changes for audit/ETL | 3 | Script-Only | This IS the CDC operation |

---

**Layer 2**

**Enable for database:**
```sql
EXEC sys.sp_cdc_enable_db
```

**Enable for table:**
```sql
EXEC sys.sp_cdc_enable_table
    @source_schema = 'dbo',
    @source_name = 'Customer',
    @role_name = 'cdc_reader',
    @capture_instance = 'dbo_Customer_v1',
    @supports_net_changes = 1
```

**Query changes:**
```sql
DECLARE @from_lsn binary(10) = sys.fn_cdc_get_min_lsn('dbo_Customer_v1')
DECLARE @to_lsn binary(10) = sys.fn_cdc_get_max_lsn()

SELECT * FROM cdc.fn_cdc_get_all_changes_dbo_Customer_v1(@from_lsn, @to_lsn, 'all')
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Enterprise Edition | CDC requires Enterprise (or Developer/Eval). |
| SQL Agent | Requires SQL Agent running for capture jobs. |
| Schema changes | Any schema change on CDC table requires capture instance management. See [Section 12](#12-cdc-and-schema-evolution). |
| 🔴 **The CDC Surprise** | Schema changes without instance recreation leave history incomplete. See [Anti-Pattern 19.5](#195-the-cdc-surprise). |

**Related:**
- Section: [12. CDC and Schema Evolution](#12-cdc-and-schema-evolution)
- Anti-pattern: [19.5 The CDC Surprise](#195-the-cdc-surprise)
- Pattern: [17.9 CDC-Enabled Table Schema Change](#179-pattern-cdc-enabled-table-schema-change-production)
- Decision aid: [18.5 CDC Impact Checker](#185-cdc-impact-checker)

---

### Enable Change Tracking

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Lightweight tracking of which rows changed | 2 | Declarative (table) / Script (database) | Alternative to CDC |

---

**Layer 2**

**Simpler than CDC:** Tracks *that* rows changed, not *what* changed.

**Enable for database:**
```sql
ALTER DATABASE [YourDb]
SET CHANGE_TRACKING = ON
(CHANGE_RETENTION = 7 DAYS, AUTO_CLEANUP = ON)
```

**Enable for table:**
```sql
CREATE TABLE [dbo].[Customer]
(
    -- columns
)
WITH (CHANGE_TRACKING = ON)
```

**Query changes:**
```sql
SELECT 
    ct.CustomerId,
    ct.SYS_CHANGE_OPERATION,  -- I, U, D
    c.*
FROM CHANGETABLE(CHANGES dbo.Customer, @last_sync_version) ct
LEFT JOIN dbo.Customer c ON ct.CustomerId = c.CustomerId
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| No before/after | Only tells you row changed, not old values. |
| Sync scenarios | Good for cache invalidation, offline sync. |
| All editions | Available in all SQL Server editions. |

**CDC vs Change Tracking:**
| Aspect | CDC | Change Tracking |
|--------|-----|-----------------|
| What's tracked | Full before/after row images | Which rows changed |
| Storage | Separate change tables | Internal tracking |
| Edition | Enterprise | All editions |
| Use case | Audit, ETL | Sync, cache invalidation |
| Overhead | Higher | Lower |

---

## 16.10 Quick Lookup: All Operations by Tier

### Tier 1: Self-Service

| Operation | Mechanism |
|-----------|-----------|
| Create table | Declarative |
| Add nullable column | Declarative |
| Add default constraint | Declarative |
| Add check constraint (new table) | Declarative |
| Add index (small table) | Declarative |
| Create view | Declarative |
| Create synonym | Declarative |
| NOT NULL → NULL | Declarative |

### Tier 2: Pair-Supported

| Operation | Mechanism |
|-----------|-----------|
| Add NOT NULL column (with default) | Declarative |
| Add FK (clean data) | Declarative |
| Add unique constraint | Declarative |
| Add check constraint (existing data) | Declarative |
| Add index (large table) | Declarative |
| Widen column | Declarative |
| Change type (implicit) | Declarative |
| NULL → NOT NULL | Pre-deployment + Declarative |
| Add manual audit columns (existing) | Post-deployment |
| Enable Change Tracking | Script + Declarative |
| Create indexed view | Declarative |

### Tier 3: Dev Lead Owned

| Operation | Mechanism |
|-----------|-----------|
| Rename column | Declarative + Refactorlog |
| Rename table | Declarative + Refactorlog |
| Add FK (orphan data) | Multi-Phase |
| Change cascade behavior | Declarative |
| Drop column (with deprecation) | Multi-Phase |
| Change type (explicit) | Multi-Phase |
| Add/remove IDENTITY | Multi-Phase |
| Move table between schemas | Declarative + Refactorlog |
| Enable CDC | Script-Only |
| CDC table schema change | Multi-Phase |
| Add system-versioned temporal (existing) | Multi-Phase |
| Extract to lookup table | Multi-Phase |

### Tier 4: Principal Escalation

| Operation | Mechanism |
|-----------|-----------|
| Drop table with data | Declarative (guarded) |
| Narrow column | Pre-deployment + Declarative |
| Split table | Multi-Phase |
| Merge tables | Multi-Phase |
| Move column between tables | Multi-Phase |
| Any data-destructive operation | Varies |
| Novel/unprecedented patterns | Case-by-case |

---

## 16.11 Cross-Reference: Anti-Patterns and Patterns

| Operation | Related Anti-Pattern | Related Multi-Phase Pattern |
|-----------|---------------------|----------------------------|
| Rename column/table | [19.1 The Naked Rename](#191-the-naked-rename) | [17.8 Schema Migration with Backward Compatibility](#178-pattern-schema-migration-with-backward-compatibility) |
| Add NOT NULL column | [19.2 The Optimistic NOT NULL](#192-the-optimistic-not-null) | [17.2 NULL → NOT NULL](#172-pattern-null--not-null-on-populated-table) |
| Add FK | [19.3 The Forgotten FK Check](#193-the-forgotten-fk-check) | [17.4 Add FK with Orphan Data](#174-pattern-add-fk-with-orphan-data) |
| Narrow column | [19.4 The Ambitious Narrowing](#194-the-ambitious-narrowing) | — |
| CDC table change | [19.5 The CDC Surprise](#195-the-cdc-surprise) | [17.9 CDC-Enabled Table Schema Change](#179-pattern-cdc-enabled-table-schema-change-production) |
| Refactorlog handling | [19.6 The Refactorlog Cleanup](#196-the-refactorlog-cleanup) | — |
| Create view | [19.7 The SELECT * View](#197-the-select--view) | — |
| Change type (explicit) | — | [17.1 Explicit Conversion Data Type Change](#171-pattern-explicit-conversion-data-type-change) |
| Add/remove IDENTITY | — | [17.3 Add/Remove IDENTITY](#173-pattern-addremove-identity-property) |
| Drop column | — | [17.5 Safe Column Removal](#175-pattern-safe-column-removal-4-phase) |
| Split table | — | [17.6 Table Split](#176-pattern-table-split) |
| Merge tables | — | [17.7 Table Merge](#177-pattern-table-merge) |
