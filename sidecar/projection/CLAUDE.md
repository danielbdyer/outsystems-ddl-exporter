# CLAUDE.md — V2 Sidecar Navigation

> **Adopted 2026-06-11** (operator approval; `DECISIONS 2026-06-11 — CLAUDE.md rebuilt from
> scratch`, amending the 2026-05-22 reading-order entry). The predecessor is archived verbatim
> at `CLAUDE_ARCHIVE_2026_06_11.md` (provenance only — do not read it for current state). The
> rebuild's governing rule: **this file points; it restates nothing** — with one exception,
> §4's survival list, whose entries are re-verified at every chapter close precisely because
> they are restatements. Any other restated count, line number, or feature inventory in this
> file is a first-class defect: fix it in the same commit as the discovery.

---

## 0 — What this file is

The first-read pointer surface for agents. It answers three questions — *what is this system,
what do I read, what will hurt me today* — and routes every other question to the surface
that owns it. It is not where disciplines land (`DECISIONS.md`), not where laws live
(`AXIOMS.md` + `AxiomTests.fs`), not where state lives (the debrief + the confirmed backlog),
and not a summary of any of them. When this file and a canonical surface disagree, the
canonical surface is right and this file has a bug.

## 1 — Orientation in sixty seconds

This is **V2 of a publication-and-provenance engine** for an evolving relational model
(sourced today from an OutSystems estate), publishing schema + data to on-prem SQL Server and
to an external SSIS consumer, accumulating an exact replayable provenance of every change,
terminating one day at an **eject** after which there is no upstream to re-derive from. Its
formal soul is one adjunction — `Ingest ∘ Project = identity`, modulo *named, closed*
erasures — lifted from states to the displacements between them: **state is a torsor over
delta**; minimality is measured (CDC capture count = the data norm); nothing is ever lost in
silence. The working gestalt: the engine is an **accounting system for change** — identities
are conserved charges, displacements are transactions counted by two independent rulers
(CDC for fidelity, Bench for cost), append-only ledgers hold the partial sums (the episode
store, the capture journal, the refactorlog), and the round-trip canaries are the audits.
Pure F# core with zero I/O; adapters at the boundary; every correctness claim a property
test; every refusal named; silence reserved as the strongest guarantee (CDC-silence on
idempotent redeploy).

V1 still owns the production write path (R6 dual-track); V2 emits-but-doesn't-ship until the
per-pair cutover gates flip. The current program (see `HANDOFF.md`) is the before/after
bottleneck sweep on the measurement substrate `PERF_HARNESS.md` designs.

## 2 — The reading order

**Tier 1 — every fresh agent, in this order (~40 minutes):**

1. `KICKOFF.md` — the five-minute brief.
2. `HANDOFF.md`, top letter only — where the work is *right now* and what the next program
   is. Letters below it are this chapter's history.
3. `THE_USE_CASE_ONTOLOGY.md` — **the single index of the target**: the move alphabet, the
   protein catalog (every operator workflow), the master matrix, the laws (torsor T12–T16 +
   A43, the faithfulness ladder, the intent filter, CDC-as-norm). Target-first; everything
   older is provenance, indexed in its §6.2.
4. `DEBRIEF_2026_06_02_ISOMORPHISM_CLIMB_AND_BACKLOG.md` + `CONFIRMED_BACKLOG_2026_06_09.md`
   — where the code stands against the target, and the living what's-left ledger. The
   masterwork is the destination; these are the distance.
5. `PERF_HARNESS.md` — the measurement fleet (design RESOLVED; slices 0–2 built) — and
   `CONSTELLATION_BACKLOG.md`, the slice plan that sequences the active build program.

**Tier 2 — before touching an area, read its surface:**

| Area you are about to touch | Read first |
|---|---|
| any Wave-6 / algebra / diff slice | `WAVE_6_ALGEBRA.md` (the equation to balance) + `AXIOMS.md` T12–T16/A43 + the debrief's cluster |
| the transfer / reverse leg / 288M program | `AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md` + `DECISIONS` 2026-06-10/11 entries (streaming realization, capability descent, the selector) |
| operator-facing copy, CLI, display | `THE_VOICE.md` + `THE_STORYBOARD.md` + `THE_VOICE_INTEGRATION.md`; `THE_INSTRUMENT.md` / `DYNAMIC_DISPLAY.md` / `REPORTING_HORIZON.md` for the display program |
| `projection.json` / flows / verbs | `THE_CONFIG_CONTROL_PLANE.md` (A44: expressible ⇔ reachable) |
| cloud insertion / data producers | `THE_DATA_PRODUCERS.md` + `PREFLIGHT_CLOUD_INSERTION.md` |
| synthetic data | `THE_SYNTHETIC_DATA_DESIGN.md` (σ: Profile → Data; π ∘ σ ≈ id) |
| architecture beyond the current program | `CONSTELLATION.md` — the architectural-future thesis: the eight stars, the holonic grain tower, the calculus and thermodynamics of the change-accounting, the adjudicated streaming question, R1–R5 with their reification in F#, and the pattern corpus. Read §10 (migration path) before §§1–9 if you only want the build order. |
| opening or closing a chapter | `V2_DRIVER.md` (the destination KPI; per-axis stakes) + `BACKLOG.md` (the operational ledger) + the chapter-rhythm entries in `DECISIONS.md` (strategic-frame axis-naming at open; chapter-mid-audit; the eight-item close ritual) |
| reaching for a name or a string/text primitive | `PLAYBOOK.md` decision trees — the executable form of pillars 7/8 |
| what V1 donated | `ADMIRE.md` (the editorial-inheritance ledger) |

**Tier 3 — provenance (read for depth on the fragment, never for current state):**
`NORTH_STAR.md` (where the bullseye was first named), `VISION.md` (cutover-era frame),
`WAVE_6_ONTOLOGY.md` / `WAVE_6_MORPHOLOGY.md`, `CRYSTALLINE_FORM.md` (the at-rest audit;
Intent/Quotient, the writer sinks, false symmetry), the dated audits, the chapter closes,
`CLAUDE_ARCHIVE_2026_06_11.md` (the predecessor of this file).
The latest-first rule governs: when two documents disagree, the newer one and the code win;
`DECISIONS.md` adjudicates.

## 3 — Where truth lives

One question, one owner. Do not answer these from this file or from memory.

| Question | The owning surface |
|---|---|
| What is the target end state? | `THE_USE_CASE_ONTOLOGY.md` |
| Where does the code stand against it? | the debrief + `CONFIRMED_BACKLOG_2026_06_09.md`; machine-read: `scripts/matrix-status.sh` → `NORTH_STAR.matrix.generated.md` |
| What has been decided, ever? | `DECISIONS.md` — read the latest ten entries first; the **Active deferrals** index at the top catches silently-fired triggers |
| Which laws hold, executably? | `AXIOMS.md` + `tests/Projection.Tests/AxiomTests.fs` — run `dotnet test --filter "FullyQualifiedName~AxiomTests"`; do not trust restated counts, anywhere, including here |
| What is happening right now? | `HANDOFF.md`, top letter |
| What is the active build program? | `PERF_HARNESS.md` |
| Where is the architecture headed? | `CONSTELLATION.md` |
| How do I run/measure/test anything? | `scripts/` — `test.sh`, `warm-sql.sh`, `perf-gate.sh`, `matrix-status.sh`; each script is its own documentation |
| What register does operator copy use? | `THE_VOICE.md` (the twelve rules; the banned list) |

## 4 — Survival rules (the will-bite-you-today list)

These are deliberately *restated*, not just pointed at — each has cost an agent real time
within their first session. Everything not on this list, this file only points to.

1. **Never run the pure and Docker test pools as one `dotnet test`** — it OOM-kills the host.
   Use `scripts/test.sh` (`fast` / `docker` / `canary` / `focus <name>` / `all`). And **CDC
   test classes always use `IsolatedContainerFixture`** — `sp_cdc_enable_db` flips
   instance-wide state; never on the warm container.
2. **A batch of `Could not open a connection` failures means the warm SQL container died**,
   not a regression. `scripts/warm-sql.sh restart`. Check `scripts/test.sh status` first,
   always. Same remedy for a second signature (2026-06-12): **a bulk load that hangs
   indefinitely** (no error, zero rows) on a long-running warm container is a
   RESOURCE_SEMAPHORE memory-grant stall — batch-size-independent; diagnose via
   `sys.dm_exec_query_memory_grants`, then restart. (PERF_HARNESS §5.) And a third
   (2026-06-12, S-track session): **`insufficient system memory in resource pool 'default'`**
   failing the 100k-row scale tests after several full Docker-pool runs on one warm
   container — the instance's memory pool degrades with accumulated load; it is the
   container, not the code. Restart, re-run the failed class focused, and only then suspect
   a regression. *Root cause found 2026-06-12 (L/R session): per-run databases were never
   dropped on the warm container (209 counted after one session) — `withBootstrappedDatabase`
   now reaps on exit, so accumulation is structural-fixed; the restart remedy stays for
   long-lived containers predating the fix. A fourth signature from the same family:
   **a batch of pre-login-handshake failures** across warm-container suites — same remedy.*
3. **Never `pgrep`-guard a test run; never watch a run through `| tail`** — the guard matches
   itself and tail buffers to EOF. Launch bare in the background; poll `test.sh status`.
4. **On any `Failed: N>0`, re-run with the TRX logger and grep the TRX** — console output
   interleaves and lies. (`DECISIONS 2026-05-20`, test-failure capture protocol.)
5. **FS3511 in Release builds**: no `let rec` inside a `task { }`, no tuple `let!`, no
   tuple-pattern `for`. Hoist helpers to module level; bind single values.
   (`DECISIONS 2026-06-10`.)
6. **`[<Literal>]` only on CLR primitives.** `[<Literal>] decimal` is a cctor bomb that
   detonates at module load as `InvalidProgramException`. Use `let private x : decimal = …`.
7. **`{ X.create … with … }` silently inherits constructor defaults** for every field the
   `with` block omits — the compiler does not warn. At reconstruction sites, count fields.
   (`DECISIONS 2026-06-01`, totality-contract verification.)
8. **`ReadSide` marks every reconstructed data-bearing table `Static`** — profiling a
   ReadSide-derived catalog yields an empty evidence cache unless the marking is cleared
   first. (The 4.4 trap.)
9. **`ISNULL(col,col)` strips IDENTITY through `SELECT INTO`; a `CASE` wrapper does not** —
   it constant-folds and the staging table mints its own keys. The keystone canary catches
   it; don't make it have to. (`DECISIONS 2026-06-10`.)
10. **`HANDOFF.md` is prepend-only via Edit — never overwrite with Write.** The chapter's
    letter history is the operating surface. And a handoff is a **forward-looking,
    second-person letter** to the next agent — never a status report of what shipped.
11. **The perf-gate baseline is re-recorded (`PERF_GATE_RECORD=1`) only when a floor
    legitimately moves, and only with a DECISIONS amendment naming why.**
12. **Docker-gated tests that soft-skip are indistinguishable from passes at summary level.**
    When the Docker pool's verdict matters, confirm against the TRX or `test.sh status`,
    not the green count.

## 5 — The load-bearing commitments (standing law, one line each)

Break none of these without writing the DECISIONS amendment *first*. Each line is a pointer;
the cited entry is the substance.

- **Pure core.** `Projection.Core` has zero I/O, no clock, no `Task`/`Async`, no module-level
  mutable state except `Bench` (the one named exception). Enforced by
  `NoUnsafeTimeInCoreAnalyzer` + audit.
- **A18.** Π consumes `Catalog × Profile` subsets, never `Policy`. Policy work belongs in a
  pass. Structurally enforced by the `Emitter<'e>` signatures.
- **Pillar 9 / A41.** Every transformation is `DataIntent` or a registered
  `OperatorIntent of OverlayAxis`; the registry drives the run from one `chainSteps`
  definition site; `registered ⇔ executed` is property-tested
  (`RegisteredAllTransformsBidirectionalTests`).
- **A35 / A36.** Π's canonical output is a typed deterministic statement stream; how a
  realization consumes it (bulk, text, parallel) is invisible to Π.
- **A39 / A40.** Aggregate-root smart constructors carry the invariants; single-axis-divergent
  algorithms become one parameterized algorithm.
- **T11.** Sibling emitters agree on the catalog keyset — structural, via
  `ArtifactByKind.create`.
- **Writer-fidelity + the Kleisli pipeline.** Passes are `Pass<'a,'b> =
  'a -> Lineage<Diagnostics<'b>>`; writer construction flows through the CE builders or the
  canonical primitives; A24's chronological bind extends to the stacked writer; laws
  property-tested.
- **T12–T16 + A43.** The change algebra (torsor round-trip, FTC replay, channel orthogonality,
  CDC-as-norm isometry, the commuting square, identity conservation) — all Bucket-A executable.
- **Every `AXIOMS.md` change carries its `AxiomTests.fs` entry in the same commit** — an
  axiom without its executable (or Skip-with-trigger) witness does not land.
- **Total decisions, named skips; refusals named, downgrades never silent.** From strategy
  outcomes to the realization selector (`ReverseLegRealization.choose`) to the capability
  ladder (descend only on the named capability error; every descent on the report).
- **R6 + the cutover ladder.** V2 emits-but-doesn't-ship; per-pair V2-driver transition gates
  on N=10 consecutive green canaries + operator sign-off; the T-30/T-15 fallback-ladder gates
  govern the rung; V1 stays warm through cutover+30.
- **V2 is self-contained; V1 is editorial donor only.** Carbon-copy + header citation +
  `ADMIRE.md` row; zero runtime dependency on V1.
- **IR grows under evidence; primitives at the second consumer; carriers reify eagerly,
  verbs at the second consumer.** The dead-algebra retirement (2026-06-04) is the enforcement
  precedent: zero-consumer symmetry-builds get deleted.
- **Determinism is constructed.** Sort by `SsKey`; `decimal` for continuous evidence; the
  boundary supplies clocks; byte-identical output is T1's claim and the codec's law.

## 6 — The style center (read the code; these are the gravities)

The canonical statement of each lives in `DECISIONS.md` (the operating-disciplines index at
its top) and in the code's own worked examples. The gravities, one line each:

- The type system is the contract: smart constructors, closed DUs, identity as a type
  (`SsKey`, `Name`), coordinates as VOs. (Compiler gap to remember: `String.Concat`/`Join`/
  `AddWithValue` accept `object`, so after every VO lift, grep for VO-bearing arguments.)
- **The private-constructor module is the house derive-macro**: `private` + smart ctor +
  `[<RequireQualifiedAccess>]` makes a law unforgeable (`ArtifactByKind` is the worked
  example; `CONSTELLATION.md` §9.8.9 enumerates the family).
- **The CE builder is the house syntax-macro**: writer-fidelity is impossible to violate
  inside `lineageDiagnostics { }`; new builders ship with their law-triple in the same commit.
- **The active pattern is the house match-macro**: adopted at N≥3 recurring match shapes,
  never before.
- Domain-first naming (pillar 8): concept-shaped names; the generic-suffix smell test stops
  you.
- Typed-AST-first for any SQL/JSON/XML emission (text-builder-as-first-instinct is the named
  failure mode); LINT-ALLOW only at terminal boundaries, with the four-question rationale.
- Lenses for nested IR updates; `Composition.fanOut` for registered-intervention drivers;
  CE form for pass-driver writer tails.
- Tests cite the law in their backticked names; property tests for combinatorial spaces;
  Skip stubs carry their promotion trigger; declarative test inputs over a constructed-valid
  generator.
- Measurement is curried into production (`Bench.scope` / `streamProbe` / `iterDo`): a hot
  loop without a label is unfinished code; the probe is identity on the value plane. Perf
  changes follow the bench protocol — three candidates, two refuted with data, one confirmed.
- **Audit during the work**: when something second-order surfaces mid-slice, act on it before
  shipping — codification absorbs refinements while the slice is hot.
- Sibling wrappers pass the distinguishing test: a wrapper that *supplies a default the
  caller couldn't compute* is the F# default-argument idiom; one that *hides information* is
  debt to collapse.
- Repeated SQL probes over one substrate collapse to **discover-once, derive-pure** (the
  `EvidenceCache` pattern), with the Big-O audit at the second derivation.
- Operator-facing strings obey the twelve-rule register; sites emit codes, the Voice owns
  copy, `code ⇔ copy` is tested.

## 7 — The F# surface, governed by triggers

The meta-rule stands: **purity-first core; adapters may use what Core forbids when their role
demands it.** Beyond that, this file no longer carries a hand-maintained feature inventory —
the predecessor's table drifted against the code within weeks (the case is enumerated in
`DECISIONS 2026-06-11` and the archive's banner). What governs instead:

- **The live inventory is the code.** Grep before assuming a combinator exists; the
  `AsyncStream` surface is exactly `nextBatch` / `toList` / `probe`, and grows per-combinator
  under real consumers only.
- **The trigger registry is `DECISIONS.md`'s Active deferrals index** — every consciously
  deferred feature (reflection, object expressions, type providers, SRTP, free monads,
  `IObservable`) has its re-open trigger there or in the entry that deferred it.
- **Currently-fired promotions awaiting their gates** are tracked in `CONSTELLATION.md` §9.7:
  `[<Struct>]` scoped to the row-grain carrier (fired by measured allocation priors; its
  gate — harness slice 2 — passed 2026-06-11; **landed with the Q-track 2026-06-12**,
  `RowQuantum`) and units of measure scoped to the `Run.diff` delta surface (fired by mixed
  quantities in one expression; still gate-held on R1d).
- **Three metaprogramming devices are sanctioned** (builder / active pattern /
  private-constructor module); quotations and type providers stay out absent a trigger.

## 8 — Maintenance contract

- This file updates at chapter closes (the chapter-close ritual's currency check) and when a
  Tier-1 reading-order surface changes ownership — never as a side effect of feature work.
- Anything in this file that restates a count, a line number, or a feature list is a bug
  unless it sits in §4 (survival rules), whose entries are re-verified at every chapter
  close precisely because they are restatements.
- The aspiration is T-IV (documentation totality): the more of this file that becomes a
  *generated* projection (the matrix status already is; the axiom buckets could be), the less
  of it can lie. Hand-written prose here should trend toward pointers and the survival list.
- When you find this file wrong, fix it in the same commit as the discovery, and say so in
  the commit message — drift in the index is a first-class defect, not cosmetics.
- The predecessor lives at `CLAUDE_ARCHIVE_2026_06_11.md`, provenance only.

## 9 — Closing

The codebase has earned its shape because the disciplines were operated, not admired. They
are not constraints; they are the load-bearing structure that lets each chapter carry more
weight than the last. Read the top letter, run the tests warm, name every refusal, count
every crossing, and leave the books balanced.

Hold the spine.
