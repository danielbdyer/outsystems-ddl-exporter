# J5 — Real-UAT SQL Probe Sheet

> **What this is.** The sequential ops-spike probe sheet for J5 (real-UAT execute,
> OPEN-2). Run these SQL statements **as the DML-only managed login** the engine will
> use, against a **throwaway OSUSR entity table** in the corporate cloud-UAT
> environment. Record every outcome — the results resolve OPEN-1 / OPEN-2 / OPEN-3 /
> OPEN-5 / OPEN-6 / OPEN-7 and determine the per-kind disposition mix the engine uses.
>
> **Governance.** This is an ops action, not a write path. It is the spike
> `EXECUTION_PLAN.md` 5.1 and `PREFLIGHT_CLOUD_INSERTION.md` M5 sanction.
> No `--execute` is engaged; no R6 posture changes; no production data is touched.
> Use a throwaway OSUSR table — not a table with live data you cannot afford to corrupt.
>
> **Reference.** Probe design: `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md` §2.
> What remains genuinely open against the real estate:
> `AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md` §4.

---

## Setup — create the throwaway probe table

Run as a login with DDL rights (not the DML-only login) to create the sandbox. The
DML-only login probes it from P1 onward.

```sql
CREATE TABLE [dbo].[OSUSR_PROBE_J5] (
    [Id]       INT          NOT NULL IDENTITY(1,1),
    [Name]     NVARCHAR(50) NOT NULL,
    [ParentId] INT          NULL,
    CONSTRAINT [PK_OSUSR_PROBE_J5] PRIMARY KEY ([Id])
);

-- Seed one row so DELETE / TRUNCATE probes have something to act on.
INSERT INTO [dbo].[OSUSR_PROBE_J5] ([Name], [ParentId]) VALUES (N'seed', NULL);
GO
```

---

## P1 — Grant enumeration (OPEN-2 envelope)

**Resolves:** OPEN-2 — does the login hold the grants the engine requires?
**Gates:** every probe below.

```sql
-- Database-scope grants (what Preflight.captureGrantEvidence reads today).
SELECT permission_name, subentity_name
FROM sys.fn_my_permissions(NULL, 'DATABASE')
ORDER BY permission_name;

-- Object-scope grants on the probe table.
-- A per-table DENY escapes the database-scope check (named gap G1 —
-- AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md §2).
SELECT permission_name, subentity_name
FROM sys.fn_my_permissions('dbo.OSUSR_PROBE_J5', 'OBJECT')
ORDER BY permission_name;
```

**Record:** every `permission_name` returned at DATABASE scope and at OBJECT scope.
Expected minimum for a `grant: data` DML-only login: `SELECT`, `INSERT`, `DELETE`,
`UPDATE`. `ALTER` and `CREATE TABLE` should be absent. If `SELECT` is absent at object
scope despite appearing at database scope, a DENY is in effect (G1 gap applies).

---

## P2 — INSERT omitting the identity column; read the assigned key back (OPEN-1)

**Resolves:** OPEN-1 — `AssignedBySink` viability. The engine inserts without the PK
and captures the minted key via `INSERT … OUTPUT inserted.Id`
(`insertCaptureRow`, `TransferRun.fs:101-130`).
**Gates:** `AssignedBySink` disposition and `SurrogateRemap.fs`.

```sql
-- Omit [Id]; the platform mints it; OUTPUT captures the assigned key.
INSERT INTO [dbo].[OSUSR_PROBE_J5] ([Name], [ParentId])
OUTPUT inserted.[Id]
VALUES (N'probe-p2', NULL);
```

**Record:** does the statement succeed? What `Id` did the platform assign?
Success → `AssignedBySink` is viable.

---

## P3 — SET IDENTITY_INSERT — expect DENIED (OPEN-1)

**Resolves:** OPEN-1 — `PreservedFromSource` viability. If IDENTITY_INSERT is denied,
the DML-only path forecloses `KeepIdentity` / `PreservedFromSource`.
**Expected:** denied on a managed OutSystems cloud database.

```sql
SET IDENTITY_INSERT [dbo].[OSUSR_PROBE_J5] ON;
INSERT INTO [dbo].[OSUSR_PROBE_J5] ([Id], [Name], [ParentId])
VALUES (9999, N'probe-p3', NULL);
SET IDENTITY_INSERT [dbo].[OSUSR_PROBE_J5] OFF;
```

**Record:** error code and message. Denied → `PreservedFromSource` is not viable;
`AssignedBySink` is the live path (confirmed by P2). Permitted → note the message and
update OPEN-1 resolution.

---

## P4 — MERGE … OUTPUT surrogate capture (Contribution B gate)

**Resolves:** whether `MERGE … OUTPUT INTO @table` is permitted under the real grant.
This is the P4 trigger in `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md` §3.
**Gates:** the set-based `MERGE … OUTPUT` capture (Contribution B). Do not build until
P4 AND a measured per-row bottleneck both fire.

```sql
-- @map captures (source_key → assigned_key) without a natural key.
DECLARE @map TABLE (assigned_id INT, source_id INT);

MERGE INTO [dbo].[OSUSR_PROBE_J5] AS T
USING (VALUES (101, N'probe-p4a'), (102, N'probe-p4b')) AS S(old_key, [Name])
   ON 1 = 0                                        -- force NOT MATCHED → pure insert
WHEN NOT MATCHED THEN
    INSERT ([Name], [ParentId]) VALUES (S.[Name], NULL)
OUTPUT INSERTED.[Id], S.old_key INTO @map (assigned_id, source_id);

SELECT assigned_id, source_id FROM @map ORDER BY source_id;
```

**Record:** does the MERGE succeed? Does `@map` contain `(assigned_id, source_id)` pairs
where `assigned_id` differs from `source_id` (101/102)? That difference confirms the
platform minted new keys. Failure → Contribution B is blocked; the per-row
`INSERT … OUTPUT` path remains the only viable form.

---

## P5 — OUTPUT INTO targets: table variable and temp table

**Resolves:** which server-side storage the key-map can use (required by P4).
**Reference:** `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md` §3.

```sql
-- Table variable (no CREATE TABLE permission needed).
DECLARE @tv TABLE (id INT);
INSERT INTO @tv VALUES (1);
SELECT * FROM @tv;

-- Temp table (needs tempdb write — typically available).
CREATE TABLE #tmp (id INT);
INSERT INTO #tmp VALUES (1);
SELECT * FROM #tmp;
DROP TABLE #tmp;
```

**Record:** which forms succeed? Table variable needs no extra permission; temp table
needs `CREATE TABLE` in `tempdb`. If table variable + P4 both work, Contribution B's
`@map` approach is viable.

---

## P6 — DELETE vs TRUNCATE (OPEN-6)

**Resolves:** OPEN-6 — scope-clear strategy. The engine uses child-first `DELETE` for
the WipeAndLoad refresh (`TransferRun.fs` `wipeTargets`); `TRUNCATE` needs `ALTER`
which the DML-only login does not hold.
**Expected:** DELETE works; TRUNCATE is denied.

```sql
-- DELETE — should succeed under the DML login.
DELETE FROM [dbo].[OSUSR_PROBE_J5] WHERE [Name] LIKE N'probe-%';

-- TRUNCATE — expect DENIED (needs ALTER).
TRUNCATE TABLE [dbo].[OSUSR_PROBE_J5];
```

**Record:** DELETE rows deleted? TRUNCATE error code and message.
TRUNCATE denied (expected) → `WipeAndLoad` uses DELETE (already the engine's form).
TRUNCATE permitted → update OPEN-6 resolution.

---

## P7 — Batch ceilings and real-network throughput (OPEN-5)

**Resolves:** OPEN-5 — live-executor batch sizing and real-network round-trip cost.
The mock-container floor is ~271 rows/sec on loopback
(`AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md` §0 F2); the real-estate figure determines
whether the set-based `MERGE … OUTPUT` (Contribution B) is worth building.

```sql
-- Multi-row VALUES: 10 rows in one statement (SQL Server limit is 1,000 rows per
-- VALUES list; the engine batches at 2,500 rows via SqlBulkCopy for the bulk lane).
INSERT INTO [dbo].[OSUSR_PROBE_J5] ([Name], [ParentId])
VALUES
    (N'batch-01', NULL), (N'batch-02', NULL), (N'batch-03', NULL), (N'batch-04', NULL),
    (N'batch-05', NULL), (N'batch-06', NULL), (N'batch-07', NULL), (N'batch-08', NULL),
    (N'batch-09', NULL), (N'batch-10', NULL);

SELECT COUNT(*) AS row_count FROM [dbo].[OSUSR_PROBE_J5];
```

**Record:** does the multi-row INSERT succeed? Row count after the insert?
For real-network throughput: time 100 consecutive P2-form single-row INSERTs and divide.
That measured rows/sec figure is the wire-adjusted floor for deciding whether Contribution
B's trigger (b) — "per-row is the measured bottleneck" — fires.

---

## P8 — ALTER … NOCHECK CONSTRAINT and disable-trigger — expect DENIED (OPEN-6)

**Resolves:** OPEN-6 — whether the engine must handle CHECK constraints or triggers via
`NOCHECK` / disable-trigger. On a managed DML-only login these should be denied.
**Expected:** denied — the DML-only login does not hold `ALTER`.

```sql
-- ALTER TABLE NOCHECK — expect DENIED.
ALTER TABLE [dbo].[OSUSR_PROBE_J5] NOCHECK CONSTRAINT ALL;

-- Disable trigger (if the table had one) — expect DENIED.
-- DISABLE TRIGGER [some_trigger] ON [dbo].[OSUSR_PROBE_J5];
```

**Record:** error code and message. Denied (expected) → the engine must operate without
NOCHECK. Any OSUSR table with active triggers is a named open: per-row
`INSERT … OUTPUT` is rejected by SQL Server when the target has triggers
(`AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md` §4 item 2), which would force the
`OUTPUT INTO` form; survey P5 resolves the target.

---

## P9 — sys.* and INFORMATION_SCHEMA read

**Resolves:** whether the reconcile profiling (`reconcileAgainstSink`) and `verify-data`
paths can read the Sink population.
**Reference:** `ReadSide.fs` — the engine reads `INFORMATION_SCHEMA.COLUMNS` and
`sys.columns` / `sys.tables`.

```sql
-- Primary schema-read path.
SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
  AND TABLE_NAME = 'OSUSR_PROBE_J5'
ORDER BY ORDINAL_POSITION;

-- Identity-flag path (ReadSide.fs:317).
SELECT c.name, c.is_identity, t.name AS type_name
FROM sys.columns c
JOIN sys.tables  tb ON tb.object_id = c.object_id
JOIN sys.types   t  ON t.user_type_id = c.user_type_id
WHERE tb.name = 'OSUSR_PROBE_J5';

-- Row-count read (used by reconcileAgainstSink).
SELECT COUNT(*) AS row_count FROM [dbo].[OSUSR_PROBE_J5];
```

**Record:** do all three queries succeed? If `INFORMATION_SCHEMA.COLUMNS` returns zero
rows, check VIEW DEFINITION permission — Azure SQL least-privilege accounts may filter
metadata (`ReadSide.fs:303`).

---

## P10 — User-directory table readability (OPEN-7 / ReconciledByRule)

**Resolves:** whether the platform user table is SELECT-able and how it is keyed —
required by the `peer` / `golden` user re-key path (`ReconciledByRule` Dev→UAT).
**Reference:** `ReadSide.userDirectoryReadability` / `conventionalUserTables` in
`ReadSide.fs:1600` — the engine probes `OSSYS_USER`, `User`, `USERS` by default.

```sql
-- Probe each conventional candidate; note which exists and is readable.
SELECT TOP 5 * FROM [dbo].[OSSYS_USER]  ORDER BY (SELECT NULL);

-- If the above fails, try the app-entity form (eSpace-prefixed).
-- SELECT TOP 5 * FROM [dbo].[OSUSR_U_USER] ORDER BY (SELECT NULL);

-- Identify the email-shaped key column (the engine matches column names
-- containing 'EMAIL', case-insensitively — ReadSide.fs:1632).
SELECT TOP 1
    SCHEMA_NAME(t.schema_id) + '.' + t.name AS tbl,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.columns c
        WHERE c.object_id = t.object_id AND c.name LIKE '%EMAIL%'
    ) THEN 1 ELSE 0 END AS email_keyed
FROM sys.tables t
WHERE t.is_ms_shipped = 0
  AND t.name IN ('OSSYS_USER', 'User', 'USERS')
ORDER BY t.name;
```

**Record:** which table name exists and is readable? Is `email_keyed = 1`? This resolves
the `ReconciledByRule` / `validate-user-map` path. A readable, email-keyed user directory
means the `peer` / `golden` producer requires user-map reconciliation before any DML.

---

## P11 — Explicit transaction and lock timeout (OPEN-3 / OPEN-5)

**Resolves:** OPEN-3 (CDC-tracked sink vs long transactions) and OPEN-5 (chunk-commit
granularity — the engine may wrap per-chunk writes in an explicit transaction when P6 +
P11 confirm it is safe; `Preflight.fs` A3 scaffold).

```sql
-- Explicit transaction roundtrip.
BEGIN TRAN;
INSERT INTO [dbo].[OSUSR_PROBE_J5] ([Name], [ParentId]) VALUES (N'probe-p11', NULL);
-- Verify the insert is visible within the transaction.
SELECT COUNT(*) AS in_tran FROM [dbo].[OSUSR_PROBE_J5] WHERE [Name] = N'probe-p11';
ROLLBACK;
-- Verify rollback was effective.
SELECT COUNT(*) AS after_rollback FROM [dbo].[OSUSR_PROBE_J5] WHERE [Name] = N'probe-p11';

-- Lock timeout.
SELECT @@LOCK_TIMEOUT AS lock_timeout_ms;
SET LOCK_TIMEOUT 5000;
SELECT @@LOCK_TIMEOUT AS after_set;
```

**Record:** `BEGIN TRAN … ROLLBACK` atomicity — `in_tran` = 1, `after_rollback` = 0?
`@@LOCK_TIMEOUT` value? If the managed environment blocks explicit transactions, the A3
transactional chunk-commit is unavailable; the resumable marker (G10) path applies.

---

## Disposition decision tree

After running all probes, map the results to the per-kind disposition mix
(`PREFLIGHT_CLOUD_INSERTION.md` §0 / `PRESCOPE_TRANSFER.md` §6.4):

| P-result | Disposition |
|---|---|
| P3 denied AND P2 works | `AssignedBySink` is the live path for all IDENTITY kinds |
| P10 → user table readable and email-keyed | `ReconciledByRule` for the User kind (email re-key before any DML) |
| P3 permitted AND P2 works | `PreservedFromSource` is viable (blank-target case) |
| P2 fails | Re-check P1 grant; engine cannot load without INSERT |
| P4 works AND P5 table-var works | Contribution B (`MERGE … OUTPUT`) may be built once trigger (b) fires |
| P6 TRUNCATE denied (expected) | `WipeAndLoad` uses DELETE — engine's current form, no change needed |
| P8 ALTER denied AND P8 triggers present on OSUSR tables | `INSERT … OUTPUT` is blocked on trigger-bearing tables; use `OUTPUT INTO @map` (P5) |
| P11 transaction roundtrip works | A3 transactional chunk-commit is viable |
| P11 transaction blocked | Use resumable marker (G10) only |

---

## Cleanup

Run as a login with DDL rights after all probes are complete.

```sql
DROP TABLE IF EXISTS [dbo].[OSUSR_PROBE_J5];
```

---

## What to relay back

Post the following to the project as a DECISIONS entry resolving OPEN-1/2/3/5/6/7:

1. **P1** — database-scope `permission_name` list; any object-scope anomalies (DENY?)
2. **P2** — succeeded / failed; assigned `Id` value
3. **P3** — error message (expected: DENIED)
4. **P4** — succeeded / failed; sample `(assigned_id, source_id)` row
5. **P5** — table-var works / temp-table works
6. **P6** — DELETE rows deleted / TRUNCATE error message
7. **P7** — multi-row INSERT succeeded; measured per-row throughput (rows/sec over wire)
8. **P8** — ALTER error message; any evidence of triggers on OSUSR entity tables
9. **P9** — `INFORMATION_SCHEMA.COLUMNS` readable / `sys.columns` readable
10. **P10** — user-table name found; `email_keyed` value
11. **P11** — transaction atomicity confirmed; `@@LOCK_TIMEOUT` value

These results gate M5 (`PREFLIGHT_CLOUD_INSERTION.md`) and unlock `--execute` under R6.
