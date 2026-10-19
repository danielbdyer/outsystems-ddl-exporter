# ATOMIC_ENVELOPE_VALIDATION.md — the resolution protocol for the §10 giant-`BEGIN TRAN` trigger

> **Status: questions only — no build.** This is the validation protocol whose answers would *fire or refute* the
> §10-deferred Atomic `BEGIN TRAN` envelope for `migrate A B`. Authored 2026-06-16 alongside **M21** (the live
> compensating-undo rollback arm; `DECISIONS 2026-06-16 (later)`). Building the envelope without these answers
> violates "IR grows under evidence." The A3 scaffold (`Preflight.fs:345`) and `THE_VECTOR_EXECUTION_KICKOFF.md`
> §10 ("the live atomic `BEGIN TRAN` wrapper / the managed-login grant survey resolves") point here.

## 0 — The boundary (what M21 already covers, so this is scoped honestly)

`migrate A B` today **refuses without damage**: on a mid-deploy failure M21 rides M12's `CatalogDiff.inverse` to
return the substrate to A (`ExecutionRolledBack`) or names the residual (`PartialWriteUnrecovered`). What M21 does
**not** give is true *all-or-nothing atomicity at estate scale within one transaction* — the case where you want SQL
Server itself to revert every statement on `ROLLBACK`, including applied ALTERs that M21 deliberately won't auto-invert.
The Atomic envelope is that stronger guarantee. **The first question is therefore whether it is even needed** (D1),
because M21 may already satisfy the operator's risk tolerance.

## 1 — Decision questions (operator judgment — no SQL)

| # | Question | Why it gates the build |
|---|---|---|
| **D1** | Given M21 (rollback-or-named-residual) now exists, is *true single-transaction all-or-nothing* actually required, or is compensating-undo + resumable-by-re-diff sufficient? | If D1 = "M21 is enough," the envelope drops in priority — name it permanently-deferred, don't build. |
| **D2** | What is the acceptable **blocking / maintenance-window duration** for a cutover `migrate`? (seconds? minutes? a planned outage?) | A giant TRAN holds schema-modification locks for its whole duration. D2 is the budget the P7b throughput measurement (§2.4) must fit inside. |
| **D3** | Is the target **one DB pair**, or the **full estate in one transaction**? | A per-pair TRAN is mild; the giant-transaction-*over-the-estate* is the scary one §10 actually defers. The envelope's blast radius is set here. |
| **D4** | Is the target the same **J5-class managed OutSystems env** (DML-only, AssignedBySink, no ALTER), or a **self-managed** SQL where we control the recovery model and own a maintenance window? | On a J5-class env the envelope is likely *moot* (no ALTER grant → no DDL to wrap; the J5 evidence already pointed to the compensating channel). On self-managed it is buildable. D4 decides whether §2's probes even apply. |

## 2 — Validation probes (the SQL I'd have you run on the **target** DB)

Run each on the actual target environment (the one a real `migrate` would touch), as the **same login** the engine
will use, and paste the result back. Each probe says what its answer *implies for the envelope*. All are read-only
except probe 2.2, which creates and **rolls back** a throwaway table.

### 2.1 — Recovery model + transaction-log headroom (a giant TRAN pins the log until COMMIT)
```sql
SELECT name, recovery_model_desc, log_reuse_wait_desc
FROM sys.databases WHERE name = DB_NAME();

SELECT total_log_size_in_bytes/1024/1024 AS log_mb,
       used_log_space_in_percent
FROM sys.dm_db_log_space_usage;

SELECT name, size*8/1024 AS size_mb, max_size, growth, is_percent_growth
FROM sys.database_files WHERE type_desc = 'LOG';
```
**Implies:** `FULL` recovery + a large transaction → the log cannot truncate until COMMIT → growth/exhaustion risk
during the window. If `max_size` is capped or growth is tight, the envelope needs log headroom or must stay chunked
(which defeats single-transaction atomicity). `log_reuse_wait_desc = ACTIVE_TRANSACTION` during a test run confirms the
pin.

### 2.2 — Can THIS login hold an explicit transaction containing DDL, and does ROLLBACK revert it? (the grant survey)
```sql
-- the DDL grant the envelope needs. NOTE: sys.fn_my_permissions has NO state/grant
-- column — it returns ONLY the permissions the current login effectively HAS, so a
-- row appearing IS the grant. (An earlier draft selected a non-existent `state_desc`.)
SELECT permission_name
FROM sys.fn_my_permissions(NULL, 'DATABASE')
WHERE permission_name IN ('ALTER','CONTROL','ALTER ANY SCHEMA');

-- the smoke test (creates then ROLLS BACK a throwaway object)
BEGIN TRAN;
CREATE TABLE dbo.__v2_tran_probe (id int);
ALTER TABLE dbo.__v2_tran_probe ADD note nvarchar(10) NULL;
SELECT @@TRANCOUNT AS tran_depth, XACT_STATE() AS xact_state;
ROLLBACK;
SELECT OBJECT_ID('dbo.__v2_tran_probe') AS still_exists;  -- NULL ⇒ DDL is transactional & rollback worked
```
**Implies:** the single most important answer. If `ALTER`/`CONTROL` is absent → there is no DDL to wrap → the envelope
is moot for this env (the J5 outcome). If `still_exists` is non-NULL or the `BEGIN TRAN` is rejected → the managed env
forbids transactional DDL → envelope **refuted**, M21 is the ceiling.

### 2.3 — Will the env *kill* a long-running transaction? (idle/governor timeout)
```sql
SELECT @@LOCK_TIMEOUT AS lock_timeout_ms;

SELECT transaction_isolation_level, login_name
FROM sys.dm_exec_sessions WHERE session_id = @@SPID;

-- Resource Governor (may be DENIED on a managed env — a denial is itself a signal)
SELECT pool_id, name, request_max_cpu_time_sec, max_memory_percent
FROM sys.dm_resource_governor_resource_pools;
```
**Implies:** a positive `request_max_cpu_time_sec` or a hostile governor policy means a multi-minute transaction may
be aborted mid-flight — which would *trigger M21's compensation anyway*, so the envelope buys nothing. A denial reading
the governor views tells us it is a locked-down managed env (lean toward M21).

### 2.4 — P7b throughput: how long would the deploy actually hold the lock? (the measurement that gates D2)
```sql
-- the heaviest tables (how much the transfer leg must move under the lock)
SELECT TOP 20 s.name AS [schema], t.name AS [table], p.rows
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
ORDER BY p.rows DESC;

-- a timed, representative ALTER on a CLONE/throwaway of a large table to get rows/sec:
--   SET STATISTICS TIME ON; ALTER TABLE <clone_of_big> ALTER COLUMN <col> <wider_type>; SET STATISTICS TIME OFF;
```
**Implies:** total lock-hold ≈ Σ(op durations) + the transfer's rows/sec × row count. Compare against **D2**. This is
the concrete "P7b throughput" number the matrix footer says the giant-transaction is gated on. Also run
`projection migrate <A> <B> --dry-run` and share the channel counts / norm — that is exactly how many statements the
envelope would wrap.

### 2.5 — CDC interaction (a giant TRAN delays capture → SSIS-consumer lag)
```sql
SELECT name, is_cdc_enabled FROM sys.databases WHERE name = DB_NAME();

SELECT s.name AS [schema], t.name AS [table]
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.is_tracked_by_cdc = 1;

EXEC sys.sp_cdc_help_jobs;   -- capture latency / maxtrans / pollinginterval
```
**Implies:** an uncommitted giant transaction holds the LSN, so CDC capture (and the SSIS consumer the UAT feeds) lags
the whole window — a direct tension with the engine's CDC-silence guarantee. If CDC is on, the envelope and the
capture contract must be reconciled (or the envelope scoped to non-CDC tables).

### 2.6 — Blocking blast radius (RCSI)
```sql
SELECT name, is_read_committed_snapshot_on, snapshot_isolation_state_desc
FROM sys.databases WHERE name = DB_NAME();
```
**Implies:** without `READ_COMMITTED_SNAPSHOT`, readers block on the envelope's `Sch-M`/exclusive locks for the whole
window; with it, reads are versioned. Sets how disruptive the window is to live traffic.

## 3 — The decision the answers produce

- **2.2 negative (no ALTER grant / no transactional DDL)** → envelope **refuted** for this env; record it permanently
  deferred, M21 is the ceiling, done.
- **2.2 positive, but 2.4 lock-hold > D2 budget, or 2.5 CDC conflict** → envelope **deferred-with-evidence**; pursue
  per-pair (D3) or chunked-resumable instead. M21 + resumable remains the arm.
- **2.2 positive, 2.4 within D2, 2.5/2.6 clear** → the trigger has **fired**; build the envelope (wrap the `deploy`
  stage in `BEGIN TRAN`/`COMMIT`/`ROLLBACK`, with `SET XACT_ABORT ON`), and M21 becomes its fallback for the
  managed-login-denied path. A DECISIONS amendment records the fired trigger first.

> Boundary reminder: even if built, the envelope does not subsume M21 — it removes the *partial-state* window, M21
> removes the *silent-corruption* window. They compose.

## 4 — Results log

### On-prem SQL Server sink — probe 2.2 (2026-06-16): **PASS**
- `sys.fn_my_permissions(NULL,'DATABASE')` → `ALTER ANY SCHEMA` present (DDL grant held; the in-transaction smoke test
  ran `CREATE TABLE` + `ALTER TABLE` successfully, proving it operationally sufficient for table/column DDL).
- Smoke test: `@@TRANCOUNT = 1`, `XACT_STATE() = 1` (active **and committable** after the DDL — the DDL did not doom
  the transaction), `OBJECT_ID(...) = NULL` after `ROLLBACK` ⇒ **DDL is transactional and ROLLBACK fully reverts it.**
- **Verdict:** the *capability* conjunct of the §10 trigger is **SATISFIED on the on-prem sink** — an Atomic
  `BEGIN TRAN` envelope over the deploy is **viable** here. Remaining gates before firing the build: §2.1 (log
  headroom), §2.4 (the P7b throughput number vs the D2 window budget), §2.5 (CDC lag), §2.6 (RCSI blast radius);
  plus the D1/D2/D3 decisions. **The DDL envelope applies to the on-prem sink only** — the OutSystems managed env is
  DML-only (no DDL leg); its atomicity question is the *data-leg* wrapper, scoped separately (see the DML probes
  appended below if pursued).
