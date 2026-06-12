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
**Re-imaging 2 (generation 3, 2026-06-11, post-PR-#596).** Builder session 1 executed H0/H1/H3
and closed H2 as refuted. This document was then re-verified card-by-card at HEAD `9ab0e4b`
by an independent session: every load-bearing citation re-read at the new HEAD, five fresh
adversarial briefs, and — because the builder's `bench/perf` witnesses were never committed
(gitignored by design, root `.gitignore:32`) — both empirical verdicts **re-run and
REPLICATED on a second host** (cliff: `.ok`=10000 at Count=1, zero `.cliff` samples;
materialize 4.83 µs/row = 40.1% of stream wall, vs the builder's 4.77/42%). Six further
corrections land as RI-7–RI-12 — including one claim of this document that is itself false
(RI-7) and one gen-2 forecast falsified by gen-2's own builder execution (RI-8). Cards F6
and H7 are added; three refusals join §6; the critical path is redrawn in §4. Where a card
body and an RI entry disagree, the RI entry is newer and wins.

> **The plan in one sentence.** Cut along sixteen verified cleavage planes in six stages —
> measurement first, and it has already paid: the MERGE cliff is REFUTED, the Q-gate is OPEN
> — then the quantum (now the critical path), the spine, the ledger, the Run, and licensed
> parallelism, with thirty-eight cards, every one independently shippable, every one
> carrying its witness, and the corrections ledger now running twelve entries deep across
> both the thesis and this document's own first edition.

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
*(True at first writing. PR #596 then landed H0/H1/H3's production touches —
`StaticSeedsEmitter.fs` +3, `ReadSide.fs` +13 (later line citations shift by up to 13),
`PerfHarnessScenarios.fs`, `scripts/perf-harness.sh`. Re-imaging 2 re-verified every
citation below at `9ab0e4b`; RI-7 onward are its findings.)*

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

*Re-imaging 2 (generation 3). RI-1–RI-6 corrected the thesis; RI-7–RI-12 correct this
document's first edition and the builder's first session. The surgeon's scans get re-imaged
too.*

**RI-7 — F1's casing claim is FALSE; the unification must be byte-stable, not normalizing.**
At HEAD the hex census is ten sites, not five — and the casing map in §2/N1 was wrong:
`CaptureJournal.fs:50-52` and `TransformRegistry.fs:533-534` produce **lowercase**
(`Convert.ToHexString` + `.ToLowerInvariant()`), not uppercase as claimed; the actual
uppercase sites are `PhysicalSchema.fs:611` (`hashStaticRow`'s hex view), `ReadSide.fs:824`,
and `SyntheticData.fs:167/183` — none catalogued in the first edition. Worse, two digests
are persistence-coupled: **CaptureJournal's digest IS the journal filename**
(`transfer-<digest16>.ndjson` — change its bytes and every existing journal orphans into a
silent fresh run, the exact resume-loss the journal exists to prevent), and `Run.InputDigest`
is persisted by `Run.save` (pinned at `RunTests.fs:71`). Card F1 is rewritten below: one
`Digest` module unifies the **mechanism** (SHA-256 → hex, zero-alloc); every call site keeps
its current byte-form; casing is never "fixed once." Byte-stability is the witness, and the
journal filename is the reason.

**RI-8 — The thesis's §9.8.11 prediction partially FAILED, by its own test, at H0.**
§9.8.11 predicted the harness scenario catalog would arrive as the fifth declare-once system
("one list … project[ing] names, tags, KeyLabels, and runs") and instructed: "if its builder
instead invents a new shape, this section has failed as a map and should be amended to say
why." H0 shipped scenarios as scattered gated `[<Fact>]`s plus a grep-able
`// PERF-SCENARIO:` comment registry — two enumeration surfaces, no single definition list,
no totality test. Gen-2's own §8 forecast ("recognized as an instance, not invented as a
shape") is thereby falsified in part — by gen-2's own builder session, the same day. The
repair is cheap and is now **card H7**; `CONSTELLATION.md` §9.8.11 carries the outcome note
in the same commit as this entry.

**RI-9 — F5's "bypassed drain" has a structural cause the card must respect.**
`ReadSide.drainRows` exists (`ReadSide.fs:249`, `let private`, 13+ in-file consumers — one
brief reported it absent and was corrected by direct grep), but `LiveProfiler.fs` compiles
**before** `ReadSide.fs` (fsproj lines 17/19), so the profiler structurally cannot reuse it.
F5 stays a local extraction, exactly as carded; the shared drain kernel is a named refusal
(§6) with its wake condition.

**RI-10 — The Q-track's totality precondition is stronger than RI-4 stated.**
The in-flight carrier is total **by construction**, not by scope policy: `readRowsStream`
walks `kind.Attributes` with `List.mapi`, and NULL columns land as `(name, "")` — never as
absent keys (`formatRawValue` null → `""`; ReadSide.fs:929-936 at current HEAD). The
omit-vs-NULL hazard is structurally confined to the IR grain. All 19 production `.Values`
sites re-verified at HEAD: by-key lookup, attribute-driven, or explicitly sorted — raw Map
iteration order is observable nowhere. Q1's permutation remains load-bearing for the two
name-sorted hashes.

**RI-11 — R1b's orphan census was understated by half.**
`withRun` wraps ~6 dispatch sites in `Program.fs`; of the 27 faces, **~21 run bare**, not
~11. R1b's law gains a scoping clause: every envelope-*emitting* verb moves under `withRun`;
pure read-only verbs may stay outside if they mint no envelopes (decided per-face, named in
the card's commit). And the existing env var is `PROJECTION_LEDGER_DIR`
(`RunLedger.configuredDir`) — R1b aligns with it rather than minting a second.

**RI-12 — Builder session 1 left no DECISIONS trace; an armed item's wake condition was
registry-invisible.** The cliff refutation, the staged-bulk demotion, and the Q-gate opening
lived only in `PERF_HARNESS.md` §5 and this document — while `CLAUDE.md` §7 names the
DECISIONS Active-deferrals index as *the* trigger registry, whose stated purpose is catching
silently-fired (and silently-armed) triggers. Fixed this session: `DECISIONS 2026-06-11 —
the perf-harness verdicts`, plus an index row for the staged-bulk wake condition.

**RI-13 (generation 4, 2026-06-12, the arc-cutter) — the Q-cards changed shape under the
knife, per the F1/F3 precedent.** Executing the arc: (a) Q4's deletion content (the per-row
`READSIDE_ROW` SsKey synthesis out of the stream) landed structurally AT Q2 — once
`readRowsStream` emits quanta, the synthesis has nowhere to live in the pull; it moved to
the ONE IR-grain boundary (`ReadSide.materializeStream`), where it carries its own
aggregated label (`readside.rowstream.materializeIr`) so the relocated cost stays on the
books (the §3.8/attribution rule, honored rather than dodged). (b) Q2 and Q3 shipped as ONE
commit: the backlog's own sequencing finding proves Q2 is not independently win-bearing, and
a Q2-only commit would have required staging a tree state that was never itself
suite-verified — the witness chain (pure pool + canary suite + full Docker pool, green at
the commit) outranks the card count. (c) Q3's `SurrogateCapture` incision generalized the
ladder over the row carrier (a staged `getterOf : Attribute -> ('row -> string)`) instead of
duplicating it — A40 applied at the carrier axis; the materialized path keeps `StaticRow`
through the same functions. The journal fingerprint bytes are unchanged — existing journals
resume across the carrier change (the RI-7 byte-stability discipline, applied to the
fingerprint plane).

---

## 2 — The cleavage-plane inventory

Thesis planes, re-verified, plus the planes the hunt found. Every citation read at HEAD this
session. Status: **fired** (consumers exist now) / **armed** (named consumer queued) /
**refused** (left alone, reason in §6).

| # | Plane | Both sides, cited | Status |
|---|---|---|---|
| T1 | the MERGE TVC cliff | `buildMergeStatementCore` adds all rows to one `InlineDerivedTable` (`ScriptDomBuild.fs:857`); no test at the >1000-row boundary | **closed — REFUTED 2026-06-11** (H1, replicated): the derived-table form executes at 10k rows; staged-bulk demoted to armed-perf (§6) |
| T2 | stage identity as strings | `pipelineStages` (`OperatorConsole.fs:91-100`) ∥ code-prefix convention (`Watch.fs:114-161`) ∥ emissions scattered across 4 pipeline files (RI-2) | fired |
| T3 | the two ledgers | `CaptureJournal` (quantum ledger) ∥ `LifecycleStore` (snapshot chain) — duals under one admission discipline (RI-3) | fired, corrected |
| T4 | the unwired Run | `Run.fs`/`RunHistory.fs`/`Ref.fs` shipped, test-only; four sinks unrouted; ~11 verbs outside `withRun`; one orphan `beginRun` | fired, reframed |
| T5 | the row carrier | `Map<Name,string>` + `READSIDE_ROW` sprintf per row (`ReadSide.fs:919-943` post-#596) vs the measured ~1.85 µs/row prior | **fired — gate OPEN 2026-06-11** (H3: materialize 4.77 µs/row = 42%; replicated 4.83 = 40.1%) |
| T6 | the comment-borne parallel contract | `Deploy.fs:425-436` MUST-docstring ∥ `DataEmissionComposer.fs:389` LINT-ALLOW ∥ `TopologicalOrder.levels` (`TopologicalOrder.fs:238`) | fired |
| N1 | **the digest twins + scatter** | `hashRowBytes` (`PhysicalSchema.fs:333-345`) ∥ `hashStaticRowBytes` (`:593-605`) — byte-identical, same file, walled by private scopes; + ten hex sites in three idioms: allocating-lowercase (`VersionedPolicy.fs:119-125`, `Run.fs:49-54`), zero-alloc-lowercase (`CaptureJournal.fs:50-52`, `TransformRegistry.fs:533-534`), zero-alloc-uppercase (`PhysicalSchema.fs:611`, `ReadSide.fs:824`, `SyntheticData.fs:167/183`). **Two persistence-coupled** (journal filename; `Run.InputDigest`) — RI-7 corrects the first edition's casing map | fired, corrected |
| N2 | **the Static-strip triplication** | `ReadSide.fs:1711` mints `Static rows` on readback; stripped at `ProfileCaptureRun.fs:25-27`, `DataIntegrityChecker.fs:127-136`, and `Preflight.fs:126` — where the strip is `Modality = []`, a **wider erasure** that would silently clear authored static populations | fired (+ latent bug) |
| N3 | **divergent physical-name resolution** | `TransferSpec.fs:80-87` (`OrdinalIgnoreCase`) ∥ `CatalogResolution` `tryKindByPhysicalTable`/`tryAttributeByPhysical` (case-sensitive `=`) — same lookup, different semantics; SQL identifiers are case-insensitive under default collation, so the case-sensitive side is the likely latent bug | fired |
| N4 | the profiler's private drain | `LiveProfiler.fs:261-270` hand-rolled `while` bypasses `drainRows`; outer `Bench.scope` makes a probe unnecessary (a thesis "Bench blindness" framing softened) | fired (small) |
| N5 | the journal's test blindspot | `CaptureJournal` exercised only through Docker-gated integration suites; zero pure-pool tests of load/append/fingerprint semantics | fired |
| N6 | Config parse/interpret | **closed as already-resolved**: `Config.fs` is purely syntactic; interpretation lives in the `*Binding` modules — the seam exists at the file boundary; the long-open item can close | — |
| N7 | **the fixture-catalog quadruplets** | `staticSeedCatalog` (`PerfHarnessScenarios.fs:165`) ∥ `wideSeedCatalog` (`:292`) ∥ the AC-X1 static catalog (`MigrationCanaryTests`) — the same hand-rolled static-kind `Catalog.create` chain, third+ instance shipped 2026-06-11 (`meshModel` is a cousin, not a twin: FK mesh, no static rows) | fired (test grain → F6) |
| N8 | the env-gate idiom | five `GetEnvironmentVariable … = "1"` sites across tests; identical parse, no observed divergence | refused (§6) |
| N9 | **the unregistered fifth declare-once system** | the scenario catalog (`PerfHarnessScenarios.fs`) ∥ the four shipped declare-once systems (§9.8.11): same shape, missing its single definition site and totality test — RI-8 | fired (→ H7) |
| N10 | **the consumer-less streaming-digest apparatus** | `RowDigester.empty/add/finalize` + `PhysicalSchema.withDigests`: zero call sites at HEAD (grep-verified, re-imaging 2) — `RowDigests` is always `Set.empty`, so `Missing/ExtraRowDigests`, their `isEqual` clauses (`PhysicalSchema.fs:789-790`), and their render blocks (`:927-928`) are structurally dead; the module docstring's "used by the canary" claim was false (fixed same-commit). The live hash path is `ofCatalog → hashStaticRow → hashRowBytes` (<100k Static populations only) | fired (→ F7) |

---

## 3 — The slice catalog

Card schema: **Plane · Incision · Unlock · Witness · Size · Deps · Rollback.**
Sizes: **S** ≤ half a day, **M** 1–2 days, **L** multi-day (used once). Every card is
independently shippable with the suite green; behavior-changing cards are witness-first.
Harness cards (H\*) carry their acceptance criteria **by pointer** to `PERF_HARNESS.md` §4 —
not duplicated here.

### Stage 0 — correctness and the fired trivia (no gates; start anywhere)

**H0 · The harness spine — DONE 2026-06-11** (builder session 1, commit `ec5ad7f`;
re-verified at `9ab0e4b` by re-imaging 2 — code read, acceptance re-exercised). PERF_HARNESS
§4 slice 0, verbatim: `PerfHarnessScenarios.fs` (types + `measure` + env gate +
`ssdtEmitOnly`) + `scripts/perf-harness.sh`. *Witness:* slice 0's own (zero-Δ double-capture;
fleet absent from `test.sh docker`) — passed in-commit. Residue: the catalog shipped outside
the declare-once shape (RI-8) — repaired by H7.

**H1 · The cliff's BEFORE witness — DONE 2026-06-11** (commit `261a8f9`); **verdict:
REFUTED** (see H2). Replicated by re-imaging 2 on a second host: `.ok`=10000 at Count=1,
`renderMerge.rows`=10000 at Count=1, zero `.cliff` samples. PERF_HARNESS §4 slice 1:
`seedMergeRender` + `seedMergeExecute` at 1k/2.5k/10k rows/kind + the `renderMerge.rows`
label. The witness answered the hypothesis — against the forecast, which is the harness
doing its job.

**H2 · ~~The cliff fix~~ — REFUTED BY H1 (2026-06-11, in-harness).** The execute probe ran at
1k/2.5k/10k rows/kind: **all executed** (`perf.seedMerge.execute.ok` = COUNT(\*)-verified row
counts; zero `.cliff` samples; `renderMerge.rows`=10000 at Count=1 proves the single-MERGE
form). The 1000-row TVC cap binds `INSERT … VALUES`, not the `MERGE … USING (VALUES …)`
derived-table form on SQL Server 2022. Plane T1 closes as **no-correctness-defect**; this
card's §8 forecast is FALSIFIED, as designed. The staged-bulk shape + `chooseMergeShape`
selector demote to **armed-perf** (wake: ≳100k-row static populations where the measured
~2.5k rows/sec execute slope matters — and note the 100k `readRows` threshold already bounds
IR-materialized populations). The critical path shortens: H0 → H1 → **H3**.

**F1 · The row-hash twin collapse — DONE 2026-06-11 (re-imaged, split per the one-plane
rule).** Plane N1's *core*: `RowDigester.hashRowBytes` (`PhysicalSchema.fs:333`) ∥
`PhysicalSchema.hashStaticRowBytes` (`:593`) were byte-identical `StaticRow → byte[]`
recipes in two `[<RequireQualifiedAccess>]` modules of one file, each privately walled, one
caller each. Incision: `RowDigester.hashRowBytes` becomes the canonical name (the RS-separator
note migrates onto it); `hashStaticRowBytes` deleted; `hashStaticRow` repoints to
`RowDigester.hashRowBytes`. Behavior-preserving (the bytes are identical) — the existing
PhysicalSchema canary suite is the byte-stability witness (13 green; the aggregate path and
the granular per-row-hex path now hash through one recipe by construction). S. Deps: none.
Rollback: revert.

*Re-imaging note (a disagreement with this card's own RI-7 rewrite):* the rewrite bundled the
twin collapse with a 10-site hex-idiom unification. Executing it, the one-plane rule bit: the
twins are a within-file, zero-risk dedup (the genuine "same shape built twice"); the hex
scatter is a **different plane** spanning four assemblies, three idioms, and two
persistence-coupled sites (the journal filename; `Run.InputDigest`) — high blast, low reward
(no bug; each site is internally consistent). Bundling them would have been the failure mode.
The twin collapse ships as F1; the hex scatter defers as **F1-hex (armed)**, §6 item 13.

**F2 · One Static-strip + the Preflight over-erasure fix — DONE 2026-06-11** (commit
`20a8a1b`). Plane N2 (step a). Incision: `Catalog.stripStaticPopulations` in Core (the name
landed; not `stripReadSideStatic` as the card first guessed); three sites delegate
(`ProfileCaptureRun`'s private helper deleted, `DataIntegrityChecker`'s trivial wrapper
inlined, `Preflight` narrowed from `Modality = []` to the Static-only strip). *Unlock:* the
4.4 trap gets one definition site; the latent authored-Static erasure (TenantScoped /
SoftDeletable / SystemOwned / Temporal) closes. *Witness shipped:* `` `F2:
stripStaticPopulations strips Static only — preflight preserves non-Static modality marks`
`` (pure pool, green by name). S. Deps: none. Rollback: revert (sites keep working).

**F3 · One physical-name resolution — DONE 2026-06-11 (re-imaged; commit `3cf9910`).** Plane
N3. The card recommended *structural* delegation (`TransferSpec` → `CatalogResolution`);
executing it, that was refused as **false symmetry** — the two sides return different types
(`CatalogResolution → SsKey` for the binders; `TransferSpec → Kind/Attribute` to drill into
columns), and one function would force a double lookup. The actual divergence is the
*comparison policy*: `CatalogResolution`'s physical lookups used case-sensitive `=` (the
latent bug — SQL Server is case-insensitive under default collation) while `TransferSpec`
compared case-insensitively. Incision: name the policy once —
`TableId.tableTextEquals`/`schemaTextEquals`/`ColumnRealization.columnNameEquals` (Core,
`OrdinalIgnoreCase`); the three case-sensitive `CatalogResolution` comparisons switch to it
(the behavior fix); `TransferSpec`'s two inline comparisons route through the same names
(behavior-preserving). *Witness shipped:* `` `F3: physical lookup resolves case-divergent
names identically from every entry point` `` (returns `None` on the old code). S. Deps: none.
Rollback: revert.

**F4 · Journal unit tests — DONE 2026-06-11** (commit `f33caf9`). Plane N5. Seven pure-pool
witnesses in `CaptureJournalTests.fs`: missing-file = fresh run; append/load round-trip
(fingerprint fields + `Pairs[][]`); accumulation; the `(kind, chunkIx)` last-write-wins
index; blank/literal-null line skipping; the digest-keyed filename law. **Finding pinned as
observed:** a corrupt non-JSON line *throws* — the load loop's `| null -> ()` tolerates only
the literal JSON `null`, not arbitrary garbage; the resume surface is not silently lossy on
corruption, a contract L2 inherits. *Unlock:* L2 lands against a pinned surface. S. Deps:
none. Rollback: n/a.

**F5 · The profiler's drain, extracted — DONE 2026-06-11** (commit pending). Plane N4. A
local `drainReader` (mirroring `ReadSide.drainRows`' exact signature/body) absorbs **two**
hand-rolled drain loops, not one: `reflectNullability` (`:106`) and `discoverKind` (`:262`) —
the N≥2 within-file consumers that justify the extraction. No probe added (the outer
`profile.live.*` scopes suffice — RI-grade softening of the thesis). RI-9's rationale holds:
`ReadSide.drainRows` exists but compile order (`LiveProfiler.fs` before `ReadSide.fs`)
forecloses reuse — the local kernel is the honest cut; the cross-file shared kernel is refused
in §6 item 10 with its wake condition. *Witness shipped:* the Docker-gated
`LiveProfilerIntegrationTests` (6/6 green against the warm container — both drain paths
exercised). S. Deps: none. Rollback: revert.

**F6 · One static-fixture catalog builder.** Plane N7. Incision: a test-tree
`StaticCatalogFixtures` module (parameterized: kind name, attribute shapes, rows) absorbing
`staticSeedCatalog`, `wideSeedCatalog`, and the AC-X1 catalog build; `meshModel` stays (a
cousin — FK mesh, no static rows). *Unlock:* the fourth instance never gets written; fixture
determinism has one definition site. *Witness:* harness scenarios + AC-X1 outputs byte-stable
across the move. S. Deps: none. Rollback: revert. Interleave filler — never block a gated
card on it.

**F7 · Retire the consumer-less streaming-digest apparatus.** Plane N10. The fold surface
(`RowDigester.State/empty/addInPlace/add/finalize`), `PhysicalSchema.withDigests`, the
always-empty `RowDigests : Set<PhysicalRowDigest>` axis, its two diff arms, two `isEqual`
clauses, and two render blocks — zero consumers, all arms structurally inert. Incision:
delete, per the dead-algebra precedent (`DECISIONS 2026-06-04`: zero-consumer
symmetry-builds get deleted). **Keep** `hashRowBytes`/`hashQuantumBytes` (live consumers:
`hashStaticRow`/`ofCatalog`; the Q-track) — the recipes are not the fold. Blast radius: ~13
sites in `PhysicalSchema.fs` + three stale comments (`Tolerance.fs:80`,
`AdjunctionLawTests.fs:190`, `ReadSide.fs:958-961`); zero test literals construct the axis
(grep-verified). *Witness:* suite green; the diff record loses two always-empty fields.
*Alternative disposition, named:* if the executing session is simultaneously opening the
>100k data-round-trip canary (the fold's only plausible consumer, now cheap over quanta via
`hashQuantumBytes`), wire it instead — but do not keep it on that hypothesis alone; the
precedent exists because "it might be wired someday" is how dead algebra accretes. S. Deps:
none. Rollback: revert (git preserves the fold).

**H7 · The scenario catalog becomes the fifth declare-once instance — DONE 2026-06-11.**
Plane N9 (RI-8). Shipped as `PerfHarnessScenarios.all : ScenarioDecl list` (Name/Docker/
Scale/`Make` thunk — thunks so gate-closed paths build no fixture; verified: 20 ms
pass-through SKIP); the seven gated facts index into it via `runDeclared` (undeclared name
fails the fact; declared-vs-built name drift fails loudly at run); the pure-pool totality
test pins registry ⇔ catalog — `` `H7: PERF-SCENARIO registry ⇔ the declared catalog — the
declare-once totality` `` + `` `H7: declared scenario names are unique` `` (the source file
read as fixture); `perf-harness.sh list` fixed to stop cutting scale alternations at the
first `|`. *Finding en route:* the substring pool-split trap (risk register) — the totality
test's first name embedded the Docker module's name and silently fell out of the pure pool
until renamed. Gate-open path re-verified end-to-end (`run ssdt-emit-only` green). §9.8.11's
outcome note already records the prediction's partial failure; this card is its repair —
the next three scenarios (H4–H6) now land declared. S. Deps: none. Rollback: revert.

### Stage 1 — the measurement substrate (PERF_HARNESS §4 slices 2–5, by pointer)

**H3 · ReadSide drain + the `materialize` isolating label** — **DONE 2026-06-11; the gate is
OPEN.** In-harness at 100k×12 (warm): end-to-end **11.40 µs/row**; the new aggregated
`materialize` sample isolates the carrier build at **4.77 µs/row = 42% of stream wall**
(wire+rest 6.63). The R4 premise is confirmed; the Q-track may proceed, gated per card on the
before/after protocol. (Label-overhead note: one `GetTimestamp` pair per row ≈ tens of ns —
two orders below the measured signal; the per-row-scope distortion the design feared was
avoided by the aggregated-sample shape.) **Replicated by re-imaging 2** (second host:
end-to-end 12.05 µs/row; materialize 483 ms = 4.83 µs/row = 40.1%); accumulator boundary
read against its documentation and they agree: `t0` after `ReadAsync`, stop before
`SsKey.synthesized`, one aggregated sample at EOF.
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

**R1b · Wire capture into the envelope — census corrected per RI-11.** `withRun` calls
`Run.capture`+`Run.save` under the existing `PROJECTION_LEDGER_DIR` (not a second env var);
the orphan verbs move under `withRun` — **~21 of 27 faces run bare today**, not ~11, so the
card scopes by the law: every envelope-*emitting* verb moves; pure read-only verbs that mint
no envelopes may stay outside, each named in the commit. The `runReadiness` orphan
`beginRun` (`RunFaces.fs:930`) is fixed. *Witness:* `` `every verb's run is capturable: no
orphan RunIds` ``. M–L. Deps: R1a; after S4 if sequenced late (the spine changes what
`withRun` brackets — do R1b after S4 to wire once). Rollback: env-gated.

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

**GATE: OPEN as of 2026-06-11** — H3 confirmed the carrier as the dominant non-wire cost
(4.77 µs/row = 42% of stream wall), and re-imaging 2 replicated it independently (4.83 =
40.1%). The premises beneath every Q-card were re-verified at `9ab0e4b` (RI-10): totality by
construction, 19 consumer sites all order-safe, both hashes name-sorted, `RowQuantum`/
`RowBasis` not yet extant.

**Q1 · `RowBasis` with the hash permutation — DONE 2026-06-11** (commit pending). Columns in
attribute order **plus a precomputed name-sorted ordinal permutation** (the RI-4 correction —
hashing walks the permutation; bytes identical to today). Shipped in `Catalog.fs`:
`RowQuantum` (`[<Struct>]`, the §9.7 promotion the H3 gate fired) + `RowBasis` (private record
+ `ofNames` smart ctor, the house derive-macro) + `RowQuantum.ofStaticRow`/`toValues`;
`RowDigester.hashQuantumBytes` in `PhysicalSchema.fs` shares `hashRowBytes`' exact recipe and
RS (0x1e) separator. Totality precondition documented on the type: quanta are in-flight
ReadSide-origin rows, total over the basis (absent-key semantics stay at the IR grain).
*Witness shipped:* `` `Q1: hashQuantumBytes equals hashRowBytes over any total row` `` — an
FsCheck property (100 random orderings) plus declarative edge cases (the reverse-sorted
permutation; values bearing `=`/RS; 12 interleaved columns), asserting byte-identical hashes
and a lossless `toValues` round-trip. The property test is the type's second consumer (the
zero-consumer tension resolved). S. Deps: gate (open). Rollback: revert; types unused until
Q2.

**Q2 · The stream re-typed — DONE 2026-06-12** (with Q3, one commit; RI-13). `readRowsStream`
emits `RowQuantum` (`[<Struct>]`, per the fired promotion); `Ingestion.streamKind` re-typed;
`streamsInOrder` DELETED rather than re-typed — its single consumer was `collectInOrder` and
the streaming realization streams per kind inside its own chunk loop, so the re-typed triple
would have shipped consumer-less (the dead-algebra precedent; a boundary-map correction —
the map said "re-type", the census at the cut said "delete"); the buffered `readRows` path and
`collectInOrder` convert at the ONE IR-grain boundary (`ReadSide.materializeStream` →
`StaticRow.ofQuantum` — Map + `READSIDE_ROW` Identifier minted there, preview/canary scale),
which carries the `materializeIr` aggregated label. *Witness shipped:* `` `R4: ofQuantum ∘
toQuantum = id` `` + pure pool + canary suite green at the commit.

> **Sequencing finding (re-imaging 2, Q1-session):** Q2 is **coupled to Q3+Q4**, not
> independently win-bearing. `readRowsStream`'s streaming consumers (`TransferRun.toCellsOver`/
> `pkOf`/`writeChunk`) read by-key, so a Q2 that flips the public return type to `RowQuantum`
> breaks them until Q3 moves them; and a Q2 that converts `RowQuantum → StaticRow` at the pull
> boundary to keep consumers working *adds* per-row cost (cells **and** Map **and** the
> SsKey) with **no measured win until Q4 deletes the synthesis**. Therefore execute **Q2→Q3→Q4
> as one focused arc**, green at each commit, with the byte-identical canary (Docker) as the
> standing witness and an H3 re-run at the end showing the `materialize` label drop. Do not
> ship Q2 alone expecting a number; the number is Q4's.
>
> **The arc's boundary map (verified at HEAD, session 2 close — the next session starts
> here, not at a fresh survey):**
> - `readRowsStream : SqlConnection -> Kind -> AsyncStream<StaticRow>` (`ReadSide.fs:837`)
>   has exactly **three** consumers: the buffered `readRows` (`:988`, `AsyncStream.toList`,
>   <100k, IR grain), `Ingestion.streamKind` (`Ingestion.fs:19`, a thin alias), and the
>   harness drain scenario (test).
> - `Ingestion` splits the two worlds cleanly: **`collectInOrder`** materializes
>   `Map<SsKey, StaticRow list>` for the pure plan (preview/canary scale — this is the
>   *materialized* path's single conversion point: quantum → StaticRow lands HERE, Map +
>   Identifier synthesized at the boundary, cost unchanged at its scale); **`streamsInOrder`**
>   is what the streaming realization consumes directly — THIS is the surface Q3 re-types to
>   `(SsKey * RowBasis * AsyncStream<RowQuantum>) list`.
> - **Renames become basis-level.** `RenameProjection`'s per-row `Map.toList` walk
>   (`RenameProjection.fs:55`) is, under a basis, a rename of the basis *header* done once
>   per kind — rows untouched. The ordinal rewrite does not port the per-row rename; it
>   deletes it.
> - **The attribution trap, named:** the `materialize` accumulator brackets the carrier
>   build *inside the pull*. Any Q2 form that relocates the Map/SsKey build to a consumer
>   boundary makes the label drop **without the cost dropping**. The label must ride the
>   carrier build wherever it lives, or the H3 re-run is void (the §3.8 trap in a new
>   costume). The honest end-state: after Q4 the label measures cells-build only, and the
>   end-to-end `.all` number — not the label — is the win's witness.
> - `RowDigester`'s fold is dead (N10/F7) — the arc does NOT need to wire quanta into it;
>   `hashQuantumBytes` already covers the hash plane (Q1, witnessed).

**Q3 · In-flight consumers — DONE 2026-06-12** (with Q2, one commit; RI-13). The streaming
realization consumes quanta end-to-end: renames are HEADER operations (`RowBasis.rename`,
once per kind — the per-row walk deleted, not ported; the materialized path keeps
`RenameProjection` at its scale); PK fingerprints + phase-2 identity re-keys index cells
through the basis (`RowQuantum.cellGetter`, ordinals resolved once per kind); FK re-point at
the quantum grain (`SurrogateRemap.remapQuantumFksWith` + `fkOrdinalsTargeting`, Name-ordered
so skip diagnostics are identical, copy-on-write cells); the capture ladder carrier-generic
(`SurrogateCapture` staged `getterOf` — A40 at the carrier axis); bulk lanes project via
`quantumCellsOver`. Journal fingerprint bytes unchanged — journals resume across the carrier
change. *Witnesses shipped:* `` `Q3: remapQuantumFksWith equals remapRowFksWith over any
total rows` `` (FsCheck) + `` `Q3: RowBasis.rename — the header rename equals the per-row
rename walk` `` + full Docker pool (reverse-leg Streaming/Canary/Property/Scale suites)
green at the commit.

**Q4 · Delete the per-row SsKey from the stream — DONE 2026-06-12** (deletion content landed
structurally at Q2, per RI-13; this card closed as the arc's measurement). The `READSIDE_ROW`
synthesis and its sprintf are out of the stream — identity at row grain is the PK cell
through the basis; the synthesis survives ONLY at the IR-grain boundary
(`materializeStream`), where the IR-grain `Identifier` consumers (sort passes,
`DataInsertRow`) still receive it unchanged. *Witness:* H3 re-run (same host, same warm
container, back-to-back with the BEFORE) — see PERF_HARNESS §5 Q-arc results.

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

*(Redrawn by re-imaging 2, 2026-06-11 — the first edition's graph survives in git; H0/H1/H3
are done, H2 is closed-refuted, and the correctness leg is retired.)*

```
H0 ✓ ──► H1 ✓ ⇒ REFUTED (H2 closed; staged-bulk → armed-perf, §6)
 │  └──► H7 ──► H4, H5, H6        (substrate completion; H7 first, so the
 │                                 next scenarios land declared — RI-8)
 └─────► H3 ✓ ══► [GATE OPEN] ──► Q1 ──► Q2 ──► Q3 ──► Q4
F1' F2 F3 F4 F5 F6               (independent; any order; fillers between larger cards)
S1 ──► S2 ──► S3 ──► S4 ──► S5   (parallel lane; disjoint files from Q until R1)
F4 ──► L1 ──► L2 ──► L4
        └───► L3
L1 ─┐
S4 ─┴─► R1a ──► R1b ──► R1d
        └─► R1c        └─► R1e (also needs S2)
P1 ──► P2 ; P1 + H-evidence ──► P3
```

**The critical path, redrawn and defended: Q1 → Q2 → Q3 → Q4.**
The first edition's critical path (H0 → H1 → H2) is retired the right way — its middle card
refuted its own forecast and the leg closed. What remains is the only track with a hard
evidence gate, and that gate is now **open**: the row quantum, the largest *measured* prize
on the board (materialize = 4.77–4.83 µs/row, 40–42% of stream wall, confirmed twice on two
hosts). The F-cards stay the warm-up batch (all S, all independent, F1 rewritten); the
S-track runs in its parallel lane unchanged — and note it remains the *structural* critical
path to the thesis's keystone (R1e runs through S4), which we deliberately do not cut first:
an evidence-gated win outranks structural completeness while its gate stands open and the
measurement is fresh. H7 precedes H4–H6 so the substrate finishes declared. R1a/R1c/F\* stay
the interleave cards — small, independent, ideal between larger slices or after a J5
interruption.

---

## 5 — Sequencing and the J5 preemption

Mapped onto `CONSTELLATION.md` §10 and `PERF_HARNESS.md` §4: Stage 0 here = §10 stage 0 +
the fired planes (the thesis's stage 0 grows the F-cards because fired trivia should never
queue behind gated work); Stage 1 = §10 stage 1; Stages 2–6 = §10 stages 2–5 with the ledger
and spine swapped into explicit parallel lanes (the thesis ordered them linearly; the
re-imaging shows they share no files until R1).

**The immediate queue (re-imaging 2, post-#596):** stage 0's H-leg is done; what remains of
stage 0 is the F-batch. In order: F2 → F3 → F1 (rewritten) → F4 → F5 → F6 as fillers, **Q1**
under the open gate, H7 then H4/H5/H6 to finish the substrate, S-track in parallel at the
builder's discretion. This is the builder-1 handoff's queue with H7 inserted and F1
corrected — divergences argued at RI-7/RI-8, not smoothed.

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

*Added by re-imaging 2:*

9. **The staged-bulk MERGE shape + `chooseMergeShape`: armed-perf** (demoted from
   correctness by H1's refutation — was the thesis's §9.8.4/§9.8.6 centerpiece). Wake:
   static populations ≳100k rows/kind, where the measured ~2.5k rows/sec execute slope or
   per-statement memory matters. Registered in the DECISIONS Active-deferrals index
   2026-06-11 (RI-12). Do NOT re-open the cliff as a correctness question; the witness is
   one command (`perf-harness.sh run seed-merge-execute`).
10. **A shared reader-drain kernel (LiveProfiler × ReadSide): refused.** Compile order
    forecloses reuse (RI-9) and one consumer does not justify relocating the kernel to a
    pre-profiler file. Wake: a third drain consumer outside `ReadSide.fs`.
11. **The env-gate helper (N8): refused.** Five sites parse `= "1"` identically; no
    semantic divergence observed; the cut is cosmetic today. Wake: a sixth gate, or any
    observed parse divergence between gates.
12. **perf-gate.sh ∥ perf-harness.sh aggregation unification: refused.** Different
    comparison models by design (μ+σ regression gate vs single-run exploratory delta —
    PERF_HARNESS §3.5 keeps them deliberately distinct). Wake: the first noisy-scenario
    promotion, which §3.5 already specifies as "reuse the perf-gate.sh aggregation python
    verbatim" — that moment *is* the unification moment.
13. **F1-hex — the SHA-256 hex-idiom unification: armed.** Ten sites in three idioms
    (allocating-lowercase ×2: `VersionedPolicy.fs:119`, `Run.fs:49`; zero-alloc-lowercase
    ×2: `CaptureJournal.fs:50`, `TransformRegistry.fs:533`; zero-alloc-uppercase: `PhysicalSchema`
    `AggregateHash`/`hashStaticRow`, `ReadSide.fs:824`, `SyntheticData.fs:167/183`). A `Digest`
    module (`sha256 : byte[] -> byte[]` + `hexLower`/`hexUpper`/`hexLower16`) would name the
    mechanism. Held because two sites are persistence-coupled — the journal **filename**
    (`transfer-<digest16>.ndjson`) and the persisted `Run.InputDigest` (`RunTests.fs:71`) — so
    any migration must be byte-exact for nil functional reward (no bug; each site is internally
    consistent). Wake: a *third* persistence-coupled digest, OR the casing actually diverging in
    a way that breaks a comparison, OR R3's L2 touching `CaptureJournal.digestOf` anyway (fold
    the unification into that commit, byte-stability as its witness — RI-7).

---

## 7 — The risk register

| Risk | Where it bites | The guard |
|---|---|---|
| name-sorted hash break | Q2/Q4 — any ordinal iteration that reaches the hash | Q1's permutation; the byte-identical canary witness is the card's acceptance |
| omit-vs-NULL semantics | any temptation to extend the quantum to the IR grain | the totality precondition is documented in `RowBasis`; the IR grain keeps `StaticRow` |
| `{create … with …}` default substitution | L3, R1a, any reconstruction site | count fields; the totality-contract discipline (`DECISIONS 2026-06-01`) |
| `DeleteScope` under chunking | the armed staged-bulk shape (§6 item 9), if its wake ever fires | the single-trailing-MERGE shape; the survival case in the witness |
| journal-filename coupling | F1 — any byte-form change to `CaptureJournal.digestOf` | byte-stability is the witness; the digest IS the address (RI-7) |
| fsproj compile order | any intra-assembly reuse plan (F5's lesson, RI-9) | check `<Compile Include>` order before carding a cross-file delegation |
| substring pool-split | any test whose display name embeds a Docker-collection module name | `test.sh` excludes the pure pool by `FullyQualifiedName!~<file>` substring — the H7 totality test silently fell out of the pure pool until renamed; never put a Docker-class module name in a test display name |
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

---

**Re-imaging 2 — verified directly (generation 3, 2026-06-11, at `9ab0e4b`):** the pure pool
green (3034 passed / 0 failed / 211 skipped, 67 s); both empirical verdicts re-run on a
second host and **replicated** (seed-merge-execute 10k: `.ok`=10000 at Count=1,
`renderMerge.rows`=10000 at Count=1, zero `.cliff`; readside-rowstream 100k: end-to-end
1205 ms = 12.05 µs/row, materialize 483 ms = 4.83 µs/row = 40.1% — builder's figures 11.40 /
4.77 / 42%, same verdict on both hosts); the builder's three production touches read
line-by-line (the materialize accumulator's boundary matches its own documentation; the
`renderMerge.rows` sample sits inside the existing `Bench.scope`); the digest census and its
two persistence couplings (RI-7); `drainRows` existence + the fsproj order (RI-9); in-flight
totality (RI-10); the `withRun` dispatch census (RI-11); `bench/perf` gitignored at root
`.gitignore:32` — which also means **the builder's witnesses survive only as re-runnable
commands**, a property this session exercised rather than lamented.

**Forecast outcomes (the four above, settled within one day of writing):**
(1) "the cliff exists" — **FALSIFIED**, as designed (H1/H2).
(2) "H3 will confirm the carrier" — **CONFIRMED, twice.**
(3) "S-track zero behavioral diffs through S3" — open.
(4) "the fifth declare-once system will be recognized, not invented" — **PARTIALLY
FALSIFIED by its own author's builder session** (RI-8); H7 is the repair.
Two forecasts resolved against their author's expectations in twenty-four hours: the
falsifiability was real, and the instrument that did the falsifying is the one stage 0
built.

**Testimony this session:** five fresh adversarial briefs (the F/Q/S censuses); each
load-bearing claim spot-checked in source before a card stands on it; one brief mis-reported
`drainRows` as absent and was corrected by direct grep (RI-9) — briefs remain testimony
until checked, every generation, no exceptions.

— Recorded for generation 3, the builder — and now BY generation 3, mid-build. Cut along
the planes; keep the patient breathing; re-run the witnesses you inherit before you stand
on them. Hold the spine; balance the books.
