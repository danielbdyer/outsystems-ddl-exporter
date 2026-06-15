# Charter & Compendium — Reverse-Leg DML Movement to Execution-Ready

> **What this is.** The forward charter *and* the consolidated knowledge base for taking
> reverse-leg DML movement of legacy application data — the **B→A** load that re-keys an
> OutSystems estate into a managed OutSystems environment — from
> "engine proven on mock" to "an operator trusts running it against hundreds of millions of real rows."
>
> It is the synthesis of two deep background discovery sweeps (2026-06-15): a J5-foundation
> sweep (12 + critic + 6 fill agents) and an execution-maturity sweep (7 + critic + 6 fill
> agents), ~5M subagent tokens total, plus the operator's own J5 run on a real managed OutSystems
environment. Every claim
> is `file:line`-grounded against HEAD. Parts II–VIII are the **compendium** (what is true);
> Part IX is the **charter** (what we do about it); Part X is reference.
>
> **Dated 2026-06-15.** This discharges the "J5 preemption" that the entire Constellation/
> Lapidary backlog was queued behind (`CONSTELLATION_BACKLOG.md` §5;
> `V1_FULL_EXPORT_RECONCILIATION_PLAN.md` #8).

---

## Table of contents

- **Part I** — Orientation & the two reframes
- **Part II** — The J5 capability spike (lineage, ladder, the P1–P11 taxonomy, the ledger)
- **Part III** — The OPEN catalog (canonical definitions + resolution status)
- **Part IV** — The Use-Case Ontology (identity, dispositions, the torsor, faithfulness)
- **Part V** — The data leg & the transfer isomorphism
- **Part VI** — The scale program (the throughput math, the capture lane, the remap, the journal)
- **Part VII** — Current code state: the execution surface (BUILT / ARMED / GAP inventory)
- **Part VIII** — Program context (preemption, R6/cutover, the DataVerification thread)
- **Part IX** — The charter: sequenced path, decisions, risk register
- **Part X** — Appendices: code-citation index, glossary, provenance

---

# Part I — Orientation & the two reframes

Two facts set the shape of everything that follows.

**Reframe 1 — The gate is closed.** In the repo, J5 (the managed-environment capability spike) was the
one true critical-path item: "ops-gated, STILL-OPEN" (`AUDIT_2026_06_13_INVARIANT_NEAR_MISS_HUNT.md:315`),
its §7 findings ledger a blank template, with **no `DECISIONS.md` entry closing
OPEN-1/2/3/5/6/7** (OPEN-2 appears exactly once, `DECISIONS.md:21138`, and that is a *residual*
note that leaves it open, not a closure). The reason it never ran in-repo: "no OSSYS source… it
must be witnessed against a live estate" (`DECISIONS.md:22559-22561`). The capability questions,
however, were already de-risked to a **re-run** — P1/P2/P3/P6 answered on mock/Docker, witnessed by
four green `ReverseLeg*Tests.fs` suites (`HANDOFF.md:1581-1586`). **The operator has now run J5
against a real managed OutSystems environment** (Part II §6) and produced the verdicts — answering the residual
the whole program waited on.

**Reframe 2 — The engine is largely already built.** This is an integration-and-validation effort,
not a green-field build. The two-phase transfer engine, streaming ingestion, chunk-level resume, the
set-based capture lane with capability descent, the packed surrogate remap, dry-run scaffolding, and
within-level parallelism (on the seed lane) all exist and are witnessed — but only at Docker scale
(~100k rows), never on a real wire, and with specific, *named* gaps. The forward work is wiring what
exists into a trustworthy whole, running the one experiment everything is loopback-pending on, and
closing the named gaps.

**How to read this.** Throughout, the readiness vocabulary is the repo's own: **BUILT** (code exists,
witnessed), **ARMED** (designed, build deferred behind a named numeric wake-condition), **GAP**
(neither built nor designed), **REFUSED** (deliberately declined by name).

---

# Part II — The J5 capability spike

## 1. What J5 is, and why it exists

J5 is the capability spike originally written as the **Managed-Environment Capability Playbook**
(`J5_MANAGED_ENV_CAPABILITY_PLAYBOOK.md`, now deprecated): a SQL ops spike that runs a risk-ordered ladder of
capability probes against real OSUSR entity tables **in a managed OutSystems environment** to
discover *which identity disposition the managed DML-only grant even permits*. The premise: a
DML-only managed surface may forbid `IDENTITY_INSERT`, which forecloses `PreservedFromSource` and
forces `AssignedBySink` — and that single fact reshapes the entire data-movement strategy. The grant
posture is a property of OutSystems **managed** environments generally (UAT, production), not of any
one instance — so the disposition decision holds across them, de-risking the production cutover, not
only a single-environment load.

The probes originate in `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md` §2 as a four-column table
(`# | Probe | Resolves | Gates`), elaborating `EXECUTION_PLAN.md` §5.1's one-line sketch ("connect
to a throwaway entity table and test IDENTITY_INSERT/INSERT").

## 2. Lineage: v1 probe sheet → v2 capability playbook

- **v1** (`J5_UAT_SQL_PROBE_SHEET.md`, commit `bf7c049e`, now superseded) was a literal,
  copy-paste **SQL script** against a throwaway `CREATE TABLE dbo.OSUSR_PROBE_J5` sandbox created by
  a DDL-rights login. Destructive probes (P6 TRUNCATE, P8 ALTER NOCHECK) ran live; it ended with
  `DROP TABLE`.
- **v2** (`J5_MANAGED_ENV_CAPABILITY_PLAYBOOK.md`, commit `237140ae`) is a **playbook, not a script**. It
  *defers* the sandbox DDL and binds probes at runtime to real OSUSR tables via a Phase-0 binding
  sheet. Commit `450e5c43` then stripped the deployment-context framing.
- **The headline change is the operating model — who constructs, who executes, what travels.** The
  agent constructs SQL; the **operator executes every statement**; full-fidelity results stay inside
  the probe session; only the sanitized §7 findings ledger (verdicts + standard SQL Server error
  numbers + roles, never table names / row data / logins) travels out.

## 3. The safety covenant (7 rules)

Least-risky-first, one rung at a time; prove rollback before anything persists (Service Studio is
the documented fallback channel, else `rollback: none` and stop); row budget 1 until DELETE is
proven then ≤10; **mark every probe row** `J5-PROBE-<date>-<rung>`; "expected-denied" is a
hypothesis not a safety control; destructive-if-permitted probes default SKIP and are inferred from
grants; nothing proprietary travels. *(The operator's run consciously replaced the marker rule with
delete-by-captured-key — see §6.)*

## 4. The risk ladder (Rungs A–F)

| Rung | Content | Probes |
|---|---|---|
| **A** | Read-only: grants + metadata (database- and object-scope `sys.fn_my_permissions`, columns, triggers, user directory) | P1, P9, P10 |
| **B** | Baseline `COUNT(*)` + shape + stray-probe check | — |
| **C** | Transient writes that never persist (explicit `BEGIN TRAN…ROLLBACK`; table-var / temp-table) | P11, P5 |
| **D** | **The safe-passage pair** — one marked row in (`INSERT … OUTPUT`) and the same row out (`DELETE`) | P2, P6-DELETE |
| **E** | Expected-denied probes (`IDENTITY_INSERT`; `ALTER NOCHECK` / `TRUNCATE` by inference or expendable table) | P3, P8, P6-TRUNCATE |
| **F** | Set-based forms + wire cost (`MERGE…OUTPUT INTO`; multi-row batch; net-zero throughput timing) | P4, P7 |

Rung D is pivotal — its three outcomes are: both succeed → AssignedBySink viable, channel = SQL,
budget → 10; INSERT ok / DELETE denied → STOP, remove via Service Studio, channel = ServiceStudio;
INSERT denied → write surface closed.

## 5. The complete P1–P11 taxonomy

| Probe | What it tests | Rung | Resolves | Gates / disposition | Code anchor |
|---|---|---|---|---|---|
| **P1** | Grant enumeration (`sys.fn_my_permissions` DATABASE + OBJECT scope) | A | OPEN-2 | the whole write envelope; G1 object-scope DENY | `Preflight.captureGrantEvidence` |
| **P2** | INSERT omitting the IDENTITY column; read the assigned key back | D | OPEN-1 | **AssignedBySink** | `TransferRun.fs:101-130` (`insertCaptureRow`) |
| **P3** | `SET IDENTITY_INSERT ON` (expected denied) | E | OPEN-1 | **PreservedFromSource** (Bulk KeepIdentity) | `Bulk.copyRows` KeepIdentity |
| **P4** | `MERGE … OUTPUT INSERTED.<pk>, S.src INTO @map` | F | — | **Contribution B** set-based capture, trigger (a) | `SurrogateCapture.mergeOutputChunk` |
| **P5** | Table-variable / temp-table `OUTPUT INTO` targets | C | — | how the key-map renders server-side | `SurrogateCapture` staging |
| **P6** | DELETE (vs TRUNCATE — TRUNCATE needs ALTER) | D / E | OPEN-6 | **rollback channel**; v2 splits DELETE (Rung D) from TRUNCATE (Rung E, SKIP) | — |
| **P7** | Multi-row VALUES + parameter ceiling + INSERT BULK; **P7b** = net-zero wire-cost timing | F | OPEN-5 | **Contribution B** trigger (b): per-row vs set-based bottleneck | — |
| **P8** | `ALTER … NOCHECK CONSTRAINT` / disable-trigger (expected denied) | E | OPEN-6 | loading into a constrained schema | — |
| **P9** | `sys.*` + `INFORMATION_SCHEMA` reads | A | — | reconcile profiling + `verify-data` | `TransferRun.fs:208-234` (`reconcileAgainstSink`) |
| **P10** | User-directory readability + email-keying (`OSSYS_USER`/`User`/`USERS`) — metadata-only | A | OPEN-7 | **ReconciledByRule** user re-key | `ReadSide.fs:1600,1632` |
| **P11** | Explicit `BEGIN TRAN…COMMIT` + `LOCK_TIMEOUT` | C | OPEN-3, OPEN-5 | chunk-commit granularity vs CDC; the A3 scaffold | — |

## 6. The capability ledger (managed OutSystems environment, 2026-06-15) — canonical here + in `DECISIONS.md`

Recorded in the playbook's transportable form (verdicts + standard error numbers + roles). The
playbook itself is **deprecated** (its §7 template is *not* populated); this section and the
`DECISIONS.md` 2026-06-15 entry ("J5 managed-environment capability spike RUN…") are the canonical homes.

| Probe | Verdict | Meaning |
|---|---|---|
| P1 write envelope | **permitted**: SELECT/INSERT/UPDATE/DELETE; **no ALTER** grant | OPEN-2 closed; estate is not read-only |
| P2 identity-omitting INSERT + readback | **permitted** — DB mints the key | **AssignedBySink is the live path** |
| P3 `SET IDENTITY_INSERT` | **denied (error 1088)** | **PreservedFromSource is dead** |
| P6 DELETE of own row | **permitted** — count restored to baseline | **rollback channel = SQL** |
| P11 transaction roundtrip | **permitted** — ROLLBACK clean | transaction wrapper is a rollback channel |
| P5 table-var / temp-table | **permitted** | SQL-side key-map render target available |
| P4 `MERGE…OUTPUT INTO` | **permitted** — source→assigned pairs returned | set-based capture available (Contribution B (a) ✓) |
| P7a multi-row batch (5) | **permitted** — captured + deleted clean | small-batch correctness proven |
| P8 NOCHECK / P6-TRUNCATE | **denied — inferred from absent ALTER** | constrained-schema bypass unavailable |

**One root, three denials.** No ALTER grant settles P3 (the `1088`), P8 (NOCHECK), and P6-TRUNCATE
together — precisely the §2-rule-6 inference the playbook predicted.

**The no-marker refinement.** Per operator constraint (no schema change, no artificial business-marker
values on real tables), cleanup was by **captured key**, not by marker row. This is not a compromise:
the captured key is a *more precise* cleanup handle than a marker, requires no schema change, and is
the very artifact needed for FK re-keying. The no-marker path and the AssignedBySink path are the same
mechanism — *capture the assigned key on insert; that capture is simultaneously the cleanup handle and
the FK-remap table.*

**The repo-vs-reality gap (now closed).** At the moment of the run the repo recorded none of this:
§7 was a blank template (`J5_MANAGED_ENV_CAPABILITY_PLAYBOOK.md:342-358`) and no closing DECISIONS entry
existed. Per operator decision the gap is closed by **deprecating** the playbook and relocating the
ledger to the canonical stores — this compendium and the `DECISIONS.md` 2026-06-15 closing entry —
not by populating §7 (Step 0, done).

**The "collapse letter."** The 2026-06-10 LE-3 addendum (`HANDOFF.md:1581-1586`) had already narrowed
the residual: *"the J5 managed-environment spike is now a re-run of a proven suite against a real connection —
P1/P2/P3/P6 are answered on mock infrastructure,"* leaving four real-connection items: the actual
grant envelope + the **G1** object-scope-DENY gap; platform triggers on OSUSR tables (force OUTPUT
INTO, survey P5); and P7 batch ceilings over a real wire. The operator's run settles the grant envelope
and the capability set; the genuine residuals that remain are below.

## 7. Residuals after the run

- **P7b — throughput over the real wire** (the throughput-floor / cutover-window confirmation). *Load-bearing.*
- **P10 / OPEN-7 — user directory** readable + email-keyed (confirms ReconciledByRule is available).
- **G1 — object-scope DENY** check (P1b): does any per-table DENY diverge from the database grant?
- **P5 estate survey** — which OSUSR tables carry platform triggers (the capture ladder handles them;
  we want the map).
- **OPEN-3 / CDC** — gates whether the NM-73 EXCEPT auto-fallback can be armed (Part VIII §3).

## 8. The disposition mapping (invariant v1→v2)

`P3 denied + P2 works ⇒ AssignedBySink` is the live path · `P10 readable + email-keyed ⇒
ReconciledByRule` for the User kind · `P3 permitted ⇒ PreservedFromSource survives` · `P4 + P5
permitted and P7b shows per-row as the bottleneck ⇒ both Contribution-B triggers fire`. The §7 ledger
feeds the DECISIONS entry closing OPEN-1/2/3/5/6/7 and gates **M5** (`PREFLIGHT_CLOUD_INSERTION.md`).

---

# Part III — The OPEN catalog

Canonical definitions live in `PRESCOPE_TRANSFER.md` §13 (lines 909-945; page order OPEN-1,7,2,3,4,5,6).
J5 probes resolve OPEN-1/2/3/5/6/7; OPEN-4 is governance, resolved by operator sign-off, not a probe.

| OPEN | The question | Probe(s) | Status after the run |
|---|---|---|---|
| **OPEN-1** | Identity: blank target? `IDENTITY_INSERT` permitted? — gates the disposition mix | P2, P3 | **Resolved** → AssignedBySink; PreservedFromSource dead |
| **OPEN-2** | Platform write surface: direct SQL vs platform API ("the single biggest external dependency — confirm first") | P1 | **Resolved** → direct SQL writes permitted |
| **OPEN-3** | CDC tracking (`§8.4`) | P11 | **Partial** → transaction/lock proven; CDC path still gates NM-73 |
| **OPEN-4** | Governance / R6 scope | — | Resolved by operator sign-off (not a probe) |
| **OPEN-5** | Bulk lane vs two-phase | P7, P11 | **Partial** → set-based + small batch proven; P7b throughput unmeasured |
| **OPEN-6** | Constraints / triggers / NOCHECK | P6, P8 | **Resolved** → rollback channel = SQL; NOCHECK/TRUNCATE denied |
| **OPEN-7** | Connection apparatus scope + user-directory re-key | P10 | **Open** → user directory not yet probed; environments/concurrency unscoped |

Note: in `DECISIONS.md` these OPENs are **not** closed; OPEN-2's lone hit (`:21138`) is a residual.
The closing entry is Step 0 work.

---

# Part IV — The Use-Case Ontology

`THE_USE_CASE_ONTOLOGY.md` (+ `.acceptance` / `.fitness` / `.obligations` satellites) is the conceptual
core. It frames change-over-time as an "alphabet" of structural + cross-cutting moves (the
amino-acid/protein expository framing — not type names) that fold into operator workflows (P-1..P-9).

## 1. The three name-spaces and SsKey

- **Identity = `SsKey`** (invariant — *what it is*). A four-variant DU
  (`OssysOriginal | Synthesized | DerivedFrom | V1Mapped`); identity-survives-rename (A1); the conserved
  charge under every move (A43). Comparison matches by SsKey before comparing anything; physical names
  are never reused. (`THE_USE_CASE_ONTOLOGY.md:951-968,1071-1072`)
- **Designation = `Name`** (*what we call it* — where renames act).
- **Realization = the physical object name** (*what the substrate bears* — derived; production policy
  `Realization := Designation`). Realization is itself a **disposition**: **A** = cloud physical
  `OSUSR_*`, **B** = on-prem logical clean names. Conflating the three is the "physical column rename"
  near-miss.

## 2. IdentityDisposition (the three-way classification)

`PreservedFromSource | AssignedBySink | ReconciledByRule` — the `ofKind` classifier lives at
`SurrogateRemap.fs:65-83`.

- **AssignedBySink** — the sink mints a new IDENTITY surrogate per-DB; captured per-row via `OUTPUT
  inserted.<pk>` feeding the `SurrogateRemapContext`. **Refused before any write** when the IDENTITY is
  cyclic/self-referential (phase-2 would key on a source PK the sink replaced) or composite/multi-column
  (single-leg capture would truncate the surrogate). Graded L2 "faithful". **Shipped 2026-05-31
  (acyclic).** The live path when P3 is denied and P2 works.
- **PreservedFromSource** — source surrogate preserved (Bulk KeepIdentity); the sink carries the
  source's identity values. Survives only if `IDENTITY_INSERT` is permitted (the blank-target case). A
  self-FK is *not* a refusal here (contrast cyclic AssignedBySink). **Dead on this estate** (P3 denied).
- **ReconciledByRule** — the Dev→UAT / golden **user re-key**: rows matched by reconciled business key
  (email) and FKs re-pointed; a re-keyed row is an **Update** (relationship preserved modulo surrogate),
  **never Delete+Insert**. Strategies: `ByEmail / BySsKey / ManualOverride / MatchByColumn /
  FallbackToSystemUser` (`Reconciliation.reconcileKind`). Gated by P10. Graded "partial-named-gap" at the
  fitness snapshot (missing the pre-write `validate-user-map` gate, **N4**).

## 3. Reidentify, Move, and the validate-user-map gate

- **Reidentify** (structural move #9, identity/cross-plane): the same logical entity carries different
  surrogates per substrate; the correspondence is reconstructed (match by business key, re-point FKs).
  Its data realization keys the MERGE `ON` clause on reconciled identity, not raw surrogate, with
  two-phase FK re-point.
- **Move (cross-substrate)** (structural move #10): rows flow source→sink, identity-reconciled,
  minimality CDC-measured — the data-plane projection of the whole migration.
- **`validate-user-map` gate (N4 / AC-I5)** — the pre-flight for the user re-key: every orphan source
  `UserId` must be mapped, and every target in the allow-list, *before any SQL is emitted*; it must
  halt before emission. At the fitness snapshot it was **absent as a pre-write gate**
  (`isFullyMapped`/`unmatchedCount` existed but nothing gated on them); orphans surfaced only post-write
  via exit-9. Closing this is Phase 2 work.

## 4. The torsor and "Contribution A/B" — disambiguation (important)

"A/B" and "Contribution" carry **two different meanings**; do not conflate:

- **Ontology / torsor sense** — `WAVE_6_ALGEBRA` torsor: **Realization A** (cloud-physical `OSUSR_*`) vs
  **Realization B** (on-prem-logical clean). "Cloud insertion" is `emit(B⊖A)` rendering the model in
  disposition A.
- **Transfer-substantiation sense** — **Contribution A** = the OPEN-2 capability-survey *instrument*
  (the P1–P11 probe sheet itself); **Contribution B** = the `MERGE … OUTPUT` *set-based surrogate
  capture* (a realization-layer A36 swap; no IR/lexicon change). Its two firing triggers: (a) survey P4
  confirms `MERGE + OUTPUT INTO @table` permitted under the real grant; (b) P7b shows per-row round-trips
  are the measured bottleneck. The J5 playbook's "Contribution B triggers" means **this** sense.

## 5. Faithfulness classes

Every move is totally classified: `faithful / lossy-with-warning / refuse-unless-declared`. Four
erasure-surfacing channels enforce "named and closed": a Tolerance entry · a structured diagnostic · a
fail-loud refusal · an `AxiomTests.fs` Skip stub. **The cardinal sin: a silent erasure is strictly worse
than no claim.**

---

# Part V — The data leg & the transfer isomorphism

## 1. The isomorphism claim

- **Transfer isomorphism (data-level H-050)**: a Transfer reproduces source rows in the sink up to named
  identity-remap tolerances — the data-level extension of the schema adjunction **H-050**
  (`Ingestion ∘ Projection = id`, up to named lossy fields). Proven via a data-level canary
  (`PhysicalSchema` + row-digest equality). (`PRESCOPE_TRANSFER.md:39-61`)
- The schema-level form is proven by `AdjunctionLawTests.fs`; the data-level by the round-trip canary.

## 2. The synthetic producer (σ/π)

`π: Data → Profile` (forget rows, keep marginals/counts/null-rates/cardinalities); `σ: Profile → Data`,
the approximate right-inverse such that `π ∘ σ ≈ id_Profile` within sampling ε. The synthetic producer's
correctness theorem + canary. (`THE_SYNTHETIC_DATA_DESIGN.md:54-72`)

## 3. The producer trinity

Three origins of production-like data feeding cloud insertion, all moving the same SsKey-stable model:
**synthetic** (generated from Profile — BUILT), **peer** (same-rendition cell A→A — design), **legacy**
(logical→physical B→A reverse leg — design; *this charter's subject*). (`THE_DATA_PRODUCERS.md:64-93`)

## 4. The "golden" discipline (the user re-key, concretely)

The peer producer's flagship flow: **exclude `dbo.User` rows** from the copied set, reconcile each source
user to a sink user **by email**, reidentify every user-FK to the sink surrogate — gated by
`validate-user-map` before any DML. (`THE_DATA_PRODUCERS.md:96-121`) This is the model the reverse-leg
user re-key must implement (Part VII §6).

## 5. How J5 underwrites the isomorphism

P2 (identity-minting capture), P3 (IDENTITY_INSERT — refused), P4 (MERGE-OUTPUT set capture), and P5
(temp-table render target) are exactly the capabilities the isomorphism's two-phase FK re-point depends
on for a real cloud target. The operator's run confirms they hold — so the isomorphism's data-level
canary is realizable against the real estate (pending the real-wire throughput bench).

---

# Part VI — The scale program

## 1. The throughput math (settled)

Target: **hundreds of millions of rows** in **the cutover window** ⇒ **the throughput floor** sustained, worst-case all-FK-referenced
shape (`AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md:170-200`). Three measured points:

| Lane | rows/sec | Full-estate extrapolation | Status |
|---|---|---|---|
| Per-row `INSERT…OUTPUT` (baseline) | ~271 | **days** | F2 — **superseded** as the shipped path; survives as the per-row bottleneck *reference* P7b benchmarks against |
| 1000-row `VALUES` MERGE | ~5,700 | ~½ day | **Refuted** — parse-bound at the TVC 1000-row cap |
| **Bulk-staged `MERGE…OUTPUT`, 50k chunks** | **~27,000** | **hours** | **Shipped lane** — clears the window, but **loopback-only** |

**The "271 contradiction," resolved.** The audit body states ~271 as a LIVE finding; the same-day
evening addendum titles itself "F2 superseded by the set-based lane" (`:184`), echoed in `DECISIONS.md:21336-21351`.
As an append-only ledger, the addendum wins: ~271 is **superseded as the shipped capture path** but
**live as the per-row bottleneck reference** the J5 P7b measurement compares against. A real-wire bench
materially below 20k re-opens chunk-sizing and parallel per-table loading.

## 2. The set-based capture lane

Bulk-copy each kind's rows in **50k-row chunks** into a `SELECT TOP 0 … INTO` **session staging table
cloned from the sink** (a `#` temp table — *not* a column on the business table, so the no-schema-change
rule holds), then one `MERGE INTO target USING staging ON 1=0 WHEN NOT MATCHED … OUTPUT S.[__SRC_KEY],
INSERTED.<pk>` per chunk to capture source→assigned pairs. (`SurrogateCapture.fs:121-172`)

## 3. The capability-descent ladder

`StagedMergeOutput → StagedMergeOutputInto → RowwiseScopeIdentity`, descending **only** on SQL error
**334** (OUTPUT-without-INTO on a triggered target); sticky per kind; every descent named on the report.
(`SurrogateCapture.fs:21-46,275-314`) This is how trigger-bearing OSUSR tables (the P5 survey) are handled
automatically.

## 4. The remap and its ceiling

`PackedSurrogateRemap` — a mutable `Dictionary<int64,int64>` per kind (~40 B/entry), explicitly chosen
over the string-keyed immutable `Map` because the hundreds-of-millions-of-rows estate's FK-target tables "would not fit"
(`TransferRun.fs:330-337`; `PackedSurrogateRemap.fs:15-43`). Resident ceiling ≈ **~40 B per FK-target row (the
worst-case majority-FK-referenced shape)**. Above that, the **sink-resident keymap / server-side `UPDATE…JOIN` spill** is the named next step
— **DESIGNED-only** (`AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md:247-249`), gated on the estate survey.

## 5. Streaming, the journal, and the three batch knobs

- **Streaming ingestion** (`Transfer.runStreamingWithRenames` / `writePlanStreaming`,
  `TransferRun.fs:1205+`): per-kind `AsyncStream<RowQuantum>` pulled in 50k chunks; only the packed remap
  + the in-flight chunk are resident; a one-chunk source **prefetch** overlaps read(N+1) with write(N) on
  the two connections (intra-kind, not inter-kind).
- **Chunk resume** (`CaptureJournal`): client-side append-only NDJSON; `(kind,chunkIx)` last-write-wins;
  fingerprint = `(firstPk,lastPk,rawCount)`; on resume a hit is fingerprint-verified and **replayed into
  the remap without touching the sink**; a mismatch is the named `transfer.resume.sourceDrift` refusal.
- **Three distinct batch constants** (frequently conflated): the materialized bulk lane uses
  `BatchSize <- rows.Length` (whole-kind, `Bulk.fs:112`); the seed/stream lane uses
  `DefaultBulkBatchSize = 5000` (`Deploy.fs:545`, the swept knob — **vindicated** at 31.3k/31.4k rows/sec
  for 5k/10k vs 24.6k for 1k, `PERF_HARNESS.md:555`); the capture ladder uses `CaptureChunkSize = 50_000`
  (`TransferRun.fs:271`).
- **A named infeasibility**: the 20,000 *batch-size* scenario priced out — a 539 MB bulk-insert memory
  grant exceeds a 4 GiB container's ~492 MB big-query semaphore and stalls indefinitely on
  `RESOURCE_SEMAPHORE`. The grant is batch-size-*independent* (a SORT estimate over the indexed target);
  diagnose via `sys.dm_exec_query_memory_grants` + `…_resource_semaphores` before blaming the knob.

## 6. The witness suites

`ReverseLegCanaryTests.fs` (keystone + DML-only principal + DENY/drift pins) · `ReverseLegPropertyTests.fs`
(eight pure laws — order soundness, disposition totality, remap algebra, refusal totality, rendition
invariance) · `ReverseLegScaleTests.fs` (the capture envelope + the **CDC isometry norm**:
`‖δ‖ = capture count = row count`, exactly) · `ReverseLegBoundaryTests.fs` (CLI reconcile/user-map refusal
live + four reserved Skip-stub contracts carrying their promotion triggers) · `ReverseLegStreamingTests.fs`
(the idempotence + crash-resume witnesses).

---

# Part VII — Current code state: the execution surface

There are **two distinct movement paths**; do not conflate them. (1) The **Transfer engine**
(`Projection.Pipeline.Transfer`, `TransferRun.fs`) — the live DB→DB row mover that this charter targets.
(2) The **hydration → SQL-text emission** lane (`Hydration.fs` + `DataEmissionComposer`) — streams live
static/bootstrap rows and renders `Data/*.sql` MERGE files into a bundle (a *publication artifact*, not a
live write). The goal at scale is path (1).

## 1. Movement engine — **BUILT**

Two-phase, topologically ordered (`writePlan` `:315`; `writePlanStreaming` `:1205`). Phase 1 inserts each
kind's rows with cycle-breaking deferred FK columns NULLed, in FK-safe topo order; Phase 2 UPDATEs the
deferred FK columns once targets exist. Lane dispatch by `IdentityDisposition` (`:378-401`); realization
auto-selected by `ReverseLegRealization.choose` (`:64-90`) — **streaming** when admissible (no table
subset, not G10-resumable, Incremental emission; dominates throughput ~35.5k vs ~27k and is journal-
resumable) else **materialized** (carries the table-subset / G10 / WipeAndLoad combos streaming doesn't
support). An explicit `--streaming` on an inadmissible combination **refuses by name**.

Wired to the production CLI (`RunFaces.runReverseLegTransfer` → `runReverseLegThroughConnections` /
`runStreamingReverseLegThroughConnections` / `runThroughConnectionsResumable`), gated by `--go` +
`PROJECTION_ALLOW_EXECUTE=1`. Pre-write gates BUILT: CDC, connection liveness, grant/permission (advisory),
unbreakable-cycle, composite-surrogate, unmapped-orphan (materialized arm).

- **GAP:** no real-wire run (largest exercised: 4 kinds × 25,000 = 100,000 rows, plus a 16-kind × 250
  mesh; tests print a full-estate extrapolation but no full-estate run). Reverse leg loads kinds **sequentially** — the
  level-parallel loader exists on the **seed lane only** (`Deploy.executeLeveledSeed`, `executeBatchParallel`,
  `ParallelSafe` token minted by `TopologicalOrder.levels`, `resolveParallelism` env→DMV→4 stack), measured
  2.59–2.85×. Bringing it to the reverse leg is a **PORT** (reuse, gated by the cross-kind remap dependency),
  not a green-field build. P4 "transfer wavefronts" are **ARMED**, wake = real-wire bench below the throughput floor.

## 2. Resume / checkpoint / idempotency — **PARTIAL**

The ledger contract (`Ledger.fs:64-120`: `LedgerSpec`, `writeAdmit`/`resumeAdmit`/`replay`/`resumePoint`,
typed `LedgerDrift`) and the `CaptureJournal` instance over it are **BUILT** (R1/L1–L4 cards DONE 2026-06-12).
Crash-resume at chunk granularity is witnessed (`ReverseLegStreamingTests.fs:87-147`).

- **Idempotent re-migration already exists — conditionally.** A completed journaled streaming run re-runs
  as a **full SKIP** with zero duplicates (`TransferRun.fs:1253-1270`; witnessed `…StreamingTests.fs:149-174`),
  closing the **G3** duplicate hazard *whenever a journal is supplied*. **CLOSED (Phase 3, 2026-06-15):**
  `--streaming --execute` **without** `--journal` is now **refused by name**
  (`transfer.reverseLeg.streamingExecuteRequiresJournal`, the pure `ReverseLegRealization.executeJournalGate`) —
  so every streaming execute carries a journal and is idempotent by construction; the duplicate hazard is closed
  by construction, not merely "whenever a journal is supplied." Caveat: idempotence is keyed to the same plan-marker
  **and** a byte-identical source slice (changed source ⇒ drift refusal). The journal is client-side NDJSON
  *because the DML-only grant forbids the `CREATE TABLE` a sink-resident progress table would need*.
- **ARMED:** journal compaction — resume loads the **entire NDJSON** (tens of bytes per captured pair, GBs at
  full-estate scale) into memory (`CaptureJournal.fs:66-74`); wake = any real resume > ~10M pairs. Envelope spill (`RunState.Envelopes`
  accumulates in memory) — ARMED, same scale wake.
- **GAP (STAGED):** the built resume is *journal-replay on re-run*, **not live socket-drop reconnect/retry**
  mid-transfer (Phase-1 real-wire co-requisite). **Risk (mitigated, Phase 3):** the journal filename **is** its
  content digest (`transfer-<digest16>.ndjson`, `CaptureJournal.fs`) — a byte change still orphans the prior
  journal, but the orphaning is no longer *silent*: `CaptureJournal.siblingJournalsUnderDrift` + the
  `transfer.resume.journalAddressDrift` refusal halt a would-be fresh-run-over-orphaned-journal by name.

## 3. Operator progress — **PARTIAL**

Per-kind stage progress (`LogSink.recordStageProgress "load"`, `:1370`), a `--watch` board (`Watch.renderWatch
Spines.transfer`, only on real `--execute`), and the live-push primitive `LogSink.addSubscriber` (shipped
2026-06-05) are **BUILT**. The live progress-bar/ETA leg is **DESIGNED but PARKED** at Tier-3
(`SpectreProgressAdapter : IProgressRunner over Bench.snapshot()`, `TtyRenderer.fs`, the three-mode
Glance/Watch/Explore design in `DYNAMIC_DISPLAY.md`; `REPORTING_HORIZON.md:204-206`).

- **GAP:** every progress denominator is **stage-grained**. There is no row-grained / rows-per-sec / ETA /
  durable intra-run progress surface for a single multi-hour ~100M-row table — UNADDRESSED in both
  `REPORTING_HORIZON.md` and `HORIZON.md` (a full grep of HORIZON for resume/checkpoint/restart returns
  zero). An operator reconnecting after a drop cannot today be shown "X of 100M done" from any durable source.

## 4. Dry-run & gating — **PARTIAL**

Preview-by-default + explicit commit is BUILT and totality-tested: `projection <flow>` previews,
`projection <flow> --go` commits. Two-gate live-write safety: `--go` (intent → `MovementSpec.Commit`) **and**
`PROJECTION_ALLOW_EXECUTE=1` (authorization, R6) — every live runner re-checks the env var and refuses
`gate.intent` exit 7 if absent. Rich **schema** dry-run (`explain <flow>` → snapshot⊖snapshot DDL diff:
tables/columns add·drop·rename, the change-norm `‖δ‖`, statement/rename counts). **Data** dry-run on a
`scope:data` flow (`Transfer.DryRun`): surfaces per-kind disposition, `RowsIngested`, `UnmatchedIdentities`,
`SkippedReferences` (FK orphans), unbreakable cycles, capture-lane descents. Declared-loss gates
(`--allow-drops`, `--allow-cdc`); grant-refusal at plan time for schema-into-`grant:data` (exit 9).

- **GAP:** the combined `migrate-with-data` path (the headline reverse leg with rekeying) has **no DryRun
  arm** (`RunFaces.fs:1833`, always `Transfer.Execute`). `RowsWritten` is forced to 0 in DryRun (`:790`) and
  the streaming DryRun ingests nothing — so **no row-count estimate**, **no DML/row preview**, **no
  rekey-map preview**, **no resume-state preview** ("which chunks would skip vs re-move"). The grant-refusal
  "gate" on the live move is **advisory-only** today (R6 dual-track: the survey warns then proceeds); it flips
  to a hard per-pair stop at cutover.

## 5. The Migration lane vs the migrate engine (the crucial distinction)

Two different things are named "Migration":

- **The data-emission Migration lane** (`MigrationDependenciesEmitter` + `MigrationDependenciesBinding`,
  PR #611) — one of three sibling data-publication lanes the `DataEmissionComposer` dispatches over a closed
  `DataComposition` DU (`AllRemaining | AllExceptStatic | AllData`, `Policy.fs:39-48`). Its row source is an
  **operator-curated JSON file** (`overrides.migrationDependencies.path`): logical `Module.Entity` rows with
  stable ids + logical-column→raw-value cells, resolved to SsKey against the catalog (rename-invariant) with
  a synthesized per-row Identifier. Fail-loud on a malformed/unresolved file. Emits per-kind MERGE/UPDATE
  (`Data/MigrationData.sql`) via the same algebra as `StaticSeedsEmitter`, IDENTITY_INSERT-bracketed for
  AssignedBySink kinds, with the optional `ValidateBeforeApply` drift guard (NM-73). A `unionSiblings`
  partition law keeps the three lanes provably disjoint. **The lane is a publish/emit surface, not a true
  idempotent data re-run.**
- **The schema-side `migrate A B` engine** (`MigrationRun.fs`) — `migrate A B = emit(B⊖A)`; an unchanged
  schema yields an empty differential, zero DDL, and the engine is **idempotent + resumable by construction**
  (re-running re-diffs the now-current state; `:407-411`). It has a real dry-run/preview
  (`previewFromStore` reading the prior emission schema from the durable `LifecycleStore`), live progress,
  and a verify-after-apply round-trip (`B' = B`).

**The gap between them is the charter's "idempotent re-migration" goal.** Nothing on the **data** plane keys
off "schema identical" to skip data work: the Migration lane re-MERGEs its whole curated set every run, and
under AssignedBySink a re-run into a populated target **duplicates** rows (no business-key upsert). The
schema-identity skip is schema-only — and additionally unsound for non-name facet changes (`CatalogDiff.between`
compares by name only; a changed trigger/CHECK/modality yields `‖δ‖ = 0` while the canary `PhysicalSchema.diff`
*does* see it — `AUDIT_2026_06_13:89`, sev High).

## 6. Two-phase rekey / users — **BUILT (Phase 2 landed 2026-06-15)**

Entity-row rekey is BUILT (capture ladder + `PackedSurrogateRemap`; Part VI). **User rekey on the reverse leg
is now BUILT** (`DECISIONS.md` 2026-06-15 — "Phase 2: reconcile ∘ streaming on the reverse leg"). The blanket
`transfer.reverseLeg.reconcileUnsupported` refusal is **LIFTED**: the CLI face parses + resolves reconcile /
user-map specs against the physical sink contract (a bad spec still refuses by name, exit 2, before any
connection). The streaming runner (`runStreamingReconcilingWithRenames`) reconciles the named kinds (the User
family by email) against the sink BEFORE the stream and re-keys every FK targeting them through a combined
packed-∪-reconcile lookup, never re-importing a sink-owned row (`reclassifyReconciled` → `ReconciledByRule`,
phase-1/2 skipped). The **`validate-user-map` pre-write halt now runs on the streaming arm** (NM-31 / N4 closed):
an unmapped source user refuses `transfer.unmappedIdentities` with the sink untouched, unless `--allow-drops`
downgrades it. The materialized arm reconciles through `runCore` too, so an inadmissible-combination request
never silently drops the reconcile. Witnessed by `ReverseLegStreamingTests` (the re-key-never-re-imported pair
+ the pre-write halt).

**The residual (still open):** the reconcile here is the **runtime** path — `reconcileAgainstSink` reads the
sink's user inventory live and matches by the operator ruleset. The design-time `UserFkReflowPass.discover`
(all four strategies; `UserFkReflowPass.fs:195-348`) remains a **production no-op** — `IsUserFk` is always
false and the discovered map is discarded at emit (a CHAPTER-4.2 deferral). Populating that live
user-population *discovery* reader is the remaining Phase-2 thread; the runtime reconcile path the charter's
exit test names is built and witnessed.

---

# Part VIII — Program context

## 1. The J5 preemption rule

A binding sequencing law in two places: *"if the operator arrives with a writable UAT connection, drop
everything"* (`CONSTELLATION_BACKLOG.md` §5) and binding principle #8 *"J5 preemption — a writable UAT
connection preempts all of this"* (`V1_FULL_EXPORT_RECONCILIATION_PLAN.md:434`). The whole queued
Constellation/Lapidary card program is pre-sized so **no card strands more than a day's work** — the
canary-never-red-at-a-commit-boundary rule *is also the resume story* after an interruption. **The operator
arriving with J5 results discharges this preemption** — the program can now resume with J5 settled.

## 2. R6 and the cutover ladder

R6: V2 emits-but-doesn't-ship during dual-track — it owns no production write path until the per-pair flip at
cutover. This is why the grant-refusal gate is advisory today (Part VII §4) and why `PROJECTION_ALLOW_EXECUTE`
exists as a separate authorization axis from `--go` intent. The full production-shaped dry-run is a
deferred cutover gate (`CUTOVER_READINESS_BRIEF.md`).

## 3. The DataVerification / EXCEPT-fallback thread (NM-73)

`Policy.fs:101-109` defines `type DataVerification = Standard | ValidateBeforeApply` (a closed DU). The
operator decision C2: *"CDC-silence stays canonical; EXCEPT validate-before-apply is the conservative
fallback/override until J5 proves the CDC path on a managed OutSystems environment… revisit auto-fallback after J5."* NM-73 (WP6.6)
**built and shipped the manual opt-in override** (default `Standard`, byte-identical to pre-NM-73), but the
**auto-fallback** (CDC-failure → automatic EXCEPT) is explicitly deferred until after J5. So J5's CDC verdict
(OPEN-3) is the thing that arms the automation — Phase 5.

## 4. The named-gap consolidation (G-/N-/NM- index)

- **G1** — object-scope DENY escaping a database-scope grant check (a per-table DENY under a DB grant). Live
  in the real estate; flag prominently (changes preflight refinement priority). *Residual probe (P1b).*
- **G3** — streaming duplicate hazard. **Closed** whenever a `--journal` is supplied.
- **G10** — the resumable/idempotent marker envelope (materialized arm). Built; not exercised on the reverse leg.
- **N4 / AC-I5** — the pre-write `validate-user-map` gate. Present on the materialized arm; **now also on
  streaming** (Phase 2, 2026-06-15 — was NM-31).
- **NM-31** — streaming pre-write orphan halt. **CLOSED** (Phase 2): `runStreamingReconcilingWithRenames` has
  the reconcile leg + the `validateUserMap` halt; the realizations are no longer drop-asymmetric.
- **NM-73** — EXCEPT validate-before-apply DataVerification override. Manual override shipped; auto-fallback
  J5-gated.

---

# Part IX — The charter

Dependency-ordered. Each phase has an exit test.

### Step 0 — Relocate the ledger to the canonical stores *(discharges the preemption)*
- **Deprecate `J5_MANAGED_ENV_CAPABILITY_PLAYBOOK.md`** rather than populate its §7 template — the spike has
  run, so its intent is fulfilled; banner added, content relocated. *(done 2026-06-15)*
- **Write the canonical `DECISIONS.md` entry** closing OPEN-1/2/3/5/6/7, with the Part II §7 residuals
  named. *(done 2026-06-15 — "J5 managed-environment capability spike RUN…")*
- **Remaining:** flip the status surfaces (`AUDIT_2026_06_13` "J5 (ops-gated)"; the `HANDOFF.md` /
  `CONSTELLATION_BACKLOG.md` preemption letters) and arm the NM-73 auto-fallback once the CDC verdict
  (OPEN-3) lands.
- **Exit:** the canonical stores (DECISIONS + this compendium) carry the ledger; the playbook is deprecated.

### Phase 1 — Close the real-wire loop *(mostly measurement)*
- Run the set-based streaming lane against a real managed OutSystems environment at representative scale → measure rows/sec (**P7b**).
  **Exit:** the throughput floor sustained, or the escape hatches (parallel wavefronts P4, 50k-chunk sweep, sink-resident
  spill) are triggered with a plan.
- **Estate survey**: row-count + FK-fan-in (gates resident-map vs sink-resident spill) + the P5 trigger map
  across OSUSR tables + the **G1** object-scope-DENY check (P1b).

### Phase 2 — Wire user reconciliation onto the reverse leg *(the "rekey users" gap)* — **DONE 2026-06-15**
- ✅ Built **reconcile∘streaming**: composed `Reconciliation.reconcileKind` (ByEmail/…) + `reconcileAgainstSink`
  + `SurrogateRemapContext` onto the streaming path (`runStreamingReconcilingWithRenames`); **lifted
  `reconcileUnsupported`** at the CLI face. The materialized arm reconciles through `runCore` too (no silent
  loss on inadmissible combos).
- ✅ Added the **`validate-user-map` pre-write gate** on the streaming arm (closed N4 / NM-31): halts before any
  DML on an unmapped orphan (`transfer.unmappedIdentities`), `--allow-drops` downgrades.
- ⏳ **Residual:** the live user-population *discovery* pass (`UserFkReflowPass.discover`) is still a production
  no-op; the runtime reconcile-against-sink path is built. Populating the discovery reader is the carry-over.
- ✅ **Exit met:** a reverse-leg move re-keys users by email with a pre-write orphan halt, witnessed
  (`ReverseLegStreamingTests` — re-key-never-re-imported + the pre-write halt; `DECISIONS.md` 2026-06-15).

### Phase 3 — Harden resume + idempotency for scale — **buildable levers DONE 2026-06-15; two STAGED**
- ✅ **Forced a journal** on `--streaming --execute` (closed the duplicate hazard — the small lever):
  `transfer.reverseLeg.streamingExecuteRequiresJournal` (the pure `ReverseLegRealization.executeJournalGate`).
  A journal-less streaming execute now refuses by name; every streaming execute is idempotent by construction.
- ✅ **Address-drift guard** for the filename-coupling risk: `transfer.resume.journalAddressDrift`
  (`CaptureJournal.siblingJournalsUnderDrift`) — a marker/schema byte-change that orphans the prior journal now
  refuses by name instead of silently re-streaming. (This converts the silence into a refusal; it does not
  re-address the journal — multi-transfer-per-dir coexistence is preserved.)
- ⏳ **STAGED — journal compaction** (stop the full-NDJSON load at resume): ARMED, wake = any real resume
  > ~10M captured pairs. Not built — Docker scale is KB; a speculative scale-build has no witness.
- ⏳ **STAGED — live socket-drop reconnect/retry** mid-transfer (distinct from re-run replay): needs a real
  dropped connection to prove — a Phase-1 real-wire co-requisite, not Docker-witnessable.
- **Exit:** the duplicate hazard is closed by construction (every streaming execute carries a journal);
  the killed-connection-resumes-no-duplicates witness already holds at chunk granularity
  (`ReverseLegStreamingTests` "chunk resume"). The scale-representative journal size + live socket-drop are the
  staged real-wire residuals.

### Phase 4 — Movement dry-run + row-grained progress
- **Movement dry-run**: row-count estimate, rekey-map preview, resume-state preview, for `migrate-with-data`.
- **Row-grained progress/ETA**: feed the parked `SpectreProgressAdapter` a rows-written/rows-remaining
  denominator + a durable surface surviving a reconnect.
- **Exit:** an operator can preview "N rows / M chunks would move, K would skip" and watch it live.

### Phase 5 — Cutover-readiness gates
- ≥1 **full production-shaped dry-run** (the deferred cutover gate).
- Flip the grant-refusal gate **advisory → hard pre-write stop** (per-pair at cutover, R6).
- Arm the **NM-73 auto-fallback** (CDC-silence → EXCEPT) now that J5 settles the CDC path (OPEN-3).

## Decisions needed from the operator

1. **Scale shape** — is the hundreds-of-millions-of-rows figure the whole estate or the largest table? How many rows live in **FK-target**
   tables (decides resident remap vs sink-resident spill), and how many **users** (the reconcile set)?
2. **Transfer-host memory budget** — sets the packed-remap spill trigger.
3. **The two un-run probes** — hand the operator the **P10** user-directory metadata probe now (read-only;
   confirms ReconciledByRule available) and design the **P7b** throughput harness for Phase 1.

## Risk register (carried from the discovery)

- **No live-wire validation** — every throughput/resume/marker behavior is loopback-Docker only; the single
  largest unmitigated unknown.
- **Journal-filename ↔ content-digest coupling** — a byte change silently orphans journals (defeats resume).
- **Warm-container memory-grant stall** — the batch-size-independent `RESOURCE_SEMAPHORE` wait; diagnose via
  DMVs, not the batch knob.
- **Resident remap ceiling** (~40 B per FK-target row, the worst-case majority-FK-referenced shape) — bounds host RAM, not correctness; spill is
  designed-only.
- **Schema-identity skip is schema-only and name-only-unsound** — no data-plane skip; facet-only changes read
  as `‖δ‖ = 0`.

---

# Part X — Appendices

## A. Code-citation index (where things live)

| Concern | File · symbol |
|---|---|
| Reverse-leg engine | `TransferRun.fs` — `writePlan:315`, `writePlanStreaming:1205`, `ReverseLegRealization.choose:64-90`, `CaptureChunkSize=50_000:271`, `insertCaptureRow:101-130`, `reconcileAgainstSink:208-234` |
| Set-based capture + ladder | `SurrogateCapture.fs` — ladder `:21-46`, `mergeOutputChunk:158-172`, 334 `:275-277`, `captureChunkDescending:282-314` |
| Remap | `PackedSurrogateRemap.fs:15-43`; classifier `SurrogateRemap.fs:65-83` |
| Resume journal + ledger | `CaptureJournal.fs` (load `:66-74`, digest filename `:60`); `Ledger.fs:64-120` |
| Bulk lanes | `Bulk.fs` — `copyCore/BatchSize:112`, `copyRows:121`, `copyRowsSinkMinted:129` |
| Seed parallelism + batch | `Deploy.fs` — `executeLeveledSeed:524`, `executeBatchParallel:468`, `resolveParallelism:390`, `DefaultBulkBatchSize=5000:545`; `TopologicalOrder.fs` — `ParallelSafe:153`, `levels:294-324` |
| Policy DUs | `Policy.fs` — `DataVerification:101-109`, `DataComposition:39-48` |
| Migration lane | `MigrationDependenciesBinding.fs:15-243`, `MigrationDependenciesEmitter.fs:191-406`; `DataEmissionComposer.fs:98-181` |
| migrate engine | `MigrationRun.fs` — re-diff no-op `:407-411`, preview `:122-164`, `executeWithData:673-706` |
| CLI / gating | `RunFaces.fs` — gating `:672-679,808-815`, reconcile refusal `:787-792`, watch `:741-747`; `MovementSurface.fs` — `planMovement:635,678`, journal/streaming parse `:1170-1171`; `Program.fs:160` |
| User rekey | `UserFkReflowPass.fs:195-348`, `UserRemap.fs:81-200`, `ComposeState.fs:24,88-89`; user dir `ReadSide.fs:1600,1632` |
| Progress | `REPORTING_HORIZON.md:204-206` (`SpectreProgressAdapter`), `DYNAMIC_DISPLAY.md` |
| Witnesses | `ReverseLeg{Canary,Property,Scale,Boundary,Streaming}Tests.fs` |

## B. Glossary

- **AssignedBySink / PreservedFromSource / ReconciledByRule** — the three `IdentityDisposition`s (Part IV §2).
- **SsKey** — the invariant stable-identity carrier; survives renames (Part IV §1).
- **OSUSR_*** — OutSystems' physical entity-table naming convention (Realization A, cloud-physical).
- **CDC** — change-data-capture; CDC-silence is the canonical no-overwrite redeploy posture.
- **Rung A–F** — the J5 risk ladder (Part II §4).
- **Contribution A / B** — survey instrument / set-based MERGE-OUTPUT capture (Part IV §4) — *not* the torsor A/B.
- **G1 / G3 / G10** — object-scope-DENY gap / streaming duplicate hazard / resumable marker envelope (Part VIII §4).
- **N4 / NM-31 / NM-73** — validate-user-map gate / streaming pre-write halt / EXCEPT DataVerification override.
- **R6** — the dual-track posture: V2 emits but does not ship until the per-pair cutover flip.
- **H-050** — the emitter/reader adjunction axiom `Ingestion ∘ Projection = id` (Part V §1).
- **The Voice** — the operator-communication surface (logging format, live display, `--watch`).
- **M5** — Preflight Cloud Insertion (`PREFLIGHT_CLOUD_INSERTION.md`), gated by the J5 ledger.
- **‖δ‖** — the change-norm; `‖δ‖ = 0` means an empty differential (nothing to apply).

## C. Provenance

Synthesized from two background discovery workflows (2026-06-15): the J5-foundation sweep (12 round-1 + a
completeness critic + 6 fill agents) and the execution-maturity sweep (7 + critic + 6 fill), ~5M subagent
tokens, every claim citation-backed and the inter-agent contradictions (the ~271 figure; streaming
idempotence; batch/parallelism build-status; the CLI-wiring "still open" staleness) explicitly reconciled in
the fill round. The J5 ledger (Part II §6) is the sanitized findings of the run on a managed OutSystems
environment, recorded canonically here and in `DECISIONS.md`; it is not otherwise in the repo. Durable facts also recorded in the agent's session memory (`j5-cloud-uat-ledger`,
`reverse-leg-execution-readiness`).
