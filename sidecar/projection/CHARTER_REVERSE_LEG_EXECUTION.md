# Charter — Reverse-Leg DML Movement to Execution-Ready

> **What this is.** The forward charter for taking reverse-leg DML movement of legacy
> application data — the B→A load that re-keys an OutSystems estate into a real Cloud
> UAT (and ultimately production) target — from "engine proven on mock" to "operator
> trusts running it against ~288M real rows." It is grounded in two deep discovery
> sweeps (2026-06-15) over the J5 outcomes, the Use-Case Ontology, and the current code
> state, plus the operator's real-UAT J5 run. Citations are `file:line` against HEAD.
>
> **Dated 2026-06-15.** Supersedes nothing; sequences the work the J5 preemption rule
> (`CONSTELLATION_BACKLOG.md` §5, `V1_FULL_EXPORT_RECONCILIATION_PLAN.md` #8) was
> waiting on.

---

## 0. The reframe (read first)

Two facts set the shape of everything below:

1. **The gate is closed.** In the repo, J5 (the Cloud UAT capability spike) was the one
   true critical-path item — "ops-gated, STILL-OPEN" (`AUDIT_2026_06_13_INVARIANT_NEAR_MISS_HUNT.md:315`),
   its §7 ledger a blank template, with **no DECISIONS entry closing OPEN-1/2/3/5/6/7**
   (OPEN-2 appears once, `DECISIONS.md:21138`, as a residual). The capability questions
   were already de-risked to a re-run — P1/P2/P3/P6 answered on mock, four green
   `ReverseLeg*Tests.fs` suites (`HANDOFF.md:1581-1586`). **The operator has now run J5
   against real UAT** and produced the verdicts (§1), answering the residual the whole
   program queued behind.

2. **The engine is largely already built.** This is an integration-and-validation
   effort, not a green-field build. The two-phase transfer engine, streaming, chunk
   resume, the set-based capture lane, the packed remap, dry-run scaffolding, and
   within-level parallelism (on the seed lane) all exist and are witnessed — but only at
   Docker scale (~100k rows), never on a real wire, and with specific *named* gaps.

The charter is therefore mostly about wiring what exists into a trustworthy whole and
running the one experiment everything is loopback-pending on.

---

## 1. The J5 ledger (operator's real-UAT run, 2026-06-15)

Recorded here in the playbook's transportable form (verdicts + standard error numbers +
roles). This is the source of truth until it is folded into `J5_UAT_CAPABILITY_PLAYBOOK.md`
§7 and a closing `DECISIONS.md` entry (Step 0).

| Probe | Verdict | Resolves |
|---|---|---|
| P1 write envelope | **permitted**: SELECT/INSERT/UPDATE/DELETE; **no ALTER** grant | OPEN-2 |
| P2 identity-omitting INSERT + key readback | **permitted** — DB mints the key | OPEN-1 → **AssignedBySink** |
| P3 `SET IDENTITY_INSERT` | **denied (1088)** | OPEN-1 → **PreservedFromSource dead** |
| P6 DELETE of own row | **permitted** — baseline restored | OPEN-6 → **rollback channel = SQL** |
| P11 transaction roundtrip | **permitted** — ROLLBACK clean | OPEN-3 / OPEN-5 |
| P5 table-var / temp-table | **permitted** | key-map render target |
| P4 `MERGE…OUTPUT INTO` | **permitted** — source→assigned pairs returned | Contribution-B trigger (a) |
| P7a multi-row batch (5) | **permitted** — captured + deleted clean | OPEN-5 |
| P8 ALTER NOCHECK / P6-TRUNCATE | **denied — inferred from absent ALTER** | OPEN-6 |

**One root, three denials.** No ALTER grant settles P3 (IDENTITY_INSERT/1088), P8 (NOCHECK),
and P6-TRUNCATE together — exactly the §2-rule-6 inference.

**Operator constraint.** No schema change and no artificial business-marker values on real
tables; cleanup is by **captured key**, which is also the FK-remap handle. The no-marker path
and AssignedBySink are the same mechanism.

**Still open after the run (the genuine residuals):**
- **P7b** — throughput over the real wire (the ≥20k rows/sec / ≤4h confirmation). *Load-bearing.*
- **P10 / OPEN-7** — user-directory readable + email-keyed (confirms the ReconciledByRule user path).
- **G1** — object-scope DENY check (P1b), in case a per-table DENY diverges from the database grant.
- **P5 estate survey** — which OSUSR tables carry platform triggers (the capture ladder handles them; we want the map).
- **OPEN-3 / CDC** — gates whether the NM-73 EXCEPT auto-fallback (`Policy.fs` `DataVerification`) can be armed.

---

## 2. Readiness map — six maturity dimensions, graded against the code

Vocabulary: **BUILT** / **ARMED** (designed, named wake-condition) / **GAP** (unbuilt).

### 2.1 Movement engine — **BUILT**
Two-phase, topologically ordered (`TransferRun.fs:315` `writePlan`, `:1205` `writePlanStreaming`;
topo via `TopologicalOrderPass`). Phase 1 inserts with deferred FK columns NULLed; Phase 2
UPDATEs them once targets exist. Lanes by `IdentityDisposition`: non-FK `AssignedBySink` →
`Bulk.copyRowsSinkMinted` (sink mints); FK-targeted `AssignedBySink` → the **set-based capture
ladder** (`SurrogateCapture.captureChunkDescending`: bulk-copy a 50k chunk into a `#` session
table, then `MERGE … OUTPUT S.[__SRC_KEY], INSERTED.<pk>` per chunk); `PreservedFromSource`/
`ReconciledByRule` → `Bulk.copyRows` (KeepIdentity). Capture ladder **descends on SQL 334**
(OUTPUT-with-trigger): `StagedMergeOutput → StagedMergeOutputInto → RowwiseScopeIdentity`
(`SurrogateCapture.fs:21-46`). Realizations auto-selected by `ReverseLegRealization.choose`
(`TransferRun.fs:64-90`); wired to CLI, gated by `--go` + `PROJECTION_ALLOW_EXECUTE=1`.
Chunk knob `CaptureChunkSize = 50_000` (`TransferRun.fs:271`).
- **GAP:** no real-wire run (largest exercised ~100k rows, Docker). Reverse leg loads kinds
  *sequentially*; the level-parallel loader exists on the **seed** lane only
  (`Deploy.executeLeveledSeed`, `ParallelSafe` token) — a **port**, not a build, gated by the
  cross-kind remap dependency.

### 2.2 Resume from broken connection — **PARTIAL**
`CaptureJournal` chunk-resume (client-side NDJSON; fingerprint-guarded skip; named
`transfer.resume.sourceDrift` refusal; `CaptureJournal.fs`). A completed journaled streaming run
re-runs as a **full skip**.
- **GAP:** this is *journal-replay on re-run*, not live socket-drop reconnect/retry mid-transfer.
- **ARMED:** journal compaction — resume loads the whole ~9–10 GB NDJSON into memory at 288M
  (`CaptureJournal.fs:66-74`); wake = any real resume > ~10M pairs (`CONSTELLATION_BACKLOG.md` §6 item 5).
- **Risk:** journal filename **is** the content digest (`CaptureJournal.fs:60`) — a byte change
  orphans every existing journal (§7 risk register).

### 2.3 Progress reporting — **PARTIAL**
Per-kind stage progress (`LogSink.recordStageProgress "load"`, `TransferRun.fs:1370`), `--watch`
board, and the live-push primitive `LogSink.addSubscriber` (shipped 2026-06-05) are BUILT. The
live progress-bar/ETA leg is **DESIGNED but PARKED** at Tier-3 (`SpectreProgressAdapter :
IProgressRunner`, `REPORTING_HORIZON.md:204-206`).
- **GAP:** row-grained rows/sec + ETA + a **durable** intra-run progress surface that survives a
  reconnect — the decisive gap for a multi-hour ~100M-row table — is unaddressed in both
  `REPORTING_HORIZON.md` and `HORIZON.md` (every progress denominator is stage-grained).

### 2.4 Dry-run — **PARTIAL**
Preview-by-default + two-gate commit (`--go` intent + `PROJECTION_ALLOW_EXECUTE` authorization)
+ named refusals; rich **schema** dry-run (`explain <flow>` → snapshot⊖snapshot DDL diff);
**data** dry-run on a `scope:data` flow shows the load plan, dispositions, orphan/cycle hazards.
- **GAP:** the combined `migrate-with-data` path (the headline reverse leg) has **no** data
  dry-run (`RunFaces.fs:1833`, always executes); no row-count estimate (RowsWritten forced 0);
  no DML or **rekey-map** preview; no resume-state preview.

### 2.5 Idempotent re-migration — **NUANCED**
Schema-side `migrate A B` is idempotent + resumable by construction (empty B⊖A = no-op,
`MigrationRun.fs:407-411`). Streaming **data**: a completed journaled run re-runs as a **FULL
SKIP — idempotency already exists**, *conditional on a `--journal`*.
- **GAP (small lever):** `--streaming --execute` **without** `--journal` silently *doubles*
  AssignedBySink rows (`TransferRun.fs:89`, `MovementSurface.fs:1171`). The open work is
  **force/default a journal** (or refuse journal-less streaming-execute by name) — not building
  idempotency. Caveat: keyed to same plan-marker + byte-identical source ("schema identical"
  must also mean unchanged source rows or it refuses by drift).
- The Migration **lane** (operator-curated JSON, PR #611; `MigrationDependenciesBinding.fs`,
  `MigrationDependenciesEmitter.fs`) is a publish/emit surface: it re-MERGEs its whole curated
  set every run; no business-key upsert, no data-side schema-identical skip gate.

### 2.6 Two-phase rekey / users — **GAP (functional)**
Entity-row rekey is BUILT: `PackedSurrogateRemap` (`Dictionary<int64,int64>` per kind, ~40 B/entry,
~6 GB resident at ~150M FK-target rows; `PackedSurrogateRemap.fs`); FKs re-point with
skip-and-diagnose. Sink-resident keymap / server-side spill is **DESIGNED-only**
(`AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md:247-249`).
- **GAP (the big one):** **user rekey is REFUSED on the reverse leg** —
  `transfer.reverseLeg.reconcileUnsupported` (`RunFaces.fs:787-792`). Reconcile∘streaming is
  unbuilt; the `UserFkReflowPass` discovery pass is a no-op stub (no live user-population reader);
  the `validate-user-map` pre-write gate is absent on the streaming arm (NM-31, `TransferRun.fs:1483-1490`).
  Users are **ReconciledByRule** (match by email — `ByEmail`/`BySsKey`/`MatchByColumn`/
  `ManualOverride`/`FallbackToSystemUser`; `Reconciliation.reconcileKind`) in the ontology, but
  that path is **not wired onto the reverse leg**.

**The scale math is settled** (`AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md:184-200`): 288M rows,
≤4h ⇒ ≥20k rows/sec. Per-row ~271/s ⇒ ~12 days (dead); set-based bulk-staged MERGE ~27k/s
loopback ⇒ ~3.0h (clears it) — **pending the real-wire bench**. A bench <20k re-opens parallel
wavefronts + the 50k-chunk sweep.

---

## 3. The sequenced path to execution-ready

Dependency-ordered; each phase has an exit test.

### Step 0 — Land the ledger *(cheap; unblocks the program)*
- Populate `J5_UAT_CAPABILITY_PLAYBOOK.md` §7 from §1 (verdicts + error numbers).
- Write the `DECISIONS.md` entry closing OPEN-1/2/3/5/6/7, with the §1 residuals named.
- Flip status surfaces: `AUDIT_2026_06_13` "J5 (ops-gated)" → run; arm the NM-73 *auto-fallback*
  "revisit after J5" decision.
- **Exit:** the repo's status surfaces agree with reality; the J5 preemption is discharged.

### Phase 1 — Close the real-wire loop *(mostly measurement)*
- Run the set-based streaming lane against real UAT at representative scale → measure rows/sec
  (**P7b**). **Exit:** ≥20k/s sustained, or escape hatches triggered.
- Estate survey: row-count + FK-fan-in (gates resident-map vs sink-resident spill) + P5 trigger
  map across OSUSR tables + G1 object-scope-DENY check (P1b).

### Phase 2 — Wire user reconciliation onto the reverse leg *(the "rekey users" gap)*
- Build **reconcile∘streaming**: compose `Reconciliation.reconcileKind` + `UserRemapContext` onto
  the streaming path; lift `reconcileUnsupported`.
- Add the **validate-user-map pre-write gate** on the streaming arm (close N4/NM-31).
- Populate the discovery pass (live user-population reader; today a `Map.empty` stub).
- **Exit:** a reverse-leg move re-keys users by email with a pre-write orphan halt.

### Phase 3 — Harden resume + idempotency for scale
- **Force/default a journal** on `--streaming --execute` (close the duplicate hazard).
- **Journal compaction** (stop full-NDJSON load at resume) + decouple resume-address from digest.
- **Live connection resilience**: socket-drop reconnect/retry mid-transfer.
- **Exit:** a killed connection resumes from the last committed chunk with no duplicates.

### Phase 4 — Movement dry-run + row-grained progress
- **Movement dry-run**: row-count estimate, rekey-map preview, resume-state preview, for
  `migrate-with-data`.
- **Row-grained progress/ETA**: feed the parked `SpectreProgressAdapter` a rows-written/remaining
  denominator + a durable surface surviving a reconnect.
- **Exit:** an operator can preview "N rows / M chunks would move, K would skip" and watch it live.

### Phase 5 — Cutover-readiness gates
- ≥1 **full production-shaped UAT dry-run** (the deferred cutover gate, `CUTOVER_READINESS_BRIEF.md`).
- Flip the grant-refusal gate **advisory → hard pre-write stop** (per-pair at cutover, R6).
- Arm the **NM-73 auto-fallback** (CDC-silence → EXCEPT) now that J5 settles the CDC path.

---

## 4. Decisions needed from the operator

These change the design and are not in the code:

1. **Scale shape** — is ~200M the whole estate or the largest table? How many rows in
   **FK-target** tables (decides resident remap vs sink-resident spill), and how many **users**
   (the reconcile set)?
2. **Transfer-host memory budget** — sets the ~6 GB packed-remap spill trigger.
3. **The two un-run probes** — hand the operator the **P10** user-directory metadata probe now
   (read-only; confirms ReconciledByRule available) and design the **P7b** throughput harness for
   Phase 1.

---

## 5. Provenance

Synthesized from two background discovery workflows (2026-06-15): a J5-foundation sweep
(12 + critic + 6 fill agents) and an execution-maturity sweep (7 + critic + 6 fill agents),
~5M subagent tokens, every claim citation-backed. Durable facts also recorded in the agent's
session memory (`j5-cloud-uat-ledger`, `reverse-leg-execution-readiness`). The operator's J5
ledger (§1) was supplied verbally in-session and is not otherwise in the repo.
