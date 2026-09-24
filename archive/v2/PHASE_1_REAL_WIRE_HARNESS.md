# Phase 1 — Real-Wire Harness & Estate Survey (operator-actionable)

> **What this is.** The operator-runnable package for the one thing the engine cannot fake:
> the real wire. It closes the charter's **Phase 1** (`CHARTER_REVERSE_LEG_EXECUTION.md` Part IX)
> and is the prerequisite for the **Phase 5** cutover gates. Two parts: **(A)** the P7b throughput
> bench (does the set-based streaming lane sustain the throughput floor against a real managed
> OutSystems environment?), and **(B)** the estate survey SQL (the read-only probes that size the
> remap, map the trigger lanes, and settle the residual J5 questions). Everything here is
> **read-only or operator-gated** — the agent cannot run it; hand it to the operator.
>
> **Dated 2026-06-15.** Authored alongside Phases 2–4 (reconcile ∘ streaming, force-journal,
> movement dry-run). The set-based streaming reconcile lane these measurements exercise is
> **BUILT and witnessed at Docker scale**; this package is how it earns "trusted on the real wire."

---

## Part A — The P7b throughput bench

**The question (the only load-bearing residual after the J5 run).** The shipped set-based
streaming lane measured **~27k–35.5k rows/sec on loopback Docker** (`AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md` §0).
P7b asks: does it sustain the **throughput floor** (the figure `V2_DRIVER.md` / the audit fix
for the cutover window) over a **real managed-environment wire**, worst-case all-FK-referenced shape?
A real-wire bench materially **below 20k rows/sec re-opens** chunk-sizing, parallel per-table loading
(the seed-lane level-parallel loader port), and the sink-resident spill — all named, none yet triggered.

**The harness is the shipped engine, instrumented.** The streaming reverse leg already carries
`Bench` probes on the hot loop (`Bench.scope` / `streamProbe`); a real run prints the rows/sec
without new code. The operator procedure:

1. **Pick a representative slice.** A single large FK-target table (the worst case for the resident
   remap) at representative scale — ideally the largest OSUSR table the survey (Part B §1) finds, or a
   capped slice of it. The streaming lane is whole-estate by design, but a single-table bench isolates
   the per-row vs set-based cost P7b compares.
2. **Run the streaming reverse leg, journaled, gated.** Against the real source (B, logical) → real
   managed sink (A, physical):
   ```
   PROJECTION_ALLOW_EXECUTE=1 projection move <reverse-leg-flow> --go --streaming --journal <dir>
   ```
   `--streaming` selects the bounded-memory lane; `--journal <dir>` is now **required** for a streaming
   execute (Phase 3 — the duplicate-hazard close), so the run is resumable + idempotent.
3. **Read rows/sec from the bench output** (`-v` surfaces the bench table). The per-kind `load` stage
   reports rows + elapsed; rows/sec = rows ÷ load-seconds. Cross-check against the per-chunk
   `CaptureChunkSize = 50_000` cadence.
4. **Compare to the floor.**
   - **≥ floor:** Phase 1 throughput residual closed — record the figure in `DECISIONS.md` (amending the
     `~271 / ~27k / ~35.5k` ladder) and proceed to the cutover gates (Phase 5).
   - **materially < 20k:** trigger the escape hatches with a plan: (a) the **50k-chunk sweep** (the seed
     lane vindicated 5k/10k batch at ~31k rows/sec; sweep `CaptureChunkSize`), (b) the **parallel
     per-table wavefronts** (port `Deploy.executeLeveledSeed` / `ParallelSafe` to the reverse leg —
     reuse, not build; gated on the cross-kind remap dependency), (c) the **sink-resident keymap /
     server-side `UPDATE…JOIN` spill** (DESIGNED-only, gated on the FK-target row count from §2).
5. **Watch for the warm-container memory-grant stall** (`RESOURCE_SEMAPHORE`): a bulk load that hangs
   with zero rows is a memory-grant stall, NOT a throughput finding — diagnose via
   `sys.dm_exec_query_memory_grants` + `…_resource_semaphores`, it is batch-size-independent.

**Acceptance:** the throughput floor sustained over the real wire, or the escape hatches triggered
**with a plan** (charter Phase 1 exit).

---

## Part B — The estate survey (read-only SQL)

Run as the migration principal against the real managed sink (A). Every query is **read-only**
(catalog views + `fn_my_permissions` + partition stats — no table scans, no writes). Replace the
`OSUSR[_]%` pattern if the estate uses a different physical convention. Capture the **sanitized**
results only (counts, shapes, verdicts — never table names / row data / logins) per the J5 covenant.

### 1. Row counts — sizes the run and the spill decision

```sql
-- Exact per-table row counts WITHOUT a scan (partition stats).
SELECT  s.name AS [schema], t.name AS [table], SUM(p.row_count) AS [rows]
FROM    sys.dm_db_partition_stats p
JOIN    sys.tables  t ON t.object_id = p.object_id
JOIN    sys.schemas s ON s.schema_id = t.schema_id
WHERE   p.index_id IN (0,1)           -- heap or clustered: the base rows
  AND   t.name LIKE 'OSUSR[_]%'
GROUP BY s.name, t.name
ORDER BY [rows] DESC;
```
*Feeds:* the scale-shape decision (whole estate vs largest table), the P7b slice choice, and the
operator decision "how many rows total."

### 2. FK fan-in — sizes the resident packed remap (the ~40 B/entry ceiling)

```sql
-- (a) The FK edge map among OSUSR tables — referencing (table,col) -> referenced table.
SELECT  rs.name AS ref_schema, rt.name AS referencing_table, rc.name AS referencing_col,
        ts.name AS tgt_schema, tt.name AS referenced_table
FROM    sys.foreign_keys fk
JOIN    sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN    sys.tables   rt ON rt.object_id = fk.parent_object_id
JOIN    sys.schemas  rs ON rs.schema_id = rt.schema_id
JOIN    sys.columns  rc ON rc.object_id = fkc.parent_object_id AND rc.column_id = fkc.parent_column_id
JOIN    sys.tables   tt ON tt.object_id = fk.referenced_object_id
JOIN    sys.schemas  ts ON ts.schema_id = tt.schema_id
WHERE   rt.name LIKE 'OSUSR[_]%' OR tt.name LIKE 'OSUSR[_]%'
ORDER BY referenced_table, referencing_table;

-- (b) Total rows living in FK-TARGET tables — the resident-remap RAM driver.
--     Resident ceiling ~= 40 B * (rows in FK-target tables). Above the transfer-host
--     memory budget, the sink-resident spill (DESIGNED-only) is the named next step.
WITH fk_targets AS (SELECT DISTINCT referenced_object_id AS object_id FROM sys.foreign_keys)
SELECT  SUM(p.row_count) AS fk_target_rows,
        SUM(p.row_count) * 40 / 1024 / 1024 AS approx_remap_MB
FROM    sys.dm_db_partition_stats p
JOIN    fk_targets f ON f.object_id = p.object_id
WHERE   p.index_id IN (0,1);
```
*Feeds:* resident remap vs sink-resident spill (the operator decision "how many rows in FK-target
tables" + "transfer-host memory budget").

### 3. P5 trigger map — the capture-ladder descent map

```sql
-- OSUSR tables carrying triggers. A trigger forces OUTPUT…INTO (the capture ladder
-- descends StagedMergeOutput -> StagedMergeOutputInto on SQL error 334 automatically);
-- this is the map of WHERE that descent will fire (so it is expected, not a surprise).
SELECT  s.name AS [schema], t.name AS [table], tr.name AS [trigger],
        tr.is_disabled, tr.is_instead_of_trigger
FROM    sys.triggers tr
JOIN    sys.tables  t ON t.object_id = tr.parent_id
JOIN    sys.schemas s ON s.schema_id = t.schema_id
WHERE   t.name LIKE 'OSUSR[_]%'
ORDER BY t.name;
```
*Feeds:* confidence that the trigger-bearing tables are handled by the existing descent ladder; no
code change expected — the map is for the run report, not a gate.

### 4. G1 — object-scope DENY (the P1b residual)

```sql
-- Per-table effective permissions for the CURRENT principal (object scope).
-- The J5 run settled the DATABASE-scope grant (SELECT/INSERT/UPDATE/DELETE, no ALTER).
-- G1 is the residual: does any per-table DENY ESCAPE the DB grant? A table missing
-- INSERT/UPDATE/DELETE/SELECT below has an object-scope restriction the DB grant hid.
SELECT  s.name AS [schema], t.name AS [table], p.permission_name, p.state_desc
FROM    sys.tables  t
JOIN    sys.schemas s ON s.schema_id = t.schema_id
CROSS APPLY sys.fn_my_permissions(QUOTENAME(s.name)+'.'+QUOTENAME(t.name),'OBJECT') p
WHERE   t.name LIKE 'OSUSR[_]%'
  AND   p.permission_name IN ('SELECT','INSERT','UPDATE','DELETE')
ORDER BY t.name, p.permission_name;
```
*Feeds:* the reserved `ReverseLegBoundaryTests` Skip-stub ("object-scope DENY refused by name before
any write"). If any planned-write table is missing a permission here, the preflight refinement (P1b —
descend `Preflight.captureGrantEvidence` to table scope) is promoted; the agent's
`transfer.insufficientGrant` gate is currently DATABASE-scope only.

### 5. P10 — user directory (ReconciledByRule readability + email key)

```sql
-- The user directory the reverse-leg user re-key reconciles against (Phase 2).
-- Confirms ReconciledByRule is available: a readable user table with an email column.
SELECT  s.name AS [schema], t.name AS [table], c.name AS [column]
FROM    sys.tables  t
JOIN    sys.schemas s ON s.schema_id = t.schema_id
JOIN    sys.columns c ON c.object_id = t.object_id
WHERE  (t.name = 'OSSYS_USER' OR t.name LIKE 'OSUSR[_]%USER%')
  AND   c.name LIKE '%EMAIL%'
ORDER BY t.name, c.name;
-- Then a read probe against the resolved table (confirms SELECT + a populated email):
--   SELECT TOP 5 [Id],[Email] FROM <user table> WHERE [Email] IS NOT NULL;
```
*Feeds:* OPEN-7. Phase 2 built the reverse-leg user re-key (reconcile-by-email on the streaming
arm); this confirms the real estate's user table is readable + email-keyed, so the operator can
pass `--reconcile <userTable>:EMAIL` for real. (The Phase-2 reconcile reads the sink user inventory
directly; no design-time discovery pass is needed.)

### 6. CDC tracking (OPEN-3) — gates the NM-73 auto-fallback (Phase 5)

```sql
-- Is CDC enabled on the database / any OSUSR table?
SELECT  name, is_cdc_enabled FROM sys.databases WHERE database_id = DB_ID();
SELECT  s.name AS [schema], t.name AS [table]
FROM    sys.tables  t
JOIN    sys.schemas s ON s.schema_id = t.schema_id
WHERE   t.name LIKE 'OSUSR[_]%' AND t.is_tracked_by_cdc = 1
ORDER BY t.name;
```
*Feeds:* OPEN-3 / Phase 5. The J5 run proved transaction/lock semantics but NOT the CDC path on a
real managed environment. This is the verdict that **arms the NM-73 EXCEPT auto-fallback** (today the
manual `emission.dataVerification = ValidateBeforeApply` override is shipped; the automatic
CDC-failure → EXCEPT fallback is deferred "until after J5" — i.e., until this verdict lands).

---

## Part C — Hand-off checklist

For the operator, then back to the agent:

- [ ] **§1 row counts** — the scale shape + the P7b slice choice.
- [ ] **§2 FK fan-in** — `fk_target_rows` + `approx_remap_MB` vs the transfer-host memory budget
      (decides resident remap vs the sink-resident spill).
- [ ] **§3 trigger map** — expected capture-ladder descents (report-only).
- [ ] **§4 G1** — any object-scope DENY (promotes the P1b preflight refinement if found).
- [ ] **§5 P10** — the user table + email column (enables `--reconcile <userTable>:EMAIL` for real).
- [ ] **§6 CDC** — the verdict that arms the NM-73 auto-fallback.
- [ ] **Part A P7b** — rows/sec vs the floor (closes Phase 1, or triggers the escape hatches).

When these land, the **Phase 5 cutover gates** become actionable: the production-shaped dry-run is
the Phase-4 preview run against the real estate; the advisory→hard grant gate is the per-pair
cutover wiring of `projection survey` (which already hard-stops on a blocked capability — exit 7);
and the NM-73 auto-fallback arms on the §6 CDC verdict.
