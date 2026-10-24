Amazing—here’s the **full “Junior SSDT Safety Playbook”** with **22 patterns**, each written as a self-contained instruction set your devs can follow. Every pattern includes:

* **When to use**
* **Guardrails**
* **Files you’ll touch**
* **Steps (SSDT)**
* **Post-Deploy (idempotent, ready-to-paste)**
* **Validation**
* **Rollback**

Conventions I’ll use below:

* Example schema/table: `dbo.Orders`
* New column example: `RequiredDate date`
* Post-deploy lives in `sql/Post-Deployment.sql` (or `:r` includes)
* Use **Generate Script (what-if)** before every publish
* Baseline profile flags for juniors: `BlockOnPossibleDataLoss=ON`, `DropObjectsNotInSource=OFF`

---

# 1) Add a New Table

**When**: Brand-new entity.
**Guardrails**: Name objects per standard; always include a PK; add MS_Description.

**Files**

* `Tables/dbo.Orders.sql`
* `sql/Post-Deployment.sql` (optional seed)

**Steps**

1. Add file `Tables/dbo.Orders.sql`:

   ```sql
   CREATE TABLE [dbo].[Orders](
     [OrderId]       INT            NOT NULL IDENTITY(1,1),
     [CustomerId]    INT            NOT NULL,
     [Status]        NVARCHAR(30)   NOT NULL CONSTRAINT [DF_Orders_Status] DEFAULT (N'New'),
     [CreatedUtc]    DATETIME2(3)   NOT NULL CONSTRAINT [DF_Orders_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
     CONSTRAINT [PK_Orders] PRIMARY KEY CLUSTERED ([OrderId])
   );
   ```
2. Add basic docs (see #15 for extended properties).

**Post-Deploy (optional seed)**

```sql
-- Idempotent seed example
IF NOT EXISTS (SELECT 1 FROM [dbo].[Orders] WHERE [OrderId] = 1)
BEGIN
  INSERT INTO [dbo].[Orders] ([CustomerId],[Status]) VALUES (100, N'New');
END
```

**Validation**

* `SELECT TOP 1 * FROM dbo.Orders;`
* `EXEC sp_help 'dbo.Orders';`

**Rollback**

* Remove the table file (and re-publish) **or** in hotfix:

  ```sql
  IF OBJECT_ID(N'dbo.Orders', N'U') IS NOT NULL DROP TABLE [dbo].[Orders];
  ```

---

# 2) Add a Column (Stage → Converge)

**When**: Extend table safely.
**Guardrails**: Stage as **NULL** first; backfill in post-deploy; converge model to **NOT NULL** in PR 2.

**Files**

* `Tables/dbo.Orders.sql`
* `sql/Post-Deployment.sql`

**Steps (PR 1 – Stage)**

1. In `dbo.Orders.sql`, add nullable:

   ```sql
   [RequiredDate] DATE NULL,
   ```
2. Post-Deploy backfill + flip:

   ```sql
   -- Backfill
   IF COL_LENGTH(N'dbo.Orders', 'RequiredDate') IS NOT NULL
   BEGIN
     UPDATE o
       SET RequiredDate = ISNULL(RequiredDate, CAST(SYSUTCDATETIME() AS date))
     FROM dbo.Orders o
     WHERE o.RequiredDate IS NULL;

     -- Flip if still nullable
     IF EXISTS (
       SELECT 1 FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.Orders')
         AND name = N'RequiredDate'
         AND is_nullable = 1
     )
     BEGIN
       ALTER TABLE dbo.Orders ALTER COLUMN RequiredDate DATE NOT NULL;
     END
   END
   ```

**Steps (PR 2 – Converge)**

* Change model to:

  ```diff
  - [RequiredDate] DATE NULL,
  + [RequiredDate] DATE NOT NULL,
  ```
* Remove or keep the guarded ALTER block (no-op once not nullable).

**Validation**

* Before PR 2: `SELECT COUNT(*) FROM dbo.Orders WHERE RequiredDate IS NULL;` → 0

**Rollback**

* Revert table file to `NULL`; keep/remove post-deploy accordingly.

---

# 3) Change Column Nullability (NULL → NOT NULL or back)

**When**: Tighten/loosen nullability.
**Guardrails**: Backfill before tightening.

**Files**

* `Tables/dbo.Orders.sql`
* `sql/Post-Deployment.sql`

**Steps (tighten)**

1. Keep model `NULL` initially.
2. Post-Deploy:

   ```sql
   -- Ensure no NULLs remain
   UPDATE dbo.Orders SET RequiredDate = CAST(SYSUTCDATETIME() AS date)
   WHERE RequiredDate IS NULL;

   -- Flip if still nullable
   IF EXISTS (
     SELECT 1 FROM sys.columns
     WHERE object_id = OBJECT_ID(N'dbo.Orders')
       AND name = N'RequiredDate'
       AND is_nullable = 1
   )
   BEGIN
     ALTER TABLE dbo.Orders ALTER COLUMN RequiredDate DATE NOT NULL;
   END
   ```
3. Converge model: set column to `NOT NULL`.

**Steps (loosen)**

* Change the model to `NULL` directly; no post-deploy needed.

**Validation**

* Tighten: `SELECT COUNT(*) FROM dbo.Orders WHERE RequiredDate IS NULL;` → 0

**Rollback**

* Revert model nullability; optional default for safety.

---

# 4) Change Column Data Type (Staged)

**When**: Type/length/precision change.
**Guardrails**: Stage + copy + swap; avoid lossy implicit conversions.

**Files**

* `Tables/dbo.Orders.sql`
* `sql/Post-Deployment.sql`

**Steps (PR 1)**

1. Add sibling column:

   ```sql
   [RequiredDate_new] DATETIME2(3) NULL,
   ```
2. Post-Deploy backfill:

   ```sql
   -- Copy with explicit convert
   IF COL_LENGTH(N'dbo.Orders', 'RequiredDate_new') IS NOT NULL
   BEGIN
     UPDATE o
       SET RequiredDate_new =
           CASE WHEN RequiredDate IS NULL
                THEN NULL
                ELSE CAST(RequiredDate AS DATETIME2(3))
           END
     FROM dbo.Orders o
     WHERE o.RequiredDate_new IS NULL;
   END
   ```

**Steps (PR 2 – Swap names)**

* Use **Refactor → Rename** (see #13):

  * Rename `RequiredDate` → `RequiredDate_old`
  * Rename `RequiredDate_new` → `RequiredDate`
* Post-Deploy (optional): drop old after window

  ```sql
  IF COL_LENGTH(N'dbo.Orders', 'RequiredDate_old') IS NOT NULL
  BEGIN
    ALTER TABLE dbo.Orders DROP COLUMN [RequiredDate_old];
  END
  ```

**Validation**

* Sample value checks; compare counts.

**Rollback**

* Keep reading from `_old`; reverse renames if needed.

---

# 5) Add / Alter DEFAULT Constraint

**When**: Supply an automatic value for inserts.
**Guardrails**: Name constraints; backfill only if business-required.

**Files**

* `Tables/dbo.Orders.sql`
* `sql/Post-Deployment.sql` (optional backfill)

**Steps**

1. In table script:

   ```sql
   [Status] NVARCHAR(30) NOT NULL
     CONSTRAINT [DF_Orders_Status] DEFAULT (N'New'),
   ```
2. Optional Post-Deploy backfill:

   ```sql
   UPDATE dbo.Orders SET [Status] = N'New' WHERE [Status] IS NULL;
   ```

**Validation**

* Insert omitting `Status` assigns `N'New'`.

**Rollback**

* Drop constraint in model:

  ```sql
  ALTER TABLE dbo.Orders DROP CONSTRAINT [DF_Orders_Status];
  ```

---

# 6) Add / Alter CHECK Constraint

**When**: Enforce a domain rule.
**Guardrails**: Clean up bad rows **before** enabling.

**Files**

* `Tables/dbo.Orders.sql`
* `sql/Post-Deployment.sql`

**Steps**

1. In table script:

   ```sql
   CONSTRAINT [CK_Orders_Status] CHECK ([Status] IN (N'New',N'Paid',N'Shipped')),
   ```
2. Post-Deploy cleanup (if legacy rows violate):

   ```sql
   UPDATE dbo.Orders SET [Status] = N'New'
   WHERE [Status] NOT IN (N'New',N'Paid',N'Shipped');
   ```

**Validation**

* Attempt invalid insert → should fail.

**Rollback**

* Drop constraint (model or guarded drop in post-deploy).

---

# 7) Add / Alter PRIMARY KEY or UNIQUE

**When**: Enforce uniqueness.
**Guardrails**: Prove no duplicates first.

**Files**

* `Tables/dbo.Orders.sql`
* `sql/Post-Deployment.sql` (pre-checks optional)

**Steps**

1. Pre-check (run locally / PR note):

   ```sql
   SELECT OrderId, COUNT(*) c
   FROM dbo.Orders GROUP BY OrderId HAVING COUNT(*) > 1;
   ```
2. In table script:

   ```sql
   CONSTRAINT [PK_Orders] PRIMARY KEY CLUSTERED ([OrderId]),
   -- or UNIQUE
   CONSTRAINT [UQ_Orders_ExternalId] UNIQUE ([ExternalId]),
   ```

**Validation**

* Inserts with duplicate keys fail.

**Rollback**

* Revert model; emergency hotfix:

  ```sql
  ALTER TABLE dbo.Orders DROP CONSTRAINT [UQ_Orders_ExternalId];
  ```

---

# 8) Add / Alter FOREIGN KEYS (NOCHECK → backfill → CHECK)

**When**: Enforce referential integrity.
**Guardrails**: On large sets, stage with NOCHECK, backfill, then CHECK.

**Files**

* `Tables/dbo.Orders.sql` (define FK)
* `sql/Post-Deployment.sql` (stage/check)

**Steps**

1. In table script (standard FK):

   ```sql
   CONSTRAINT [FK_Orders_Customers]
     FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers]([CustomerId]),
   ```
2. If large data: temporarily deploy disabled via post-deploy path:

   ```sql
   -- Ensure FK exists and is trusted after cleanup
   -- 1) Create FK (NOCHECK) if missing
   IF NOT EXISTS (
     SELECT 1 FROM sys.foreign_keys
     WHERE name = N'FK_Orders_Customers' AND parent_object_id = OBJECT_ID(N'dbo.Orders')
   )
   BEGIN
     ALTER TABLE dbo.Orders WITH NOCHECK
       ADD CONSTRAINT [FK_Orders_Customers]
       FOREIGN KEY ([CustomerId]) REFERENCES dbo.Customers([CustomerId]);
   END

   -- 2) Backfill orphans (example: delete, or map to 'Unknown' customer)
   DELETE o
   FROM dbo.Orders o
   WHERE NOT EXISTS (SELECT 1 FROM dbo.Customers c WHERE c.CustomerId = o.CustomerId);

   -- 3) Trust the FK
   ALTER TABLE dbo.Orders WITH CHECK CHECK CONSTRAINT [FK_Orders_Customers];
   ```

*(If you defined FK in the table file, skip step 1; do steps 2 & 3.)*

**Validation**

* `DBCC CHECKCONSTRAINTS('dbo.Orders')` returns none.

**Rollback**

* `ALTER TABLE dbo.Orders DROP CONSTRAINT [FK_Orders_Customers];`

---

# 9) Add Computed Column (Persisted or Not)

**When**: Derived value.
**Guardrails**: Persisted requires deterministic expression.

**Files**

* `Tables/dbo.Orders.sql`

**Steps**

```sql
[OrderYear] AS (YEAR([CreatedUtc])) PERSISTED,
```

**Post-Deploy**

* None needed (computed).

**Validation**

* `SELECT OrderId, OrderYear FROM dbo.Orders;`

**Rollback**

* Remove column from model.

---

# 10) Nonclustered Indexes (INCLUDE, simple Filter)

**When**: Speed up queries.
**Guardrails**: Avoid over-indexing; use measured filters.

**Files**

* `Indexes/dbo.Orders.IX_Orders_Customer_Status.sql` (or inline)

**Steps**

```sql
CREATE NONCLUSTERED INDEX [IX_Orders_Customer_Status]
ON [dbo].[Orders] ([CustomerId],[Status])
INCLUDE ([CreatedUtc])
WHERE [Status] <> N'New';
```

**Post-Deploy**

* None (index creation is DDL).

**Validation**

* Query plan uses the index.

**Rollback**

```sql
DROP INDEX [IX_Orders_Customer_Status] ON [dbo].[Orders];
```

---

# 11) Add / Alter View (with/without SCHEMABINDING)

**When**: Projection/security abstraction.
**Guardrails**: With schemabinding, dependencies must be stable.

**Files**

* `Views/dbo.vOrdersCompact.sql`

**Steps**

```sql
CREATE VIEW [dbo].[vOrdersCompact]
AS
SELECT o.OrderId, o.CustomerId, o.Status, o.CreatedUtc
FROM dbo.Orders o;
GO
```

*(For binding)*

```sql
CREATE VIEW [dbo].[vOrdersCompact]
WITH SCHEMABINDING
AS
SELECT o.OrderId, o.CustomerId, o.Status, o.CreatedUtc
FROM dbo.Orders AS o;
GO
CREATE UNIQUE CLUSTERED INDEX [IXC_vOrdersCompact_OrderId]
ON [dbo].[vOrdersCompact] ([OrderId]);
```

**Post-Deploy**

* None.

**Validation**

* `SELECT TOP 5 * FROM dbo.vOrdersCompact;`

**Rollback**

* Drop/alter view in model.

---

# 12) Add / Alter Stored Procedure (no dynamic SQL)

**When**: Encapsulate logic.
**Guardrails**: Deterministic; parameterized.

**Files**

* `Stored Procedures/dbo.pGetOrdersByCustomer.sql`

**Steps**

```sql
CREATE PROCEDURE [dbo].[pGetOrdersByCustomer]
  @CustomerId INT
AS
BEGIN
  SET NOCOUNT ON;
  SELECT o.OrderId, o.Status, o.CreatedUtc
  FROM dbo.Orders o
  WHERE o.CustomerId = @CustomerId
  ORDER BY o.CreatedUtc DESC;
END
```

**Post-Deploy**

* None.

**Validation**

* `EXEC dbo.pGetOrdersByCustomer @CustomerId = 100;`

**Rollback**

* Revert file or `DROP PROCEDURE` (model).

---

# 13) Refactor → Rename (table/column/schema)

**When**: Keep identity; prevent drop/recreate.
**Guardrails**: **Use SSDT Refactor**, not manual DDL.

**Files**

* The object file(s)
* `refactorlog.xml` (auto-maintained)

**Steps**

1. Right-click object → **Refactor → Rename** → new name.
2. Build; inspect diff (should not drop/recreate table).

**Post-Deploy**

* None (DACFx emits `sp_rename` or equivalent).

**Validation**

* Data intact; `OBJECT_ID` of table remains.

**Rollback**

* Refactor → Rename back.

---

# 14) Build & Publish via Profile

**When**: Deploy changes.
**Guardrails**: Never skip **Generate Script** review.

**Files**

* `*.sqlproj`
* `*.publish.xml`

**Steps**

1. **Build** project → `.dacpac`.
2. **Publish** → choose profile → **Generate Script** (review) → **Publish**.

**Post-Deploy**

* Runs automatically **after** the model diff.

**Validation**

* Deployment script only contains expected DDL.
* Smoke tests pass.

**Rollback**

* Publish prior dacpac/profile; or run generated revert script.

---

# 15) Extended Properties (MS_Description)

**When**: Document schema.
**Guardrails**: Consistent wording; keep close to objects.

**Files**

* `Docs/ExtendedProperties/dbo.Orders.Descriptions.sql` (or inline in table file)
* `sql/Post-Deployment.sql` (to call helper safely)

**Steps (inline helper)**
*(Place in a common “util” include once)*

```sql
-- DocsUtil.sql (include this once in Post-Deployment)
IF OBJECT_ID('dbo.__set_desc') IS NULL
  EXEC('CREATE PROCEDURE dbo.__set_desc
    @schema SYSNAME, @obj SYSNAME, @col SYSNAME = NULL, @desc NVARCHAR(4000) AS
    BEGIN
      SET NOCOUNT ON;
      DECLARE @sql NVARCHAR(MAX) = N'';
      IF @col IS NULL
        SET @sql = N'EXEC sys.sp_addextendedproperty @name=N''MS_Description'', @value=@d, 
          @level0type = N''SCHEMA'', @level0name = @s,
          @level1type = N''TABLE'',  @level1name = @o;';
      ELSE
        SET @sql = N'EXEC sys.sp_addextendedproperty @name=N''MS_Description'', @value=@d, 
          @level0type = N''SCHEMA'', @level0name = @s,
          @level1type = N''TABLE'',  @level1name = @o,
          @level2type = N''COLUMN'', @level2name = @c;';
      BEGIN TRY
        EXEC sp_dropextendedproperty @name=N'MS_Description',
          @level0type=N'SCHEMA',@level0name=@schema,
          @level1type=N'TABLE', @level1name=@obj,
          @level2type=CASE WHEN @col IS NULL THEN NULL ELSE N'COLUMN' END,
          @level2name=@col;
      END TRY BEGIN CATCH END CATCH;
      EXEC sp_executesql @sql, N'@s SYSNAME,@o SYSNAME,@c SYSNAME,@d NVARCHAR(4000)',
        @schema, @obj, @col, @desc;
    END');
```

**Post-Deploy (set descriptions)**

```sql
EXEC dbo.__set_desc @schema='dbo', @obj='Orders', @desc=N'Customer orders header table';
EXEC dbo.__set_desc @schema='dbo', @obj='Orders', @col='OrderId',      @desc=N'Identity PK';
EXEC dbo.__set_desc @schema='dbo', @obj='Orders', @col='CustomerId',   @desc=N'FK to Customers';
EXEC dbo.__set_desc @schema='dbo', @obj='Orders', @col='Status',       @desc=N'Workflow status';
EXEC dbo.__set_desc @schema='dbo', @obj='Orders', @col='CreatedUtc',   @desc=N'Creation timestamp (UTC)';
```

**Validation**

```sql
SELECT * FROM ::fn_listextendedproperty (NULL, 'SCHEMA','dbo','TABLE','Orders', DEFAULT, DEFAULT);
```

**Rollback**

* Re-set to prior text; or drop properties with `sp_dropextendedproperty`.

---

# 16) Schema Compare (Project ↔ DB) — Read-Only Discipline

**When**: Inspect drift, learn diffs.
**Guardrails**: Juniors should not “Update” the project from DB.

**Files**

* None (analysis)

**Steps**

1. Tools → SQL Server → **New Schema Comparison**
2. Source = **Project (.dacpac)**; Target = **Dev DB**
3. **Compare**, review object-by-object; export report if needed.

**Post-Deploy**

* N/A

**Validation**

* Confirm with team before acting on any unexpected diff.

**Rollback**

* If someone updated by mistake, **git revert** affected files.

---

# 17) Post-Deploy Seeds (Idempotent MERGE)

**When**: Reference/static data.
**Guardrails**: Idempotent only; stable business keys.

**Files**

* `sql/Post-Deployment.sql`
* `sql/Seeds/OrdersStatus.seed.sql`

**Steps**

1. Include in Post-Deploy:

   ```sql
   :r .\Seeds\OrdersStatus.seed.sql
   ```
2. `OrdersStatus.seed.sql`:

   ```sql
   -- Idempotent MERGE seed
   MERGE dbo.OrderStatus AS t
   USING (VALUES
     (N'New',     1),
     (N'Paid',    2),
     (N'Shipped', 3)
   ) AS s(Status, SortOrder)
   ON (t.Status = s.Status)
   WHEN MATCHED AND (t.SortOrder <> s.SortOrder) THEN
     UPDATE SET SortOrder = s.SortOrder
   WHEN NOT MATCHED BY TARGET THEN
     INSERT (Status, SortOrder) VALUES (s.Status, s.SortOrder)
   -- Optional: prune extra rows
   -- WHEN NOT MATCHED BY SOURCE THEN DELETE
   ;
   ```

**Validation**

* Re-publish → zero data changes (idempotent).

**Rollback**

* Edit the seed set and re-publish.

---

# 18) Computed Columns (Revisited—Documentation)

*(Covered in #9; add this doc step if used frequently.)*

**Post-Deploy (optional doc)**

```sql
EXEC dbo.__set_desc @schema='dbo', @obj='Orders', @col='OrderYear', @desc=N'Computed: YEAR(CreatedUtc)';
```

---

# 19) Table Types (UDTT) for TVPs

**When**: Procedures need TVPs.
**Guardrails**: Keep shape stable.

**Files**

* `User Defined Types/dbo.OrderLineType.sql`
* `Stored Procedures/dbo.pInsertOrderLines.sql`

**Steps**

```sql
CREATE TYPE [dbo].[OrderLineType] AS TABLE(
  [Sku]          NVARCHAR(50) NOT NULL,
  [Qty]          INT          NOT NULL,
  [UnitPrice]    DECIMAL(18,2) NOT NULL
);
GO
CREATE PROCEDURE [dbo].[pInsertOrderLines]
  @OrderId INT,
  @Lines   dbo.OrderLineType READONLY
AS
BEGIN
  SET NOCOUNT ON;
  INSERT INTO dbo.OrderLines (OrderId, Sku, Qty, UnitPrice)
  SELECT @OrderId, Sku, Qty, UnitPrice FROM @Lines;
END
```

**Post-Deploy**

* None.

**Validation**

* Build a TVP and exec the proc in dev.

**Rollback**

* Revert type and proc.

---

# 20) Simple UDFs (Scalar / Inline TVF)

**When**: Reusable pure logic.
**Guardrails**: Prefer inline TVF (optimizer-friendly).

**Files**

* `Functions/dbo.fnNormalizeStatus.sql`

**Steps (scalar)**

```sql
CREATE FUNCTION [dbo].[fnNormalizeStatus] (@s NVARCHAR(30))
RETURNS NVARCHAR(30)
AS
BEGIN
  RETURN CASE UPPER(@s)
           WHEN N'PAID' THEN N'Paid'
           WHEN N'SHIPPED' THEN N'Shipped'
           ELSE N'New'
         END;
END
```

**Steps (inline TVF)**

```sql
CREATE FUNCTION [dbo].[fnOrdersByYear](@yr INT)
RETURNS TABLE
AS RETURN
(
  SELECT OrderId, CustomerId, Status, CreatedUtc
  FROM dbo.Orders
  WHERE YEAR(CreatedUtc) = @yr
);
```

**Post-Deploy**

* None.

**Validation**

* `SELECT dbo.fnNormalizeStatus(N'paid');`
* `SELECT * FROM dbo.fnOrdersByYear(2025);`

**Rollback**

* Revert/diff the function.

---

# 21) Column-Scoped Collation Change (Rare)

**When**: Specific column needs different collation.
**Guardrails**: Coordinate; indexes may rebuild.

**Files**

* `Tables/dbo.Customers.sql`

**Steps**

```sql
-- Example: make Email case-insensitive, accent-insensitive
[Email] NVARCHAR(256) COLLATE SQL_Latin1_General_CP1_CI_AI NOT NULL,
```

**Post-Deploy**

* None (DACFx handles rebuilds).

**Validation**

```sql
SELECT SQL_VARIANT_PROPERTY([Email],'Collation') FROM dbo.Customers WHERE 1=0;
```

**Rollback**

* Restore prior collation in model.

---

# 22) Code Analysis / Convention Checks (If Enabled)

**When**: Build-time linting.
**Guardrails**: Keep ruleset meaningful; teach, don’t punish.

**Files**

* `Project Properties` → Code Analysis
* Rule set file (if used)

**Steps**

1. Enable Code Analysis in project properties.
2. Add a slim ruleset (e.g., naming, missing PK, disallow `SELECT *` in views).
3. Fix or justify suppressions in PRs.

**Post-Deploy**

* N/A

**Validation**

* CI build passes; warnings tracked.

**Rollback**

* Relax rule severity or suppress with justification.

---

## Profile & Order Quick-Sheet (for Juniors)

* **Profile flags**: `BlockOnPossibleDataLoss=ON`, `DropObjectsNotInSource=OFF`
* **Deploy order**: **Pre-Deploy** → **Model Diff** → **Post-Deploy**
* **Stage → Converge rhythm** (for #2, #3, #4):

  * PR 1: model = safe (nullable/new col), post-deploy = backfill + optional flip
  * PR 2: model converges to final (NOT NULL/new type), post-deploy flip removed/guarded

---

If you want, I can drop this into a **repo structure** with one file per pattern plus a **starter Post-Deploy** scaffold (includes, helper proc for descriptions, seed folder).
