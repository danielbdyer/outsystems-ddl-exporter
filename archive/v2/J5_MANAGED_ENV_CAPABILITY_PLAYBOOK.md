# J5 — Managed-Environment Capability Playbook (generic; agent-constructed, operator-executed)

> **DEPRECATED 2026-06-15 — provenance only.** The J5 spike has been **run against a real
> managed OutSystems environment**; this playbook's intent is fulfilled. (The grant posture
> it found is a property of OutSystems managed environments generally — the DML-only managed
> login — not of any one instance; the title's "Cloud UAT" framing is historical.) Do **not**
> populate the §7 ledger template below. Its information has been relocated to the canonical
> stores: the findings ledger, the full P1–P11 probe taxonomy, the risk ladder, and the
> forward plan now live in `CHARTER_REVERSE_LEG_EXECUTION.md` (Part II); the canonical
> **resolution of OPEN-1/2/3/5/6/7** is recorded in `DECISIONS.md` (2026-06-15 — "J5
> managed-environment capability spike RUN…"). This file is retained as provenance for the methodology only — the operating
> model, the safety covenant, and the risk ladder the spike was executed under. The v1 sheet
> (`J5_UAT_SQL_PROBE_SHEET.md`) was superseded by this playbook; this playbook is now
> superseded by the run.

> **What this is.** The J5 ops spike (managed-environment execute, OPEN-2) as a **playbook**, not a
> script: a risk-ordered ladder of SQL capability probes that an agent **binds to
> existing OSUSR tables** selected with the operator from the model at hand. No
> sandbox DDL is created — the prerequisite throwaway table from the v1 probe sheet is
> intentionally deferred; the probes run against real, carefully chosen entity tables
> with small, marked, reversible writes.
>
> The probes still resolve OPEN-1 / OPEN-2 / OPEN-3 / OPEN-5 / OPEN-6 / OPEN-7 and keep
> their P-numbers from `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md` §2 for traceability.
> What changed is the operating model: **who constructs, who executes, what travels.**
>
> **Governance.** This is an ops action, not a write path. No `--execute` is engaged;
> no R6 posture changes. The agent never executes a write itself; the operator runs
> every statement. Findings travel beyond the probe session only in the sanitized
> ledger form of §7 — capability verdicts and standard SQL Server error numbers, never
> table names, row data, logins, or connection details.

---

## 1. The operating model — who does what

The loop, per statement or small statement group:

1. **The agent constructs.** Working with the model and the binding sheet (§3)
   alongside the operator, the agent instantiates the next rung's template against the
   selected tables and presents the exact SQL to the operator, together with: what it
   probes, the expected outcome, and — for anything that persists — the pre-staged
   revert statement in the same message.
2. **The operator executes** in the managed OutSystems environment SQL surface and pastes back the full
   resultset / error text to the agent. Full fidelity is fine here — this exchange
   stays within the probe session.
3. **The agent adjudicates**: advance to the next rung, issue a sideways exploration
   statement (e.g., a follow-up metadata read to explain a surprise), or stop and
   summarize. A failed rung halts the ladder until adjudicated — never skip past a
   failure.
4. **The findings travel conceptually.** When the ladder is done (or stopped), the
   agent writes the §7 ledger: per-probe verdicts in generic vocabulary. That ledger —
   and only that — travels beyond the probe session, feeding the DECISIONS entry that
   closes the OPENs.

The agent may iterate freely within the session (more reads, refined probes). The
asymmetry is deliberate: rich evidence inside, conceptual findings outside.

---

## 2. The safety covenant (binding rules — read before Rung A)

1. **Least risky first, one rung at a time.** The ladder order (§4) is the risk order:
   reads → transient writes → one reversible persistent write → expected-denied probes
   → set-based writes. Do not reorder.
2. **The rollback passage is proven before anything persists.** Nothing remains in any
   table beyond **one marked row** until the DELETE of that exact row has succeeded
   (Rung D). If SQL DELETE is denied, the stray row is removed via **Service Studio**
   before any further rung runs — and that removal is itself a first-class finding:
   *the rollback channel is Service Studio, not SQL*. If neither channel can remove the
   row, stop the spike entirely and record `rollback: none`.
3. **Row budget.** 1 row until DELETE is proven; at most 10 thereafter. The budget is
   raised only by explicit operator decision, recorded on the binding sheet.
4. **Every probe row is marked.** Each inserted row carries the marker
   `J5-PROBE-<yyyymmdd>-<rung>` in the designated text column, so probe rows are always
   findable (`WHERE <text_col> LIKE 'J5-PROBE-%'`) and hand-removable in Service Studio.
5. **"Expected denied" is a hypothesis, not a safety control.** Probes we expect the
   managed login to refuse run **late** (Rung E), and each is presented with its revert
   statement pre-staged — an unexpected success must be reverted in the same sitting.
6. **Destructive-if-permitted probes default to SKIP.** `TRUNCATE` and
   `ALTER … NOCHECK` were safe in v1 only because the target was a throwaway table.
   Against a real OSUSR table, a permitted TRUNCATE destroys real rows. Default
   posture: **infer from Rung A grant evidence** (both require `ALTER`, so an absent
   `ALTER` grant settles them) and run the live probe only against an
   operator-designated expendable table, if one exists.
7. **Nothing proprietary travels.** Table names, column names, row values, logins,
   server names, and connection strings stay within the probe session. Standard SQL Server
   error numbers (e.g., 229 permission-denied, 8102 identity-column update) are generic
   and transportable.

---

## 3. Phase 0 — table selection and the binding sheet

The agent reviews the model at hand and nominates candidates; the operator confirms.
Selection criteria for the primary probe table `<T1>`, best first:

- **Leaf entity** — no inbound FK referencers (so a probe row can never strand a
  referencing child, and DELETE has no cascade surface).
- **IDENTITY PK** — the disposition questions (P2/P3) are about identity minting.
- **At least one writable text column** to carry the probe marker, and only nullable or
  defaultable remaining columns (so a minimal single-row INSERT is constructible).
- **Low business criticality / low row count** — an expendable corner of the estate.
- **No triggers if possible** (check in Rung A; a trigger-bearing table changes which
  INSERT form is even testable — see P5/P8 notes).

Optional secondary table `<T2>`: an FK referencer of `<T1>`, only if relational probes
are later wanted; not required for the core ladder. The user-directory probe (P10)
needs no selection — it probes conventional names via metadata only.

**The binding sheet** (stays within the probe session; the agent keeps it current):

| Binding | Value |
|---|---|
| `<T1>` | schema-qualified primary probe table |
| `<PK>` | its IDENTITY PK column |
| `<TXT>` | the text column carrying the marker |
| `<COLS…>` | the minimal insertable column list beyond `<TXT>` |
| `<MARKER>` | `J5-PROBE-<yyyymmdd>-<rung>` |
| Baseline row count | recorded at Rung B, re-checked at every rung exit |
| Row budget | 1 (until Rung D passes), then ≤10 |
| Rollback channel | `unproven` → `SQL` \| `ServiceStudio` \| `none` (set at Rung D) |
| Expendable table for Rung E destructive probes | name or `none — infer from grants` |

---

## 4. The risk ladder

Every rung ends with the same exit check: **re-run the Rung B row count; it must equal
baseline plus exactly the rows the ledger says currently persist (normally zero).**

### Rung A — read-only: grants and metadata (P1, P9, P10)

No table rows are read or written. Templates the agent binds:

```sql
-- P1a: database-scope grants (what Preflight.captureGrantEvidence reads).
SELECT permission_name, subentity_name
FROM sys.fn_my_permissions(NULL, 'DATABASE')
ORDER BY permission_name;

-- P1b: object-scope grants on the selected table — a per-table DENY escapes
-- the database-scope check (named gap G1, AUDIT_2026_06_10 §2).
SELECT permission_name, subentity_name
FROM sys.fn_my_permissions('<T1>', 'OBJECT')
ORDER BY permission_name;

-- P9: the engine's metadata read paths.
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = '<T1 unqualified>'
ORDER BY ORDINAL_POSITION;

SELECT c.name, c.is_identity, c.is_nullable, c.is_computed
FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
WHERE t.name = '<T1 unqualified>';

-- Trigger presence on <T1> — decides whether INSERT…OUTPUT is even valid here
-- (SQL Server rejects OUTPUT-without-INTO on trigger-bearing targets).
SELECT tr.name, tr.is_disabled
FROM sys.triggers tr JOIN sys.tables t ON t.object_id = tr.parent_id
WHERE t.name = '<T1 unqualified>';

-- P10: user-directory readability — METADATA ONLY, no row read (no PII):
SELECT TOP 1
    SCHEMA_NAME(t.schema_id) + '.' + t.name AS tbl,
    CASE WHEN EXISTS (SELECT 1 FROM sys.columns c
                      WHERE c.object_id = t.object_id
                        AND c.name LIKE '%EMAIL%')
         THEN 1 ELSE 0 END AS email_keyed
FROM sys.tables t
WHERE t.is_ms_shipped = 0 AND t.name IN ('OSSYS_USER', 'User', 'USERS')
ORDER BY t.name;
-- SELECT-ability without reading a row:
SELECT TOP 0 * FROM <found user table>;
```

**Adjudicate before leaving Rung A:** if `INSERT` or `DELETE` is absent at both scopes,
the spike's write rungs are already answered (denied) — record and stop early. If
`ALTER` is absent at both scopes, mark P6-TRUNCATE and P8 as **settled by inference**
(denied) and skip their live probes.

### Rung B — read-only on the selected table (the baseline)

```sql
-- The baseline count every later rung re-checks.
SELECT COUNT(*) AS baseline FROM <T1>;

-- Shape check (resultset stays within the session; prefer naming columns over * if the
-- table is wide or sensitive).
SELECT TOP 5 <PK>, <TXT> FROM <T1> ORDER BY <PK> DESC;

-- Confirm no prior probe rows linger from an earlier session.
SELECT COUNT(*) AS stray_probes FROM <T1> WHERE <TXT> LIKE 'J5-PROBE-%';
```

### Rung C — transient writes: nothing persists even on failure (P11, P5)

These are the least risky writes because a working ROLLBACK (or tempdb scope) means no
cleanup dependency on DELETE at all.

```sql
-- P11: explicit transaction roundtrip — the insert never persists.
BEGIN TRAN;
INSERT INTO <T1> (<TXT>, <COLS…>) VALUES (N'<MARKER>', <minimal values…>);
SELECT COUNT(*) AS in_tran FROM <T1> WHERE <TXT> = N'<MARKER>';
ROLLBACK;
SELECT COUNT(*) AS after_rollback FROM <T1> WHERE <TXT> = N'<MARKER>';
-- Expect in_tran = 1, after_rollback = 0. Then:
SELECT @@LOCK_TIMEOUT AS lock_timeout_ms;
```

```sql
-- P5: OUTPUT INTO targets — touches only a table variable and tempdb.
DECLARE @tv TABLE (id INT);
INSERT INTO @tv VALUES (1);
SELECT * FROM @tv;

CREATE TABLE #tmp (id INT);
INSERT INTO #tmp VALUES (1);
SELECT * FROM #tmp;
DROP TABLE #tmp;
```

**Adjudicate:** if P11's rollback verifiably works (`after_rollback = 0`), the
transaction wrapper is itself a rollback channel for later rungs — note it. If
`BEGIN TRAN` is refused, Rung D becomes the only safe-passage proof; proceed with
extra care.

### Rung D — the safe-passage pair: one row in, the same row out (P2 + P6-DELETE)

The pivotal rung. One marked row is inserted **and immediately deleted**; the pair
proves `AssignedBySink` viability (P2) and the rollback passage (P6's DELETE half) in
one breath.

```sql
-- P2: INSERT omitting the IDENTITY column; capture the platform-minted key.
-- (If Rung A found triggers on <T1>, use OUTPUT INTO a table variable instead —
-- that substitution is itself the P5/trigger finding.)
INSERT INTO <T1> (<TXT>, <COLS…>)
OUTPUT inserted.<PK>
VALUES (N'<MARKER>', <minimal values…>);

-- P6 (DELETE half): remove exactly that row, by the key just returned.
DELETE FROM <T1> WHERE <PK> = <returned key> AND <TXT> = N'<MARKER>';

-- Exit check.
SELECT COUNT(*) AS probe_rows FROM <T1> WHERE <TXT> LIKE 'J5-PROBE-%';  -- expect 0
SELECT COUNT(*) AS total FROM <T1>;                                     -- expect baseline
```

**Adjudicate — three outcomes:**
- **INSERT and DELETE both succeed** → `AssignedBySink` viable; rollback channel =
  `SQL`; row budget rises to 10; proceed.
- **INSERT succeeds, DELETE denied** → STOP. Remove the row via **Service Studio**,
  confirm the count is back to baseline, set rollback channel = `ServiceStudio`. Then
  decide with the operator whether later rungs (which all create rows needing cleanup)
  are worth the per-row Service Studio cost — possibly continue at budget 1.
- **INSERT denied** → the write surface is closed (or object-DENY'd — compare with
  P1b); record and stop the write rungs. The spike's headline OPEN-2 answer may
  already be `platform-API-only`.

### Rung E — expected-denied probes (P3; P8 and P6-TRUNCATE by inference or expendable table)

Run only after Rung D settled the rollback channel.

```sql
-- P3, split in two so the toggle is probed without any insert:
SET IDENTITY_INSERT <T1> ON;   -- expect: denied (error 8106/229-class)
SET IDENTITY_INSERT <T1> OFF;  -- only reached if ON succeeded
```

If — unexpectedly — `SET IDENTITY_INSERT ON` is **permitted**, the optional second half
inserts one marked row with an explicit `<PK>` far above the current maximum, reads it
back, and deletes it via the proven channel. Only run this half if the operator wants
`PreservedFromSource` definitively settled; the toggle being permitted is already the
OPEN-1 headline.

```sql
-- P8 / P6-TRUNCATE: default SKIP — settled by Rung A inference when ALTER is
-- absent (both require ALTER). Run live ONLY against the operator-designated
-- expendable table, with the revert in the same batch:
ALTER TABLE <expendable> NOCHECK CONSTRAINT ALL;   -- expect: denied
ALTER TABLE <expendable> WITH CHECK CHECK CONSTRAINT ALL;  -- the revert, if not
-- TRUNCATE TABLE <expendable>;                    -- expect: denied; destructive if not
```

### Rung F — set-based forms and the wire cost (P4, P7)

All rows marked; all deleted via the proven channel; budget ≤10.

```sql
-- P4: MERGE … OUTPUT INTO — the set-based (source→assigned) capture form.
DECLARE @map TABLE (assigned_id INT, source_id INT);
MERGE INTO <T1> AS T
USING (VALUES (101, N'<MARKER>'), (102, N'<MARKER>')) AS S(old_key, txt)
   ON 1 = 0                                   -- force NOT MATCHED → pure insert
WHEN NOT MATCHED THEN
    INSERT (<TXT>, <COLS…>) VALUES (S.txt, <minimal values…>)
OUTPUT INSERTED.<PK>, S.old_key INTO @map (assigned_id, source_id);
SELECT assigned_id, source_id FROM @map ORDER BY source_id;

-- Cleanup via the proven channel:
DELETE FROM <T1> WHERE <PK> IN (SELECT assigned_id FROM @map);
```

```sql
-- P7a: multi-row VALUES within budget (here 5), then cleanup.
INSERT INTO <T1> (<TXT>, <COLS…>)
VALUES (N'<MARKER>', …), (N'<MARKER>', …), (N'<MARKER>', …),
       (N'<MARKER>', …), (N'<MARKER>', …);
DELETE FROM <T1> WHERE <TXT> = N'<MARKER>';
```

**P7b — the wire-cost measurement, net-zero rows:** time ~20 repeated single-row
INSERT…OUTPUT + targeted-DELETE pairs and divide. This bounds the real-network per-row
round-trip without ever holding more than one probe row. (It measures the pair, not the
bare insert — a conservative floor, which is exactly what the Contribution B
bottleneck-trigger decision needs against the mock-container ~271 rows/sec figure,
`AUDIT_2026_06_10` §0 F2.)

**Final exit check:** stray-probe count 0; total = baseline; binding sheet closed out.

---

## 5. Adjudication notes for the agent

- A denial's **error number** distinguishes a grant refusal (229) from a feature
  refusal (e.g., 8106 identity, 334 OUTPUT-with-trigger) — relay the number into the
  ledger; it is generic and transportable.
- A surprise (success where denial was expected, or vice versa against P1's evidence)
  warrants a sideways metadata probe before advancing — the cause (object-scope DENY,
  trigger, computed column) is usually one read away.
- When P1b and P1a disagree (object DENY under a database grant), that is the **G1 gap
  live in the real estate** — flag it prominently; it changes the engine's preflight
  refinement priority.

## 6. Stop conditions

Stop the ladder and write the ledger as-is when any of these hits:

- A persisted probe row cannot be removed by SQL **or** Service Studio.
- INSERT is denied (the write rungs are moot).
- Any statement touches more rows than intended (count drift at a rung exit).
- The operator calls it — for any reason, no justification needed.

---

## 7. The findings ledger — the transportable artifact

The only artifact that travels beyond the probe session. Generic vocabulary only: verdicts,
standard error numbers, and the bindings replaced by their roles (`the probe table`,
`the user directory`).

| Probe | Verdict (`permitted` / `denied <errno>` / `skipped — inferred from P1` / `not reached`) | Resolves |
|---|---|---|
| P1 DB-scope grant set | list of permission names (generic) | OPEN-2 envelope |
| P1 object-scope anomalies | none / DENY observed (G1 live) | G1 priority |
| P9 metadata readability | | reconcile profiling, `verify-data` |
| P10 user directory found / email-keyed | | OPEN-7, `ReconciledByRule` |
| P11 transaction roundtrip; lock timeout | | OPEN-3 / OPEN-5, A3 scaffold |
| P5 table-var / temp-table | | key-map rendering target |
| P2 identity-omitting INSERT + key readback | | OPEN-1, `AssignedBySink` |
| P6 DELETE of own row | | OPEN-6, **rollback channel** |
| — Rollback channel | `SQL` / `ServiceStudio` / `none` | the safe-passage finding |
| P3 IDENTITY_INSERT toggle | | OPEN-1, `PreservedFromSource` |
| P8 ALTER NOCHECK | | OPEN-6 |
| P6 TRUNCATE | | OPEN-6 |
| P4 MERGE…OUTPUT INTO | | Contribution B trigger (a) |
| P7 multi-row VALUES; measured pair-throughput (rows/sec) | | OPEN-5, Contribution B trigger (b) |
| Triggers present on probed entity tables | yes / no | INSERT…OUTPUT form selection |

**Disposition mapping (unchanged from v1):** P3 denied + P2 works ⇒ `AssignedBySink` is
the live path; P10 readable + email-keyed ⇒ `ReconciledByRule` for the User kind; P3
permitted ⇒ `PreservedFromSource` survives; P4 + P5 permitted and P7b shows per-row as
the bottleneck ⇒ both Contribution B triggers fire. The ledger feeds the DECISIONS
entry closing OPEN-1/2/3/5/6/7 and gates M5 (`PREFLIGHT_CLOUD_INSERTION.md`).

---

## 8. Relation to the v1 probe sheet

The v1 sheet (`J5_UAT_SQL_PROBE_SHEET.md`, superseded by this file) assumed a
throwaway `CREATE TABLE` sandbox — deferred here because DDL in the managed UAT may be
unavailable or undesirable. If a sandbox table *does* become available later, the v1
flow is just this playbook with the binding fixed to that table, the row budget
relaxed, and Rung E's destructive probes un-skipped. The probe semantics and the
OPEN-resolution mapping are identical.
