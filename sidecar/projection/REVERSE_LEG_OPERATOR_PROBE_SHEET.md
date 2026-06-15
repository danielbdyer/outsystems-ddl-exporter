# Operator Probe Sheet — The Remaining Unknowns for the Reverse-Leg Data Load

> **What this is.** A self-contained, runnable investigation sheet — modelled on the J5 capability
> probe sheet — naming the facts about **your two databases** that the engine cannot determine on
> its own. Each item is framed from the agent's side: *"here is something I cannot know without
> your data; if you answer it, here is the specific engine outcome it unlocks, and here is exactly
> how to find the answer."* The data-movement engine (two-phase, topologically-ordered, set-based
> surrogate-capture with FK re-keying, bounded-memory streaming, chunk-resume, and reconcile-by-
> business-key for the user directory) is **built and tested against mock data**; what it has never
> seen is the **shape, scale, and grant posture of your real estate**. These probes settle that.
>
> **Generic by design.** Nothing here names a product, environment, or table — substitute your own.
> Two databases are involved: the **SOURCE** (the system of record the data lives in today) and the
> **TARGET** (where it is being loaded). Most probes are **read-only catalog queries**; the few
> **WRITE-PROBES** are explicitly transactional and roll back — *nothing persists*.
>
> **There are two *classes* of TARGET** (and the engine must interact with each differently — see
> `DATABASE_ARCHETYPES.md`): a **full-rights** database (on-prem SQL Server the migration team owns —
> DDL + DML, receives the emitted schema and a copy of the migrated data) and a **managed-DML** database
> (a platform-managed sink — DML-only, no DDL/`ALTER`/`IDENTITY_INSERT`). Parts A–D are
> **archetype-agnostic** (they probe schema/data/capability shape regardless of class). **Part E**
> determines *which archetype a target is* — because that single fact reshapes the identity strategy,
> the resume mechanism, the schema-deploy lane, and the rollback channel. The managed-DML profile was
> settled by the earlier capability spike; the full-rights profile is what Part E verifies.
>
> **How to use it.** (1) Fill the Binding Sheet (§0) once. (2) Run each probe against the database
> it names (SOURCE / TARGET / BOTH). (3) Record the verdict in the Ledger (§5). Only the **verdicts**
> need to travel back — never table names, row data, or credentials.

---

## §0 — Binding Sheet (resolve once, then substitute into every probe)

| Placeholder | Meaning | Example |
|---|---|---|
| `{{ENTITY_LIKE}}` | A `LIKE` pattern matching the **business tables you intend to move** (so the surveys sweep them). Use `'%'` for "all user tables" and rely on the system-object exclusion already in each query, or a prefix/schema filter. | `'APP\_%' ESCAPE '\'` |
| `{{SCHEMA}}` | Schema of a specific table under investigation. | `dbo` |
| `{{TABLE}}` | A specific, representative business table (ideally the largest — see B1). | `Customer` |
| `{{PK}}` | That table's primary-key column. | `Id` |
| `{{USER_TABLE}}` | The table holding the **user / principal directory** (the reconcile target — the rows you do **not** re-import but re-key FKs to). | `User` |
| `{{EMAIL}}` | The **business-key column** used to reconcile users across the two databases. | `Email` |
| `{{CHILD}}` / `{{FK}}` / `{{PARENT}}` / `{{PARENT_PK}}` | A child table, its FK column, the parent table, and the parent's PK — for the orphan probe (B3). | `Order` / `CustomerId` / `Customer` / `Id` |

> The agent's note: I derive the FK graph, dispositions, and load order **structurally** from the
> schema — but only your data can tell me the **scale**, the **fidelity** (orphans, duplicate keys,
> casing), and the **grant/runtime posture**. Those are the unknowns below.

---

## Part A — Schema shape *(determines the per-table strategy and the named refusals)*

The engine classifies each table's identity strategy and decides whether a clean two-phase load is
even possible. These probes tell me which strategy each table lands on and which tables I will
**refuse by name** (so you hear it before the run, never after).

### A1 — Is every business table's primary key a single IDENTITY (auto-number) column?
- **What I can't know:** whether each table mints its own surrogate key (auto-number) or carries a
  natural/business key.
- **What it unlocks:** the **identity disposition** per table. A single IDENTITY PK ⇒ the sink mints
  the key and I capture+remap it (the proven path). A non-IDENTITY PK ⇒ the source key must either be
  preserved (which a DML-only grant forbids — see C1) **or** reconciled by a business rule; a table
  here that is *not* the user directory is a table I currently cannot move and must flag.
- **Probe (run on TARGET):**
  ```sql
  SELECT  s.name AS [schema], t.name AS [table],
          COUNT(ic.column_id)                                   AS pk_col_count,
          MAX(CASE WHEN c.is_identity = 1 THEN 1 ELSE 0 END)    AS pk_is_identity
  FROM    sys.tables   t
  JOIN    sys.schemas  s  ON s.schema_id = t.schema_id
  JOIN    sys.indexes  i  ON i.object_id = t.object_id AND i.is_primary_key = 1
  JOIN    sys.index_columns ic ON ic.object_id = t.object_id AND ic.index_id = i.index_id
  JOIN    sys.columns  c  ON c.object_id = t.object_id AND c.column_id = ic.column_id
  WHERE   t.name LIKE '{{ENTITY_LIKE}}'
  GROUP BY s.name, t.name
  ORDER BY pk_col_count DESC, pk_is_identity;
  ```
- **Read-out:** `pk_col_count = 1, pk_is_identity = 1` → mint-and-remap (good). `pk_is_identity = 0`
  → business-key table (flag unless it is the user directory). Any business table **absent from the
  result entirely** has **no primary key** → I have no surrogate to capture; flag it.

### A2 — Are there any composite (multi-column) primary keys?
- **What I can't know:** whether any table keys on more than one column.
- **What it unlocks:** the **`compositeSurrogateUnsupported` refusal**. Surrogate capture is
  single-column; a composite key would be truncated, so I refuse rather than half-capture it. Knowing
  the list lets us plan each (natural-key reconcile, or exclude) before the run.
- **Probe (run on TARGET):**
  ```sql
  SELECT  s.name AS [schema], t.name AS [table], COUNT(*) AS pk_columns
  FROM    sys.indexes i
  JOIN    sys.tables  t ON t.object_id = i.object_id
  JOIN    sys.schemas s ON s.schema_id = t.schema_id
  JOIN    sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
  WHERE   i.is_primary_key = 1 AND t.name LIKE '{{ENTITY_LIKE}}'
  GROUP BY s.name, t.name
  HAVING  COUNT(*) > 1
  ORDER BY pk_columns DESC;
  ```
- **Read-out:** every row is a table I will refuse under mint-and-remap. Empty = clean.

### A3 — What is the foreign-key graph, and which FK columns are nullable / self-referential?
- **What I can't know:** the FK edges between your tables and whether each FK column is nullable —
  the input to load ordering and cycle-breaking.
- **What it unlocks:** the **load order** (topological), the **deferred-column** plan (nullable cycle
  FKs are NULLed in phase 1, re-pointed in phase 2), and the **`unbreakableCycleFk` refusal** (a
  **non-nullable** FK whose target is back inside its own dependency cycle cannot be satisfied by a
  clean two-phase load).
- **Probe (run on TARGET):**
  ```sql
  SELECT  rs.name AS ref_schema, rt.name AS referencing_table, rc.name AS referencing_col,
          rc.is_nullable,
          ts.name AS tgt_schema, tt.name AS referenced_table,
          CASE WHEN rt.object_id = tt.object_id THEN 'self' ELSE 'cross' END AS edge_kind
  FROM    sys.foreign_keys fk
  JOIN    sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
  JOIN    sys.tables  rt ON rt.object_id = fk.parent_object_id
  JOIN    sys.schemas rs ON rs.schema_id = rt.schema_id
  JOIN    sys.columns rc ON rc.object_id = fkc.parent_object_id AND rc.column_id = fkc.parent_column_id
  JOIN    sys.tables  tt ON tt.object_id = fk.referenced_object_id
  JOIN    sys.schemas ts ON ts.schema_id = tt.schema_id
  WHERE   rt.name LIKE '{{ENTITY_LIKE}}' OR tt.name LIKE '{{ENTITY_LIKE}}'
  ORDER BY referenced_table, referencing_table;
  ```
- **Read-out:** a **non-nullable `self` edge** is an unbreakable self-cycle (refusal). For `cross`
  edges I compute cycles myself — but a non-nullable FK participating in any mutual A→B→A pair is the
  risk; hand me this edge list + nullability and I will tell you exactly which (if any) refuse.

### A4 — Are there IDENTITY columns that are **not** the primary key?
- **What I can't know:** whether any table has a natural PK *and* a separate auto-number column.
- **What it unlocks:** the **IDENTITY-insert bracketing** decision. Writing any explicit value into an
  IDENTITY column requires a privilege that a DML-only grant typically denies (C1); a non-PK IDENTITY
  column means the row's INSERT touches that column and may be rejected. I need to know these tables to
  confirm the column is sink-minted (omitted from the INSERT), not source-supplied.
- **Probe (run on TARGET):**
  ```sql
  SELECT  s.name AS [schema], t.name AS [table], c.name AS identity_col,
          CASE WHEN ic.column_id IS NOT NULL THEN 1 ELSE 0 END AS is_primary_key
  FROM    sys.columns c
  JOIN    sys.tables  t ON t.object_id = c.object_id
  JOIN    sys.schemas s ON s.schema_id = t.schema_id
  LEFT JOIN sys.indexes i ON i.object_id = t.object_id AND i.is_primary_key = 1
  LEFT JOIN sys.index_columns ic
         ON ic.object_id = t.object_id AND ic.index_id = i.index_id AND ic.column_id = c.column_id
  WHERE   c.is_identity = 1 AND t.name LIKE '{{ENTITY_LIKE}}'
  ORDER BY t.name;
  ```
- **Read-out:** `is_primary_key = 0` rows are natural-PK-plus-autonumber tables — confirm the
  autonumber is sink-minted (so I omit it on INSERT) and the natural PK is what we key on.

### A5 — Which columns are non-insertable (computed / generated / rowversion)?
- **What I can't know:** whether any column is computed or system-generated and therefore must be
  excluded from the INSERT column list.
- **What it unlocks:** the **INSERT column list correctness**. Inserting into a computed or
  `rowversion`/`timestamp` column raises an error; I must skip them. Knowing the set confirms the
  generated column list is right for your tables.
- **Probe (run on TARGET):**
  ```sql
  SELECT  s.name AS [schema], t.name AS [table], c.name AS [column],
          c.is_computed, ty.name AS data_type
  FROM    sys.columns c
  JOIN    sys.types   ty ON ty.user_type_id = c.user_type_id
  JOIN    sys.tables  t  ON t.object_id = c.object_id
  JOIN    sys.schemas s  ON s.schema_id = t.schema_id
  WHERE   t.name LIKE '{{ENTITY_LIKE}}'
    AND  (c.is_computed = 1 OR ty.name IN ('timestamp','rowversion'))
  ORDER BY t.name;
  ```
- **Read-out:** every row is a column the load must omit. Empty = all columns are plain insertable.

---

## Part B — Data shape *(sizes the run and surfaces fidelity hazards)*

These determine how much memory the run needs, how long it takes, and where the data itself will
force a drop or an ambiguous match.

### B1 — How many rows are in each table (and is the target already populated)?
- **What I can't know:** the scale — total rows, per-table distribution, and whether the target is
  blank or pre-loaded.
- **What it unlocks:** the whole **run plan**: the throughput estimate, the bench slice (D1), the
  memory ceiling (B2), and whether a re-run **appends** (a populated target is idempotent only via the
  resume journal — which the engine now *requires* on a streaming execute) or needs a wipe first.
- **Probe (run on BOTH — counts are scan-free via partition stats):**
  ```sql
  SELECT  s.name AS [schema], t.name AS [table], SUM(p.row_count) AS [rows]
  FROM    sys.dm_db_partition_stats p
  JOIN    sys.tables  t ON t.object_id = p.object_id
  JOIN    sys.schemas s ON s.schema_id = t.schema_id
  WHERE   p.index_id IN (0,1) AND t.name LIKE '{{ENTITY_LIKE}}'
  GROUP BY s.name, t.name
  ORDER BY [rows] DESC;
  ```
- **⚠️ DMV-denied fallback (a real on-prem gap, observed 2026-06-15).** `sys.dm_db_partition_stats`
  needs **`VIEW DATABASE PERFORMANCE STATE`** (`VIEW SERVER STATE` on older versions). A least-privilege
  login may lack it — the probe returns nothing while plain `SELECT`s still work. **It is a capability
  gap to record, not a blocker:** fall back to an exact (table-scanning, so slower at scale) `COUNT_BIG`
  per table, or request the DMV grant. Generate-and-run, one row per table:
  ```sql
  -- emit a COUNT_BIG statement per entity table; copy the output and run it.
  SELECT 'SELECT ''' + s.name + '.' + t.name + ''' AS tbl, COUNT_BIG(*) AS rows FROM '
         + QUOTENAME(s.name) + '.' + QUOTENAME(t.name) + ' UNION ALL'
  FROM   sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
  WHERE  t.name LIKE '{{ENTITY_LIKE}}';
  ```
- **Read-out:** the SOURCE counts size the move; any non-zero TARGET count on a table you intend to
  load means "append" semantics — confirm the journal discipline (or a wipe) so a re-run does not
  duplicate. **If the DMV is denied on the target, record it as an environment capability gap** (it
  affects only these *fast sizing* probes — B2 keymap-sizing; the **engine's `verify-data` counts via
  `COUNT_BIG`, not DMVs, so it is unaffected**) and decide DMV-grant vs the `COUNT_BIG` scan — see
  `DATABASE_ARCHETYPES.md` (DMV-readability is a capability facet) and `NEXT_BUILD_INPUTS.sql` Part 1.

### B2 — How many rows live in FK-target tables (the resident key-map RAM ceiling)?
- **What I can't know:** the total rows in tables that are pointed *at* by a foreign key — the only
  rows whose minted keys I must hold in memory during the run.
- **What it unlocks:** the **resident remap vs spill** decision. The in-memory key map costs ≈ 40
  bytes per FK-target row; above your transfer host's memory budget, the run must switch to the
  sink-resident / server-side spill strategy. This number, against your host RAM, makes that call.
- **Probe (run on TARGET):**
  ```sql
  WITH fk_targets AS (SELECT DISTINCT referenced_object_id AS object_id FROM sys.foreign_keys)
  SELECT  SUM(p.row_count)                       AS fk_target_rows,
          SUM(p.row_count) * 40 / 1024 / 1024    AS approx_keymap_MB
  FROM    sys.dm_db_partition_stats p
  JOIN    fk_targets f ON f.object_id = p.object_id
  WHERE   p.index_id IN (0,1);
  ```
- **Read-out:** `approx_keymap_MB` ≪ host RAM → resident map (fast path). Approaching/over → trigger
  the spill design. Tell me `approx_keymap_MB` and your transfer-host memory budget.

### B3 — How many rows reference a parent that does not exist (orphans)?
- **What I can't know:** whether the source data carries FK values pointing at rows that aren't there
  (a constraint that was disabled, or cross-system drift).
- **What it unlocks:** the **drop set** and the **exit code**. The engine drops an orphan FK row,
  names it, and exits non-zero (so a "complete" never hides vanished rows). Knowing the count up front
  lets us decide: clean the source, or accept the loss explicitly. Run once per FK edge you care about.
- **Probe (run on SOURCE):**
  ```sql
  SELECT COUNT_BIG(*) AS orphan_rows
  FROM   {{SCHEMA}}.{{CHILD}} c
  WHERE  c.{{FK}} IS NOT NULL
    AND  NOT EXISTS (SELECT 1 FROM {{SCHEMA}}.{{PARENT}} p WHERE p.{{PARENT_PK}} = c.{{FK}});
  ```
- **Read-out:** `orphan_rows = 0` → no surprise drops on this edge. `> 0` → those rows will be dropped
  + reported (non-zero exit); decide clean-vs-accept before the run.

### B4 — Is the user reconcile key (email) unique, present, and consistently cased?
- **What I can't know:** the quality of the business key I match users on across the two databases.
- **What it unlocks:** the **user re-key fidelity**. Matching is by exact stored value: a **duplicate**
  email makes the match ambiguous (I keep the first and surface the rest); a **missing** email makes
  the user unmatchable (the pre-write halt fires unless you accept the drop); a **case difference**
  between source and target breaks an exact match. This probe tells me whether the re-key will be clean
  or needs a normalization/tiebreaker step first.
- **Probe (run on BOTH):**
  ```sql
  -- duplicates (ambiguous match):
  SELECT {{EMAIL}} AS email, COUNT(*) AS n
  FROM   {{SCHEMA}}.{{USER_TABLE}}
  WHERE  {{EMAIL}} IS NOT NULL
  GROUP BY {{EMAIL}} HAVING COUNT(*) > 1 ORDER BY n DESC;

  -- missing key + case-variation (raw vs lower-cased distinct counts):
  SELECT  SUM(CASE WHEN {{EMAIL}} IS NULL OR LTRIM(RTRIM({{EMAIL}})) = '' THEN 1 ELSE 0 END) AS missing_email,
          COUNT(DISTINCT {{EMAIL}})         AS distinct_raw,
          COUNT(DISTINCT LOWER({{EMAIL}}))  AS distinct_lower
  FROM    {{SCHEMA}}.{{USER_TABLE}};
  ```
- **Read-out:** duplicates present → plan a tiebreaker. `missing_email > 0` → those users halt the run
  (or need `--allow-drops`). `distinct_raw > distinct_lower` → emails differ only by case → normalize,
  or confirm the source and target store the same casing, before relying on the match.

---

## Part C — Capability, grant & runtime *(the J5 residuals + the load-time hazards)*

What the principal is actually allowed to do, what the tables will do back, and what the engine the
load runs against will tolerate at scale.

### C1 — Does any table have an object-scope DENY that the database-scope grant hides?
- **What I can't know:** whether a per-table permission overrides the broad grant (so a table refuses
  a write mid-load instead of at preflight).
- **What it unlocks:** the **pre-write grant gate's precision**. The engine checks the database-scope
  grant today; an object-scope DENY would slip through and crash a partial load. If any exist, I
  promote the preflight to table scope so the run refuses *before* writing.
- **Probe (run on TARGET, as the migration principal):**
  ```sql
  SELECT  s.name AS [schema], t.name AS [table],
          MAX(CASE WHEN p.permission_name='SELECT' THEN 1 ELSE 0 END) AS can_select,
          MAX(CASE WHEN p.permission_name='INSERT' THEN 1 ELSE 0 END) AS can_insert,
          MAX(CASE WHEN p.permission_name='UPDATE' THEN 1 ELSE 0 END) AS can_update,
          MAX(CASE WHEN p.permission_name='DELETE' THEN 1 ELSE 0 END) AS can_delete
  FROM    sys.tables  t
  JOIN    sys.schemas s ON s.schema_id = t.schema_id
  CROSS APPLY sys.fn_my_permissions(QUOTENAME(s.name)+'.'+QUOTENAME(t.name),'OBJECT') p
  WHERE   t.name LIKE '{{ENTITY_LIKE}}'
  GROUP BY s.name, t.name
  ORDER BY can_insert, can_update, t.name;
  ```
- **Read-out:** any `can_insert`/`can_update`/`can_delete` = 0 on a table you intend to write is an
  object-scope restriction → table-scope preflight required. All 1s → the database-scope check is
  sufficient.

### C2 — Is the user directory readable and email-keyed?
- **What I can't know:** the physical name/shape of the table I reconcile users against, and whether
  its email column is readable and populated.
- **What it unlocks:** the **user re-key being available at all**. The reverse-leg re-key reads the
  target's existing user inventory and matches by email; this confirms that table is selectable and the
  key column exists and is non-empty.
- **Probe (run on TARGET):**
  ```sql
  -- shape: the candidate user table + an email-like column
  SELECT  s.name AS [schema], t.name AS [table], c.name AS [column]
  FROM    sys.tables  t
  JOIN    sys.schemas s ON s.schema_id = t.schema_id
  JOIN    sys.columns c ON c.object_id = t.object_id
  WHERE   t.name = '{{USER_TABLE}}' AND c.name LIKE '%{{EMAIL}}%'
  ORDER BY c.name;
  -- read probe: SELECT works + the key is populated
  SELECT TOP 5 {{PK}}, {{EMAIL}} FROM {{SCHEMA}}.{{USER_TABLE}} WHERE {{EMAIL}} IS NOT NULL;
  ```
- **Read-out:** a returned column + non-empty rows → the re-key is available (pass `{{USER_TABLE}}:{{EMAIL}}`
  as the reconcile rule). Empty/denied → the re-key cannot run; users must be matched another way.

### C3 — Which tables carry triggers (and are any INSTEAD OF)?
- **What I can't know:** where triggers sit — they change the form of the capture statement the engine
  must use.
- **What it unlocks:** the **capture-lane descent map**. A trigger forces the set-based capture to use
  its `OUTPUT … INTO` form (the engine descends automatically on the relevant error); this is the map
  of where that descent will fire, so it is *expected* in the run report, not a surprise. An **INSTEAD
  OF** trigger is more serious — it can silently change what actually gets written.
- **Probe (run on TARGET):**
  ```sql
  SELECT  s.name AS [schema], t.name AS [table], tr.name AS [trigger],
          tr.is_disabled, tr.is_instead_of_trigger
  FROM    sys.triggers tr
  JOIN    sys.tables  t ON t.object_id = tr.parent_id
  JOIN    sys.schemas s ON s.schema_id = t.schema_id
  WHERE   t.name LIKE '{{ENTITY_LIKE}}'
  ORDER BY t.is_instead_of_trigger DESC, t.name;
  ```
- **Read-out:** `is_instead_of_trigger = 1` rows need scrutiny (they intercept the write). The rest are
  the expected `OUTPUT … INTO` descents. Empty = the fast capture lane everywhere.

### C4 — Is change-data-capture enabled on the database or any table?
- **What I can't know:** whether the target tracks changes (which governs the idempotent-redeploy /
  no-overwrite guarantee and the automatic conservative-verification fallback).
- **What it unlocks:** **arming the auto-fallback** to the compare-before-apply verification mode. The
  manual override already ships; the automatic "if change-tracking can't confirm silence, verify by
  comparison" behaviour is deferred until this verdict lands.
- **Probe (run on TARGET):**
  ```sql
  SELECT  name, is_cdc_enabled FROM sys.databases WHERE database_id = DB_ID();
  SELECT  s.name AS [schema], t.name AS [table]
  FROM    sys.tables  t JOIN sys.schemas s ON s.schema_id = t.schema_id
  WHERE   t.name LIKE '{{ENTITY_LIKE}}' AND t.is_tracked_by_cdc = 1
  ORDER BY t.name;
  ```
- **Read-out:** `is_cdc_enabled = 1` and/or tracked tables → the change-tracking path is live; I can
  arm the auto-fallback. `0` / none → it stays on the manual override (the conservative default).

### C5 — Will the target instance's memory grant admit a bulk load (the stall ceiling)?
- **What I can't know:** the target instance's query-memory semaphore vs the memory a bulk insert's
  internal sort will request — a mismatch makes a load **hang indefinitely** (not error), which looks
  like a throughput problem but is a memory-grant stall.
- **What it unlocks:** the **chunk-size / parallelism ceiling**. If the bulk grant can exceed the
  big-query semaphore, I cap the chunk size or load against the table with its non-clustered indexes
  disabled, rather than chase a phantom throughput regression.
- **Probe (run on TARGET):**
  ```sql
  -- the standing semaphores (resource_semaphore_id 0 = default/large-query pool):
  SELECT  resource_semaphore_id, max_target_memory_kb, target_memory_kb,
          total_memory_kb, available_memory_kb
  FROM    sys.dm_exec_query_resource_semaphores
  WHERE   resource_semaphore_id IN (0,1);
  -- AND, while a representative load is actually running, the live grant + any wait:
  -- SELECT requested_memory_kb, granted_memory_kb, wait_time_ms, dop
  -- FROM   sys.dm_exec_query_memory_grants ORDER BY requested_memory_kb DESC;
  ```
- **Read-out:** if a load shows `wait_time_ms` climbing with `granted_memory_kb` NULL (a request the
  semaphore won't satisfy), that is the stall — reduce chunk size / drop indexes during load. Capture
  the semaphore max so I can pre-size the chunk.

### C6 — Confirm the AssignedBySink mint-capture-delete actually works on a real table *(WRITE-PROBE)*
- **What I can't know:** whether a *real* target table — with its real triggers, constraints, and
  defaults — admits the exact statement the engine uses to insert a row, read back the minted key, and
  remove it. Mock tables proved the mechanism; this proves it on yours.
- **What it unlocks:** **end-to-end confidence in the core loop** against a real table: identity minting
  on INSERT, key capture via `OUTPUT`, trigger/constraint admission, and clean rollback — the four
  things the whole load is built on.
- **Probe (run on TARGET — transactional, rolls back; nothing persists):**
  ```sql
  BEGIN TRAN;
    DECLARE @captured TABLE (pk BIGINT);
    -- supply the table's NON-identity, non-computed columns and one valid row of values:
    INSERT INTO {{SCHEMA}}.{{TABLE}} ( /* {{NON_PK_COLS}} */ )
    OUTPUT INSERTED.{{PK}} INTO @captured
    VALUES            ( /* {{ONE_VALID_ROW}} */ );

    SELECT pk AS minted_key FROM @captured;            -- a value = INSERT + capture + triggers all pass
    DELETE FROM {{SCHEMA}}.{{TABLE}} WHERE {{PK}} IN (SELECT pk FROM @captured);
  ROLLBACK;                                             -- nothing persists; clean delete = the cleanup path holds
  ```
- **Read-out:** a `minted_key` row and a clean `DELETE` → the core loop works on a real table. An error
  on INSERT names exactly which constraint/trigger/permission blocks it (the one thing I most need to
  know before a real run). *(Optional: replace the `INSERT … OUTPUT` with the set-based `MERGE … WHEN
  NOT MATCHED … OUTPUT INSERTED.{{PK}}, <source-key> INTO @map` form to prove the bulk capture lane on
  the real table too; same transaction-rollback envelope.)*

---

## Part E — Target archetype *(which capability class is this target?)*

This is the single fact that reshapes the most: whether the target is a **full-rights** database (DDL +
DML — the on-prem schema+data home) or a **managed-DML** database (the platform sink the earlier spike
settled — DML-only, sink-mints keys, no DDL). The managed-DML profile is known; these probes **verify
the full-rights profile** and, by elimination, classify any target. Each answer flips a *bundle* of
engine dispositions, not one knob (see `DATABASE_ARCHETYPES.md` §1–§2). Run on the **TARGET**, as the
migration principal.

### E1 — `CREATE TABLE` / DDL rights *(the keystone)*
- **What I can't know:** whether the principal may create objects in the target.
- **What it unlocks:** schema deploy (the `migrate-with-data` lane), a **sink-resident progress table**
  for resume (durable + queryable + no filename↔digest coupling — strictly better than the client-side
  journal), and persistent server-side staging. This one verdict is the primary full-rights vs
  managed-DML fork.
- **Probe** (`CREATE TABLE` is a *database*-scope permission — note the `'DATABASE'` securable class):
  ```sql
  SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE')     AS can_create_table,
         HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE PROCEDURE') AS can_create_proc;
  -- Definitive (transactional — rolls back, nothing persists):
  BEGIN TRAN;
    CREATE TABLE dbo.__probe_create_rights (x INT);
    SELECT 'CREATE TABLE OK' AS verdict;
  ROLLBACK;
  ```
- **Read-out:** `can_create_table = 1` / the `CREATE` succeeds → **full-rights** (schema deploy +
  sink-resident resume available). `0` / permission error → **managed-DML** (the client-side journal +
  sink-minting path is required — the proven cloud profile).

### E2 — `ALTER` rights *(constraint / trigger bypass, fast wipe)*
- **What I can't know:** whether the principal may alter objects (disable constraints/triggers,
  `TRUNCATE`).
- **What it unlocks:** a **fast straight-load** path (`NOCHECK` constraints + disable triggers during the
  load, instead of the capture-ladder descent) and `TRUNCATE`-based refresh (instead of child-first
  `DELETE`).
- **Probe (swap `{{SCHEMA}}` / `{{TABLE}}` for a representative table):**
  ```sql
  SELECT HAS_PERMS_BY_NAME(QUOTENAME('{{SCHEMA}}')+'.'+QUOTENAME('{{TABLE}}'),'OBJECT','ALTER')
           AS can_alter_table;
  ```
- **Read-out:** `1` → constraint/trigger bypass + `TRUNCATE` available (the full-rights fast lane).
  `0` → load through live constraints (the capture-ladder descent + child-first `DELETE` — the
  managed-DML path).

### E3 — `IDENTITY_INSERT` *(the identity-disposition fork)*
- **What I can't know:** whether the principal may write explicit values into an auto-number column.
- **What it unlocks:** **PreservedFromSource** — the source surrogate keys can be written *directly*, so
  **no key capture, no remap, and no FK re-pointing are needed at all**. This is a dramatically simpler
  and faster load than the sink-minting path; it is correct *only* if this is permitted.
- **Probe (transactional — rolls back; swap `{{TABLE}}`/`{{PK}}` + one valid row):**
  ```sql
  BEGIN TRAN;
    SET IDENTITY_INSERT {{SCHEMA}}.{{TABLE}} ON;
    INSERT INTO {{SCHEMA}}.{{TABLE}} ({{PK}} /*, other required cols */)
    VALUES            ( /* an explicit unused id */ /*, ... */);
    SET IDENTITY_INSERT {{SCHEMA}}.{{TABLE}} OFF;
    SELECT 'IDENTITY_INSERT OK' AS verdict;
  ROLLBACK;
  ```
- **Read-out:** OK → **PreservedFromSource viable** (full-rights) — keys are preserved, FK values stay
  valid, the whole capture/remap machinery is unnecessary. Denied (error 1088 / permission) →
  **AssignedBySink** (the managed-DML path the engine ships). *A declared full-rights target that fails
  this is a declared-vs-actual mismatch — surface it.*

### E4 — Schema parity *(does the target host the same schema?)*
- **What I can't know:** whether the on-prem target carries the same logical model as the source/cloud.
- **What it unlocks:** confidence that the on-prem is the **same-schema home** (the verification
  destination), and the structural input the post-load canary compares.
- **Probe:**
  ```sql
  SELECT s.name AS [schema], t.name AS [table], c.name AS [column],
         ty.name AS data_type, c.is_nullable, c.is_identity
  FROM   sys.columns c
  JOIN   sys.types   ty ON ty.user_type_id = c.user_type_id
  JOIN   sys.tables  t  ON t.object_id = c.object_id
  JOIN   sys.schemas s  ON s.schema_id = t.schema_id
  WHERE  t.name LIKE '{{ENTITY_LIKE}}'
  ORDER BY t.name, c.column_id;
  ```
- **Read-out:** diff this inventory against the emitted SSDT / the source rendition. Match (modulo the
  rendition's name differences — compare by logical identity, not raw name) → the on-prem is the
  same-schema home. The engine's canary does this exactly; this is the operator spot-check.

### E5 — Sink-resident progress-table viability *(the resume upgrade)*
- **What I can't know:** whether the target admits a small engine-owned bookkeeping table.
- **What it unlocks:** a **sink-side resume checkpoint** — durable across a client crash, queryable
  mid-run, and free of the client-journal's filename↔digest coupling (the Phase-3 address-drift and
  compaction hazards simply do not exist on this archetype).
- **Probe (transactional — rolls back; subsumes E1 + an INSERT):**
  ```sql
  BEGIN TRAN;
    CREATE TABLE dbo.__progress_probe (kind SYSNAME, chunk_ix INT, committed_at DATETIME2);
    INSERT INTO dbo.__progress_probe (kind, chunk_ix, committed_at) VALUES (N'probe', 0, SYSUTCDATETIME());
    SELECT 'sink-resident progress OK' AS verdict;
  ROLLBACK;
  ```
- **Read-out:** OK → the engine can checkpoint resume state **sink-side** (the full-rights upgrade).
  Denied → the client-side journal is required (managed-DML) — which the engine already ships and
  Phase 3 hardened.

> **Classification rule.** E1 + E3 both **OK** ⇒ **full-rights** (and E2/E5 confirm the fast/durable
> upgrades). E1 + E3 both **denied** ⇒ **managed-DML** (the J5 profile — declare it and the engine uses
> the proven cloud path). A *split* result (e.g. `CREATE TABLE` yes but `IDENTITY_INSERT` no) is a
> **distinct, valid archetype** — record it; the engine should treat each capability independently, not
> assume the bundle.

---

## Part D — Throughput *(the one measurement, not a query)*

### D1 — Does the set-based streaming lane sustain the throughput floor over the real wire?
- **What I can't know:** the real rows/second over your actual network + instance, worst-case
  all-FK-referenced shape. Every figure I have is loopback-local.
- **What it unlocks:** the **go/no-go on the streaming lane as shipped**, and which escape hatch (if
  any) to trigger — larger/swept chunk size, parallel per-table loading, or the server-side spill.
- **Procedure (operator-run; the engine is already instrumented — no new code):**
  1. Pick the largest FK-target table from B1 (the worst case for the key map), or a capped slice of it.
  2. Run the streaming reverse-leg load against SOURCE → TARGET, **journaled** (a journal is required for
     a streaming execute — it makes the run resumable + idempotent) and verbose:
     `… move <flow> --go --streaming --journal <dir> -v`
  3. Read rows/second from the bench output (rows ÷ load-seconds; cross-check against the 50,000-row
     chunk cadence).
- **Read-out:** at/above your cutover-window floor → ship the streaming lane. Materially below ~20k
  rows/sec → tell me, and I trigger an escape hatch with a plan (chunk-size sweep, parallel wavefronts,
  or the sink-resident spill — sized by B2). Watch for the C5 stall (a hang with zero rows is memory, not
  throughput).

---

## §5 — Results ledger *(record verdicts here; only this needs to travel back)*

| # | Probe | Verdict (generic — counts / yes-no / which-tables, never names or rows) | Engine consequence |
|---|---|---|---|
| A1 | identity disposition | | |
| A2 | composite PKs | | |
| A3 | FK graph + nullability | | |
| A4 | non-PK identity cols | | |
| A5 | non-insertable cols | | |
| B1 | row counts (src/tgt) | | |
| B2 | FK-target rows → keymap MB | | |
| B3 | orphan FK rows | | |
| B4 | reconcile-key quality | | |
| C1 | object-scope DENY | | |
| C2 | user directory / email | | |
| C3 | triggers (+ INSTEAD OF) | | |
| C4 | change-tracking enabled | | |
| C5 | memory grant / semaphore | | |
| C6 | real-table write-probe | | |
| E1 | CREATE TABLE / DDL | | |
| E2 | ALTER rights | | |
| E3 | IDENTITY_INSERT | | |
| E4 | schema parity | | |
| E5 | sink-resident progress | | |
| — | **→ archetype verdict** | **full-rights / managed-DML / split** | drives the disposition bundle (`DATABASE_ARCHETYPES.md`) |
| D1 | throughput rows/sec | | |

**Then:** with A–C answered I finalize the per-table strategy, the refusal list, the memory/spill
plan, and the reconcile plan; with D1 answered the streaming lane is go/no-go. Each answered row turns
a "staged / gated" item in the charter into a settled one — that is the unlock.
