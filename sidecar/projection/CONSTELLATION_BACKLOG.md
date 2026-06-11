# CONSTELLATION_BACKLOG — The Lapidary Plan

**Date:** 2026-06-11
**Status:** The surgical slice plan realizing `CONSTELLATION.md`'s R1–R5, its §10 staged path,
and the fired items of its §9.8 pattern corpus. Pairs with the thesis as
`INSTRUMENT_BACKLOG.md` pairs with `THE_INSTRUMENT.md`. Mission brief: `LAPIDARY.md`.
**Method.** Six adversarial re-imaging briefs, sectored by recommendation (R1–R5 + the
harness preconditions + a new-plane hunt), each instructed to *falsify* the thesis claim it
covered; every load-bearing finding spot-checked against source at HEAD `3daa298` (code tree
identical to the thesis's verification HEAD — zero src/tests commits since).
**Disclosure.** Generation 2 is the thesis's author. The conflict is named, and the
mitigations were structural: adversarial briefs, direct source verification of every claim a
card stands on, and the disagreements ledger below — which is **populated** (§1 records five
substantive corrections to the thesis, including one claim that is simply false). One agent
brief was caught fabricating a thesis quote during this work; its code citations were
therefore verified by hand before use (§8). The re-imaging was real.

> **The plan in one sentence.** Cut along thirteen verified cleavage planes in six stages —
> correctness first (the MERGE cliff, witness-first), measurement second (the harness),
> then the spine, the ledger, the Run, the quantum, and licensed parallelism — with
> thirty-six cards, every one independently shippable, every one carrying its witness, and
> five of the thesis's own claims corrected along the way.

---

## Contents

1. [The re-imaging report — where the thesis was wrong](#1--the-re-imaging-report)
2. [The cleavage-plane inventory](#2--the-cleavage-plane-inventory)
3. [The slice catalog](#3--the-slice-catalog)
4. [The dependency graph and the critical path](#4--the-dependency-graph-and-the-critical-path)
5. [Sequencing and the J5 preemption](#5--sequencing-and-the-j5-preemption)
6. [Refusals and armed wake conditions](#6--refusals-and-armed-wake-conditions)
7. [The risk register](#7--the-risk-register)
8. [The epistemic ledger of this document](#8--the-epistemic-ledger-of-this-document)

---

## 1 — The re-imaging report

No code aged: the tree at `3daa298` is byte-identical to the thesis's verification HEAD.
Every correction below is therefore a correction of the thesis's *reading*, not of drift.
Five substantive disagreements, each verified in source this session:

**RI-1 — The thesis's central R1 claim is false: the Run aggregate EXISTS.**
`src/Projection.Pipeline/Run.fs` (shipped 2026-06-05, commit "Masterful base #2: Run — the
addressable, content-addressed run aggregate"), with `RunHistory.fs` and `Ref.fs` beside it.
`Run.Run` carries `RunId / Ts / Command / InputDigest / Outcome / Canary /
Registered/Applied/Declined / Events / Artifacts` (`Run.fs:26-44`); `Run.capture/save/load`
exist; `Ref.RunArtifact` resolves `@runId` to artifacts. The thesis's §3.5 "today there is no
`Run` value" is wrong — the original eight-sector recon let `Run.fs` fall between sectors.
What IS true: `Run.capture`/`Run.save` have **zero production callers** (tests only,
`RunTests.fs`/`RefTests.fs` — grep-verified). R1 therefore reframes from *create the fifth
aggregate* to **complete and wire the existing one**. And the existing design corrects the
thesis a second time: it separates occurrence-identity (ULID `RunId`, minted at
`LogSink.fs:191-214`) from input-identity (`InputDigest`, `Run.fs:49-54`) — the thesis's §9.4
content-addressed `RunId` conflated the two. The existing factoring is adopted; the thesis's
is withdrawn.

**RI-2 — R2's blast radius is understated and its law needs a third arm.**
Stage emissions do not live in the run faces alone: they live in `Pipeline.fs:1292-1321`
(extract/profile/emit), `MigrationRun.fs:399-488` (emit/deploy/canary),
`TransferRun.fs:256-329, 1182-1188` (load), and `FullExportRun.fs:149-224` (the "pipeline"
umbrella). Of 27 run faces, 8 carry stage structure (2 trivial, 6 hard) and 19 are stageless;
~11 verbs run without `withRun` at all, and `runReadiness` mints an orphan `beginRun`
(`RunFaces.fs:930`). Three structural corrections the spine cards absorb: (a)
**aborted-at-stage is a real third outcome** — `MigrationRun.execute` can open "emit" and
error out without closing it (`MigrationRun.fs:399-424`), so `declared ⇔ executed` must admit
`Aborted`, not just started/completed; (b) the "pipeline" umbrella **nests** — `wall(umbrella)
> Σ wall(children)` by design, so additivity is per-level-of-nesting; (c) additivity is **not
plausible today** without bracketing the pre-flight gates, config/model resolution, and
`dumpBench` — the ε is currently large and partly I/O (real SQL in `migratePreflights`).
Also: the thesis's `Watch.fs:48` citation is off — the prefix-convention mechanics live at
`Watch.fs:114-161`; `:48` is the `StageState` DU.

**RI-3 — R3's two ledgers are duals, not twins; the `LedgerSpec` as typed paper-covers one.**
Verified: the journal stores **quanta** (`ChunkRecord` with `Pairs`; state reconstructed by
re-feeding into the mutable `PackedSurrogateRemap`, `TransferRun.fs:1029-1031`); the episode
store stores **full state snapshots** (`CatalogCodec.serialize e.Schema` per episode,
`LifecycleStore.fs:98`; diffs *derived* on load; `save` rewrites the whole file). One is a
partial-sum ledger; the other is a snapshot chain with derivable displacements. The
convergence diagnosis (append-only, admission-guarded, FTC-related, built independently with
zero cross-references) **stands** — but the thesis's `FingerprintOf : 'quantum -> 'fp` cannot
honestly cover episode admission, which is **by external witness** (B′≡B via `recordVerified`,
checked at write time only) rather than by recomputation. The corrected contract splits
admission: `WriteAdmit` (may demand an external witness — the `Verified<_>` token, minted
differently per grain) from `ResumeAdmit` (fingerprint recomputation against the live source).
The journal's fold is also effectful (mutation into `PackedSurrogateRemap`) — the instance
adapts; the spec stays pure. Two bonus findings: `CaptureJournal` has **zero
SQL-independent unit tests**, and the compaction problem is quantified (~9–10 GB NDJSON at
288M pairs, loaded fully into memory at resume, `CaptureJournal.fs:66-74`).

**RI-4 — R4 is sound in outline; two named risks bound the incision.**
All consumers access `StaticRow.Values` by Name with iteration driven by `kind.Attributes` or
explicit sorts — Map ordering is never load-bearing (full consumer census: ~19 production
`.Values` sites across 8 files; ~25 production + ~120 test sites total). The two risks: (a)
**both row-hash functions sort by Name** before hashing (`PhysicalSchema.fs:333-345` ∥
`593-605`) — a quantum iterated in attribute order would change every multi-column hash and
break the canary; the corrected incision has `RowBasis` carry a precomputed name-sorted
ordinal permutation, so hashing walks the permutation and stays **byte-identical**; (b) the
Map's **absent-key ≠ empty-string** distinction is load-bearing in `StaticPopulationEmitter`
(omitted column → SQL DEFAULT, not NULL; `StaticPopulationEmitter.fs:82-86`) — resolved by
scope: the quantum applies to **in-flight ReadSide-origin rows only**, which are always total
over the basis; the IR grain (where partial fixtures live) keeps `StaticRow`, exactly as the
thesis said. Per-row `Identifier` is read by three non-test consumers, all at the IR grain
(two sort passes + `DataInsertRow`) — untouched under the in-flight scope.

**RI-5 — R5's "entirely unwired" is stale; the real gap is the comment-borne contract.**
`ComprehensiveCanaryTests.fs:545-583` carries a `LeveledDeploymentPlan` and deploys
`Phase1Levels`/`Phase2Levels` through `executeBatchParallel` — the data-side wiring exists in
the comprehensive canary; the `Deploy.fs:436` docstring ("the canary continues using
sequential executeBatch…") predates it. What remains true and sharpened: the safety contract
is comment-borne (`ParallelSafe` is the fix, unchanged); the **schema side** has no leveled
grouping (`SsdtDdlEmitter.statementsWith` consumes flat `.Order`, never `.levels`); the
production CLI deploy path is sequential. And a load-bearing physical fact the thesis lacked:
**FKs are inline in `CREATE TABLE`** (`SsdtDdlEmitter.fs:294-320`), so cross-level
dependencies are real and level-by-level is the *only* safe schema parallelization — the
two-pass create-all-then-FK alternative is foreclosed by the emitter's own shape.
Harness precondition answered (PERF_HARNESS §3.7's open check): `GenerateSpec` mints static
SQL fixtures at arbitrary scale (`StaticEntities × StaticRowsPerEntity`) but **not**
`Modality.Static`-bearing IR — harness scenarios 3a/3b/3c construct catalogs via the
`meshModel` idiom. Confirmed: no `PerfHarnessScenarios.fs`, no `perf-harness.sh` exists.

**RI-6 — A thesis self-grade is corrected: §9.8.5's `Meter.pass` was marked "fired."**
Its claimed second consumer is the unbuilt spine. Re-graded **armed on R2**: the extraction
lands with the spine (card S1), not before. The two-consumer rule does not bend for the
thesis's own sketches.

---

## 2 — The cleavage-plane inventory

Thesis planes, re-verified, plus the planes the hunt found. Every citation read at HEAD this
session. Status: **fired** (consumers exist now) / **armed** (named consumer queued) /
**refused** (left alone, reason in §6).

| # | Plane | Both sides, cited | Status |
|---|---|---|---|
| T1 | the MERGE TVC cliff | `buildMergeStatementCore` adds all rows to one `InlineDerivedTable` (`ScriptDomBuild.fs:857`); no test at the >1000-row boundary | fired (correctness) |
| T2 | stage identity as strings | `pipelineStages` (`OperatorConsole.fs:91-100`) ∥ code-prefix convention (`Watch.fs:114-161`) ∥ emissions scattered across 4 pipeline files (RI-2) | fired |
| T3 | the two ledgers | `CaptureJournal` (quantum ledger) ∥ `LifecycleStore` (snapshot chain) — duals under one admission discipline (RI-3) | fired, corrected |
| T4 | the unwired Run | `Run.fs`/`RunHistory.fs`/`Ref.fs` shipped, test-only; four sinks unrouted; ~11 verbs outside `withRun`; one orphan `beginRun` | fired, reframed |
| T5 | the row carrier | `Map<Name,string>` + `READSIDE_ROW` sprintf per row (`ReadSide.fs:912-930`) vs the measured ~1.85 µs/row prior | armed (gate: H3) |
| T6 | the comment-borne parallel contract | `Deploy.fs:425-436` MUST-docstring ∥ `DataEmissionComposer.fs:389` LINT-ALLOW ∥ `TopologicalOrder.levels` (`TopologicalOrder.fs:238`) | fired |
| N1 | **the digest twins + scatter** | `hashRowBytes` (`PhysicalSchema.fs:333-345`) ∥ `hashStaticRowBytes` (`:593-605`) — byte-identical, same file, walled by private scopes; + `VersionedPolicy.fs:119-125`, `Run.fs:49-54` (allocating form, lowercase hex) vs `CaptureJournal.fs:48-52`, `TransformRegistry.fs:507-534` (zero-alloc, uppercase hex) — four hex idioms, five sites | fired |
| N2 | **the Static-strip triplication** | `ReadSide.fs:1711` mints `Static rows` on readback; stripped at `ProfileCaptureRun.fs:25-27`, `DataIntegrityChecker.fs:127-136`, and `Preflight.fs:126` — where the strip is `Modality = []`, a **wider erasure** that would silently clear authored static populations | fired (+ latent bug) |
| N3 | **divergent physical-name resolution** | `TransferSpec.fs:80-87` (`OrdinalIgnoreCase`) ∥ `CatalogResolution` `tryKindByPhysicalTable`/`tryAttributeByPhysical` (case-sensitive `=`) — same lookup, different semantics; SQL identifiers are case-insensitive under default collation, so the case-sensitive side is the likely latent bug | fired |
| N4 | the profiler's private drain | `LiveProfiler.fs:261-270` hand-rolled `while` bypasses `drainRows`; outer `Bench.scope` makes a probe unnecessary (a thesis "Bench blindness" framing softened) | fired (small) |
| N5 | the journal's test blindspot | `CaptureJournal` exercised only through Docker-gated integration suites; zero pure-pool tests of load/append/fingerprint semantics | fired |
| N6 | Config parse/interpret | **closed as already-resolved**: `Config.fs` is purely syntactic; interpretation lives in the `*Binding` modules — the seam exists at the file boundary; the long-open item can close | — |

---

## 3 — The slice catalog

Card schema: **Plane · Incision · Unlock · Witness · Size · Deps · Rollback.**
Sizes: **S** ≤ half a day, **M** 1–2 days, **L** multi-day (used once). Every card is
independently shippable with the suite green; behavior-changing cards are witness-first.
Harness cards (H\*) carry their acceptance criteria **by pointer** to `PERF_HARNESS.md` §4 —
not duplicated here.

### Stage 0 — correctness and the fired trivia (no gates; start anywhere)

**H0 · The harness spine.** PERF_HARNESS §4 slice 0, verbatim: `PerfHarnessScenarios.fs`
(types + `measure` + env gate + `ssdtEmitOnly`) + `scripts/perf-harness.sh`. *Witness:*
slice 0's own (zero-Δ double-capture; fleet absent from `test.sh docker`). S. Deps: none.
Rollback: revert; nothing depends on it yet.

**H1 · The cliff's BEFORE witness.** PERF_HARNESS §4 slice 1: `seedMergeRender` +
`seedMergeExecute` at 1k/2.5k/10k rows/kind + the `renderMerge.rows` label. Catalogs built
via the `meshModel` idiom (RI-5: `GenerateSpec` cannot mint `Modality.Static` IR). *Witness:*
the cliff hypothesis ANSWERED with captured evidence either way. M. Deps: H0. Rollback: n/a
(evidence, not behavior).

**H2 · ~~The cliff fix~~ — REFUTED BY H1 (2026-06-11, in-harness).** The execute probe ran at
1k/2.5k/10k rows/kind: **all executed** (`perf.seedMerge.execute.ok` = COUNT(\*)-verified row
counts; zero `.cliff` samples; `renderMerge.rows`=10000 at Count=1 proves the single-MERGE
form). The 1000-row TVC cap binds `INSERT … VALUES`, not the `MERGE … USING (VALUES …)`
derived-table form on SQL Server 2022. Plane T1 closes as **no-correctness-defect**; this
card's §8 forecast is FALSIFIED, as designed. The staged-bulk shape + `chooseMergeShape`
selector demote to **armed-perf** (wake: ≳100k-row static populations where the measured
~2.5k rows/sec execute slope matters — and note the 100k `readRows` threshold already bounds
IR-materialized populations). The critical path shortens: H0 → H1 → **H3**.

**F1 · Digest unification.** Plane N1. Incision: one `Digest` module in Core
(`sha256HexOf`, casing fixed once); collapse the twins; migrate `VersionedPolicy`, `Run`,
`CaptureJournal`, `TransformRegistry` call sites. *Unlock:* retires four hex idioms and the
allocating form; surfaces any latent upper/lower comparison. *Witness:* digests byte-stable
across the migration (existing round-trip tests). S. Deps: none. Rollback: revert.

**F2 · One Static-strip + the Preflight over-erasure fix.** Plane N2 (step a). Incision:
`Catalog.stripReadSideStatic` in Core; three sites delegate; `Preflight.fs:126` narrowed from
`Modality = []` to the Static-only strip. *Unlock:* the 4.4 trap gets one definition site;
the latent authored-Static erasure closes. *Witness:* `` `preflight preserves non-Static
modality marks` ``. S. Deps: none. Rollback: revert (sites keep working).

**F3 · One physical-name resolution.** Plane N3. Incision: `TransferSpec` delegates to
`CatalogResolution` with the case policy decided **once** — recommend `OrdinalIgnoreCase`
(SQL default-collation semantics), applied to both. *Witness:* `` `physical lookup resolves
case-divergent names identically from every entry point` ``. S. Deps: none. Rollback: revert.

**F4 · Journal unit tests.** Plane N5. Pure-pool tests: load/append round-trip, fingerprint
mismatch shape, missing-file = fresh run, malformed-line behavior (pin it, whatever it is).
*Unlock:* L2 lands against a pinned surface. S. Deps: none. Rollback: n/a.

**F5 · The profiler's drain, extracted.** Plane N4. `drainReader` extraction in
`LiveProfiler.discoverKind`; no probe added (the outer scope suffices — RI-grade softening of
the thesis). *Witness:* existing profiler suites. S. Deps: none. Rollback: revert.

### Stage 1 — the measurement substrate (PERF_HARNESS §4 slices 2–5, by pointer)

**H3 · ReadSide drain + the `materialize` isolating label** — **DONE 2026-06-11; the gate is
OPEN.** In-harness at 100k×12 (warm): end-to-end **11.40 µs/row**; the new aggregated
`materialize` sample isolates the carrier build at **4.77 µs/row = 42% of stream wall**
(wire+rest 6.63). The R4 premise is confirmed; the Q-track may proceed, gated per card on the
before/after protocol. (Label-overhead note: one `GetTimestamp` pair per row ≈ tens of ns —
two orders below the measured signal; the per-row-scope distortion the design feared was
avoided by the aggregated-sample shape.)
**H4 · executeStream batch sweep.** S. Deps: H0.
**H5 · The drains:** `staticPopulationDrain`, `physicalSchemaVerify`, `profilerDiscover`. M.
Deps: H0.
**H6 · `ossysParse` at ≥1k entities.** S. Deps: H0.

### Stage 2 — the spine (R2, corrected per RI-2)

**S1 · The spine types + the decoration value.** `StageName`/`RunSpine` (private smart
ctors), `StagedOutcome = Completed of ms | Aborted of refusal | Skipped of reason` (the RI-2
third arm), and `Meter.pass` extracted **as part of this card** (RI-6: its second consumer
arrives here). *Witness:* `` `Meter.pass p ≡ p on the value plane` ``. S. Deps: none.
Rollback: revert; types unused until S2.

**S2 · The `staged{}` CE + the law.** Bind brackets (Bench scope, `<stage>.started/.completed`
envelopes, Watch transitions); `Run` closure asserts `declared ⇔ executed-or-aborted` — an
open stage at run end becomes a named `Aborted`, never a board hang. Watch pre-seeds derive
from `RunSpine` (the string lists retire). Umbrella stages are the spine's root scope —
nesting is one level, by declaration. *Witness:* `` `R2: declared ⇔ executed∪aborted` `` +
WatchTests re-derived. M. Deps: S1. Rollback: faces keep old emissions until S3/S4 migrate.

**S3 · The trivial faces.** `runDeploy`, `runCanary` onto the CE — including the
`recordStage` vs `recordStageEvent` discrepancy fix at `RunFaces.fs:297`. *Witness:* envelope
streams byte-compatible (the pinned `FullExportCliTests` shapes are the guard for S4, not
here). S. Deps: S2.

**S4 · The hard faces.** `runFullExport` (umbrella + three children), `runMigrateExecute` /
`runMigrateWithData` (**pre-flight gates become declared stages** — they are real SQL I/O and
belong inside the meter), `runTransfer`/`runReverseLegTransfer`. Touches the four pipeline
files RI-2 names, not just `RunFaces.fs`; reconciles the two bracket owners (`withRun` vs
`FullExportRun`'s self-reset) to ONE. *Witness:* pinned envelope-shape tests
(`FullExportCliTests` slice-7 trio) stay green or are amended in the same commit with the
shape change named. **L.** Deps: S2, S3. Rollback: per-face — each face migrates in its own
commit.

**S5 · Additivity, honestly.** The property `` `wall(run) − Σ wall(stage) ≤ ε` `` lands only
after S4 has bracketed gates/config/bookkeeping as named stages; ε's residue is enumerated in
the test's docstring (arg parsing, process startup). S. Deps: S4.

### Stage 3 — the ledger contract (R3, corrected per RI-3)

**L1 · `LedgerSpec`, corrected.** Core, pure: `Genesis/Apply/FingerprintOf` **plus the
admission split** — `WriteAdmit` (external-witness-capable; mints `Verified<_>`) and
`ResumeAdmit` (recomputation vs stored fingerprint). `Ledger.replay`/`resumePoint` over
verified entries. *Witness:* the FsCheck FTC property over a constructed-valid generator. M.
Deps: none (F4 recommended first). Rollback: revert; instances not yet cut over.

**L2 · The journal instance.** `CaptureJournal` re-expressed on the contract; the effectful
remap fold adapted at the instance (the spec stays pure); resume path of
`writePlanStreaming` re-routed through `Ledger.resumePoint`. *Witness:* `` `R3: crash at
chunk k resumes at k; drift refuses by name` `` + the streaming ≡ materialized equivalence
canary, unchanged. M. Deps: L1, F4. Rollback: the old inline path is one commit back; the
equivalence canary guards both.

**L3 · The episode instance, honestly.** The snapshot-chain instance: `WriteAdmit` = the
B′≡B witness (`recordVerified` re-expressed as the `Verified` mint); `ResumeAdmit` = ordinal
monotonicity (named as such — the contract does NOT pretend to re-verify B′≡B at load); the
FTC fold remains the *verification property*, not the recovery path. The store keeps full
snapshots — converting to stored-diffs is **refused** (§6). *Witness:* `LifecycleStoreTests`
green, unchanged. S. Deps: L1. Rollback: revert.

**L4 · G10 onto the contract.** The progress table as the trivial single-quantum instance —
retired as a separate mechanism, honest that it exercises nothing (RI-3). *Witness:* the
resumable-transfer Docker test, unchanged. S. Deps: L2.

### Stage 4 — the Run, completed and wired (R1, reframed per RI-1)

**R1a · Complete the aggregate.** Add to the existing `Run.Run`: `Ledgers : LedgerRef list`
(journal file digests + episode coordinates) and `Bench : Bench.Run option`. Keep the
shipped ULID + `InputDigest` factoring (the thesis's content-addressed-RunId design is
withdrawn). Codec discipline per the house (totality over the new fields). S. Deps: none
(better after L1 for `LedgerRef` naming). Rollback: additive fields.

**R1b · Wire capture into the envelope.** `withRun` calls `Run.capture`+`Run.save` when
`PROJECTION_RUNS_DIR` is set; the ~11 orphan verbs move under `withRun`; the
`runReadiness` orphan `beginRun` (`RunFaces.fs:930`) is fixed. *Witness:* `` `every verb's
run is capturable: no orphan RunIds` ``. M. Deps: R1a; after S4 if sequenced late (the spine
changes what `withRun` brackets — do R1b after S4 to wire once). Rollback: env-gated.

**R1c · Bench keyed by run.** `BenchSink` filename = RunId (wall-clock moves inside the
value — `BenchSink.fs`'s reified boundary relocated, not deleted); `perf-gate.sh` discovery
updated from mtime-glob to newest-run. S. Deps: R1a. Rollback: dual-write one release.

**R1d · The projections.** `inspect <runId>` (D5 — the store already resolves; the verb
renders) and `diff <runA> <runB>` (`Run.diff` with the UoM delta surface; the harness's
before/after becomes its restriction to KeyLabels). M. Deps: R1a–c. Rollback: read-only verbs.

**R1e · The law.** `` `R1: live view ≡ projection of the stored Run` `` — the Watch board
reconstructed from `Run.Events` equals the board the live subscriber built; `readiness`
gauges over `RunHistory`. S. Deps: R1b, S2 (the spine makes board-reconstruction total).

### Stage 5 — the row quantum (R4, gated; corrected per RI-4)

**GATE: H3's wire/materialize attribution must confirm the carrier as the dominant non-wire
cost. If refuted, this stage is closed as a documented refuted candidate.**

**Q1 · `RowBasis` with the hash permutation.** Columns in attribute order **plus a
precomputed name-sorted ordinal permutation** (the RI-4 correction — hashing walks the
permutation; bytes identical to today). Totality precondition documented: quanta are
in-flight ReadSide-origin rows, always total over the basis (the absent-key semantics stay at
the IR grain). S. Deps: gate.

**Q2 · The stream re-typed.** `readRowsStream` emits `RowQuantum` (`[<Struct>]`, per the
fired promotion); `Ingestion.streamKind` re-typed; the buffered `readRows` path converts at
the boundary via `StaticRow.ofQuantum basis` (IR grain unchanged, 100k threshold unchanged).
*Witness:* `` `R4: ofQuantum ∘ toQuantum = id` `` + canary hashes byte-identical (Q1's
permutation). M. Deps: Q1.

**Q3 · In-flight consumers.** `TransferRun.toCellsOver`/`pkOf`/`writeChunk`,
`SurrogateRemap` ordinal overload (the A40 `*With` shape extended), `SurrogateCapture` —
in-flight sites only (~the streaming subset of the 19; IR-grain consumers untouched).
*Witness:* reverse-leg property + scale suites, green, with the before/after delta in the
commit message per the bench protocol. M. Deps: Q2.

**Q4 · Delete the per-row SsKey from the stream.** The `READSIDE_ROW` synthesis and its
measured sprintf (`ReadSide.fs:921-930`) go; identity at row grain is the PK cell through
the basis. The IR-grain `Identifier` consumers (sort passes, `DataInsertRow`) are out of
scope and unaffected. *Witness:* H3 re-run shows the materialize label's drop. S. Deps: Q2,
Q3.

### Stage 6 — licensed parallelism (R5, corrected per RI-5)

**P1 · `ParallelSafe`, minted by `levels`.** The token + `executeBatchParallel` re-signed to
demand it + `ComprehensiveCanaryTests` updated as the first consumer; the `Deploy.fs:436`
stale docstring retired. *Witness:* the leveled ≡ sequential equivalence already in the
canary, now type-guarded. S. Deps: none.

**P2 · Production leveled data deploy.** The canary-only wiring promoted to the CLI deploy
path behind the existing parallelism resolution stack. *Witness:* operator-reality canary +
perf-gate (baseline re-record only if the floor moves, with its DECISIONS amendment). S.
Deps: P1, and a Stage-0/1 measurement showing the win at operator scale.

**P3 · Schema-side levels.** `statementsWith` gains a leveled grouping (inline-FK fact makes
level-by-level the only safe shape — RI-5); deploy through `ParallelSafe`. **Trigger-held:**
opens only when the harness rollup shows schema deploy as a visible bottleneck
(PERF_OPPORTUNITIES' named trigger). M. Deps: P1, H-track evidence.

---

## 4 — The dependency graph and the critical path

```
H0 ──► H1 ──► H2                 (correctness path)
 │ └──► H4, H5, H6
 └────► H3 ═══════════► [GATE] ──► Q1 ──► Q2 ──► Q3 ──► Q4
F1 F2 F3 F4 F5                   (independent; any order; fillers between larger cards)
S1 ──► S2 ──► S3 ──► S4 ──► S5   (independent of H-track)
F4 ──► L1 ──► L2 ──► L4
        └───► L3
L1 ─┐
S4 ─┴─► R1a ──► R1b ──► R1d
        └─► R1c        └─► R1e (also needs S2)
P1 ──► P2 ; P1 + H-evidence ──► P3
```

**The critical path, named and defended: H0 → H1 → H2, then H3 → Q.**
Correctness outranks everything (the cliff is the one place the engine can *refuse a valid
estate at parse time*), and it is also the cheapest path to the harness paying rent — slice 1
was designed as the handoff's prime candidate. H3 → Q follows because the row carrier is the
largest *measured* prize (~1.85 µs/row against a 1.9 µs wire floor) and the only track with a
hard evidence gate. The S-track runs in parallel lanes at the builder's discretion: it is
behavior-preserving until S4, touches disjoint files from the H-track, and unblocks R1's
final form. R1a/R1c/F\* are the interleave cards — small, independent, ideal between larger
slices or after a J5 interruption.

---

## 5 — Sequencing and the J5 preemption

Mapped onto `CONSTELLATION.md` §10 and `PERF_HARNESS.md` §4: Stage 0 here = §10 stage 0 +
the fired planes (the thesis's stage 0 grows the F-cards because fired trivia should never
queue behind gated work); Stage 1 = §10 stage 1; Stages 2–6 = §10 stages 2–5 with the ledger
and spine swapped into explicit parallel lanes (the thesis ordered them linearly; the
re-imaging shows they share no files until R1).

**J5 preemption (binding):** if the operator arrives with a writable UAT connection, drop
everything. Every card above is ≤ M except S4 (which migrates face-by-face, one commit
each), so no card strands more than a day's work; H-track evidence artifacts survive any
interruption; nothing in flight may leave the canary red at a commit boundary, ever — which
is also the resume story after the interruption ends.

---

## 6 — Refusals and armed wake conditions

1. **Episode store → stored-diffs conversion: refused.** It would forfeit O(1) access to any
   intermediate catalog and break the store format for no consumer; the snapshot chain with
   derivable diffs is the *correct* dual (RI-3). Wake: a store whose size makes snapshots
   untenable — none in sight at sprint cadence.
2. **A `Selector<'req,'real>` abstraction: refused** (thesis §9.8.4 reaffirmed). Wake: a
   third concrete selector AND a consumer projecting over selectors-as-values.
3. **NDJSON writer unification (N6): refused.** `LifecycleStore` is a one-document store,
   not a line writer; only two true line-writers exist. Wake: a third NDJSON-line writer.
4. **Provenance-typed Static (N2 step b — the `ReadbackPopulated` variant): armed.** The real
   fix for the 4.4 trap, but it is a closed-DU change with codec-totality blast (every
   round-trip surface). F2's centralization buys the time. Wake: a fourth strip site
   appearing, or an authored+readback Static collision observed in practice.
5. **Journal compaction: armed**, now with numbers (~9–10 GB NDJSON; full in-memory load at
   resume — RI-3). Wake: the estate survey's FK-target pair count, or any real resume above
   ~10M pairs.
6. **Envelope spill for huge runs: armed.** `RunState.Envelopes` accumulates in memory;
   tolerable at operator-reality scale, unproven beyond. Wake: an estate an order of
   magnitude past the canary, or R1e's law failing on memory.
7. **Transfer wavefronts (P4): armed**, unchanged — wake: the real-wire bench missing 20k
   rows/sec.
8. The thesis §11 refusals (stream wrapper, Torsor typeclass, model-plane RowDiff,
   runtime-adaptive selectors, free monads, job scheduler) are **reaffirmed without
   exception**; nothing in the re-imaging weakened any of them.

---

## 7 — The risk register

| Risk | Where it bites | The guard |
|---|---|---|
| name-sorted hash break | Q2/Q4 — any ordinal iteration that reaches the hash | Q1's permutation; the byte-identical canary witness is the card's acceptance |
| omit-vs-NULL semantics | any temptation to extend the quantum to the IR grain | the totality precondition is documented in `RowBasis`; the IR grain keeps `StaticRow` |
| `{create … with …}` default substitution | L3, R1a, any reconstruction site | count fields; the totality-contract discipline (`DECISIONS 2026-06-01`) |
| `DeleteScope` under chunking | H2 | the single-trailing-MERGE shape; the survival case in the witness |
| pinned envelope shapes | S3/S4 | `FullExportCliTests` slice-7 trio amended in the same commit, shape change named |
| double bracket owners | S4 | one owner decided (the spine); `FullExportRun`'s self-reset retired in the same commit |
| aborted-stage display hangs | S2 | the `Aborted` closure arm is part of the law, not an afterthought |
| FS3511 Release shapes | any task-CE work in L2/Q2/Q3 | the survival rules; hoist and bind single values |
| perf-gate baseline drift | P2, Q3, anything that legitimately moves a floor | `PERF_GATE_RECORD=1` + the DECISIONS amendment, never silent |
| Docker soft-skip masking | every Docker-gated witness above | confirm via TRX / `test.sh status` when the verdict matters |

---

## 8 — The epistemic ledger of this document

**Verified directly this session** (read in source by the author): `Run.fs:26-56` (the
aggregate, `inputDigest`, the ULID factoring) + its git history + the production-caller grep
(tests only); the digest twins (`PhysicalSchema.fs:333-345` ∥ `593-605`, openings compared);
the leveled-parallel apparatus in `ComprehensiveCanaryTests.fs:545-583`; the three
Static-strip sites (`ProfileCaptureRun.fs:25`, `DataIntegrityChecker.fs:127-136`,
`Preflight.fs:126` — the third initially mis-pathed by its brief and located by hand); zero
code drift since the thesis (git log over src/tests).

**Testimony, adversarially solicited and consistent** (six briefs, each instructed to
falsify): the R2 face census (27 faces; 8 staged; the abort paths at
`MigrationRun.fs:399-424`); the R3 dual-structure analysis and the `WriteAdmit/ResumeAdmit`
correction; the R4 consumer census (19 production `.Values` sites; the hash-sort and
omit-vs-NULL findings); the R5 inline-FK fact and the `GenerateSpec` IR limitation. One
brief (R1) **fabricated a thesis quote** while its code claims were accurate — caught
because its quote did not match the thesis; its load-bearing claims were re-verified by
hand, and the incident is the reason every card above stands on a directly-read citation or
a falsification-instructed brief, never on a summary.

**The disagreements ledger (the mission's own bar):** five corrections to the thesis (RI-1
through RI-5), one self-grade reversal (RI-6), one thesis framing softened (N4's probe), one
thesis design withdrawn in favor of shipped code (the content-addressed RunId). The surgeon
found the scans wrong in five places; the patient is better mapped for it.

**Falsifiable forecasts this plan makes:** the cliff exists and H1 will capture it failing
above ~1000 rows; H3 will confirm the carrier as the dominant non-wire cost (the priors say
yes; the gate decides); the S-track will ship with zero behavioral diffs through S3; and the
fifth declare-once system (the harness's scenario catalog, H0) will be recognized as an
instance, not invented as a shape. Each lives or dies in the next agent's commits — which is
exactly where a backlog should keep its promises.

— Recorded for generation 3, the builder. Cut along the planes; keep the patient breathing;
write the BEFORE before the fix. Hold the spine; balance the books.
