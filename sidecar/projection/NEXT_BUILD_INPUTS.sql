/* =============================================================================
   NEXT-BUILD INPUTS — the two facts the next slices need from the real estate.
   Companion to REVERSE_LEG_OPERATOR_PROBE_SHEET.md (reuse its §0 binding sheet).
   Generic; read-only except the clearly-marked transactional write-probes that
   ROLL BACK (nothing persists). Paste back ONLY the labeled outputs.

   Substitute:
     {{ENTITY_LIKE}}  a LIKE pattern matching your entity tables (e.g. 'APP\_%' ESCAPE '\', or '%')
     {{SCHEMA}}.{{TABLE}}  one representative IDENTITY-PK entity table (for the ALTER/IDENTITY probes)

   WHAT EACH PART UNLOCKS
     Part 1  -> the sink-resident keymap SPILL sizing (resident vs spill decision at 200M)
     Part 2  -> the ARCHETYPE verdict for the on-prem target (Slice A + the C/D forks)
     (verify-data needs NOTHING new: the engine counts via COUNT_BIG, not DMVs, so it
      already works on the DMV-denied target — no input required, no build required.)
   ============================================================================= */


/* -----------------------------------------------------------------------------
   PART 1 — KEYMAP SPILL SIZING
   Run on the SOURCE (the database rows are READ from). The resident key-map holds
   ~40 bytes PER ROW of each AssignedBySink (single-IDENTITY-PK) table that is also
   a foreign-key TARGET — those are the only minted keys with a downstream consumer.
   Sum(rows) * 40 = the resident RAM ceiling; compare to the transfer-host budget.
   ----------------------------------------------------------------------------- */

-- 1a — the set of AssignedBySink FK-target tables (catalog only; no DMV needed).
WITH fk_targets AS (SELECT DISTINCT referenced_object_id AS oid FROM sys.foreign_keys),
     identity_pk AS (
        SELECT t.object_id AS oid
        FROM   sys.tables t
        JOIN   sys.indexes i  ON i.object_id = t.object_id AND i.is_primary_key = 1
        JOIN   sys.index_columns ic ON ic.object_id = t.object_id AND ic.index_id = i.index_id
        JOIN   sys.columns c  ON c.object_id = t.object_id AND c.column_id = ic.column_id
        GROUP BY t.object_id
        HAVING COUNT(ic.column_id) = 1 AND MAX(CAST(c.is_identity AS INT)) = 1)
SELECT s.name AS [schema], t.name AS [table]
FROM   sys.tables t
JOIN   sys.schemas s ON s.schema_id = t.schema_id
JOIN   fk_targets  f ON f.oid = t.object_id
JOIN   identity_pk p ON p.oid = t.object_id
WHERE  t.name LIKE '{{ENTITY_LIKE}}'
ORDER BY t.name;
--> PASTE BACK: the table count (how many such tables). Names not needed.

-- 1b — DMV sizing (use on the MANAGED source, where the DMV is granted).
WITH fk_targets AS (SELECT DISTINCT referenced_object_id AS oid FROM sys.foreign_keys),
     identity_pk AS (
        SELECT t.object_id AS oid
        FROM   sys.tables t
        JOIN   sys.indexes i  ON i.object_id = t.object_id AND i.is_primary_key = 1
        JOIN   sys.index_columns ic ON ic.object_id = t.object_id AND ic.index_id = i.index_id
        JOIN   sys.columns c  ON c.object_id = t.object_id AND c.column_id = ic.column_id
        GROUP BY t.object_id
        HAVING COUNT(ic.column_id) = 1 AND MAX(CAST(c.is_identity AS INT)) = 1)
SELECT SUM(ps.row_count)                    AS fk_target_assignedbysink_rows,
       SUM(ps.row_count) * 40 / 1024 / 1024 AS approx_keymap_MB
FROM   sys.dm_db_partition_stats ps
JOIN   fk_targets  f ON f.oid = ps.object_id
JOIN   identity_pk p ON p.oid = ps.object_id
WHERE  ps.index_id IN (0,1);
--> PASTE BACK: fk_target_assignedbysink_rows AND approx_keymap_MB.

-- 1c — COUNT_BIG fallback (only if the DMV (1b) is denied on the database you must size).
--      Generates one COUNT statement per FK-target table; run the output, sum the counts.
WITH fk_targets AS (SELECT DISTINCT referenced_object_id AS oid FROM sys.foreign_keys),
     identity_pk AS (
        SELECT t.object_id AS oid
        FROM   sys.tables t
        JOIN   sys.indexes i  ON i.object_id = t.object_id AND i.is_primary_key = 1
        JOIN   sys.index_columns ic ON ic.object_id = t.object_id AND ic.index_id = i.index_id
        JOIN   sys.columns c  ON c.object_id = t.object_id AND c.column_id = ic.column_id
        GROUP BY t.object_id
        HAVING COUNT(ic.column_id) = 1 AND MAX(CAST(c.is_identity AS INT)) = 1)
SELECT 'SELECT COUNT_BIG(*) AS c FROM ' + QUOTENAME(s.name) + '.' + QUOTENAME(t.name) + ' UNION ALL'
FROM   sys.tables t
JOIN   sys.schemas s ON s.schema_id = t.schema_id
JOIN   fk_targets  f ON f.oid = t.object_id
JOIN   identity_pk p ON p.oid = t.object_id
WHERE  t.name LIKE '{{ENTITY_LIKE}}';
--> PASTE BACK: the summed total (= fk_target_assignedbysink_rows); * 40 / 1024 / 1024 = approx_keymap_MB.

/* ALSO TELL ME: the transfer-host RAM budget (GB). resident-map fits if approx_keymap_MB
   << that budget; otherwise the run needs the sink-resident spill (which Part 2 must permit). */


/* -----------------------------------------------------------------------------
   PART 2 — ON-PREM ARCHETYPE VERDICT  (run on the ON-PREM TARGET as the principal)
   Settles the FullRights vs managed-DML fork that drives Slice A + the C/D forks.
   The write-probes are transactional and ROLL BACK — nothing persists.
   ----------------------------------------------------------------------------- */
SET NOCOUNT ON;

-- E1 (CREATE TABLE, database scope) + E2 (ALTER on a sample table) — read-only.
SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE') AS can_create_table,
       HAS_PERMS_BY_NAME(QUOTENAME('{{SCHEMA}}') + '.' + QUOTENAME('{{TABLE}}'), 'OBJECT', 'ALTER') AS can_alter_sample_table;
--> PASTE BACK: can_create_table, can_alter_sample_table (1 = permitted, 0 = denied).

-- E3 — IDENTITY_INSERT (the PreservedFromSource fork). SET ON requires ALTER/ownership;
--      a denial here is the managed-DML signature (error 1088). Transactional, rolls back.
BEGIN TRY
    BEGIN TRAN;
        SET IDENTITY_INSERT {{SCHEMA}}.{{TABLE}} ON;
        SET IDENTITY_INSERT {{SCHEMA}}.{{TABLE}} OFF;
    ROLLBACK;
    SELECT 'permitted' AS [identity_insert];
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    SELECT CONCAT('denied (', ERROR_NUMBER(), ')') AS [identity_insert];
END CATCH;
--> PASTE BACK: identity_insert (permitted / denied(<n>)).

-- E5 — sink-resident progress table (the resume upgrade; subsumes CREATE TABLE).
--      Transactional, rolls back.
BEGIN TRY
    BEGIN TRAN;
        CREATE TABLE dbo.__progress_probe (kind SYSNAME, chunk_ix INT, committed_at DATETIME2);
    ROLLBACK;
    SELECT 'permitted' AS sink_resident_progress;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    SELECT CONCAT('denied (', ERROR_NUMBER(), ')') AS sink_resident_progress;
END CATCH;
--> PASTE BACK: sink_resident_progress (permitted / denied(<n>)).

/* VERDICT (you or I derive it):
     can_create_table=1 AND identity_insert=permitted  -> FullRights
       (the engine can preserve keys + checkpoint sink-side; Slice C+D unlock)
     can_create_table=0 AND identity_insert=denied      -> managed-DML (the J5 profile; already shipped)
     a split (e.g. CREATE TABLE yes, IDENTITY_INSERT no) -> a distinct valid archetype; record each flag
   Plus the DMV gap already found (no VIEW DATABASE PERFORMANCE STATE) — a FullRights-minus-DMV target. */
