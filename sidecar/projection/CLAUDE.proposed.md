# CLAUDE.md — V2 Sidecar Navigation (proposed rebuild, 2026-06-11)

> **STATUS: PROPOSAL.** This file does not take effect by existing. The incumbent `CLAUDE.md`
> remains the navigation surface until the operator adopts this one via a `DECISIONS.md` entry
> amending `DECISIONS 2026-05-22 — CLAUDE.md reading-order update`, plus the chapter-close
> ritual's currency check. **Why a from-scratch rebuild is proposed now:** the incumbent
> self-describes as "at higher drift risk than the other canonical surfaces because it indexes
> them" — and the risk has cashed. Verified drift in the incumbent as of today: its
> `AsyncStream` feature row still lists the combinator surface retired 2026-05-30 (the live
> surface is `nextBatch`/`toList`/`probe` — `AsyncStream.fs:23-84`); its canonical `dumpBench`
> pointer cites pre-decomposition `Program.fs` (now `OperatorConsole.fs:54`); its T11 note
> ("currently aspirational — three Π's return `string`") predates `ArtifactByKind` making T11
> structural (`ArtifactByKind.fs:69-82`); its hand-written axiom-entry count is stale. The
> design response is not better maintenance of restated detail — it is **less restated
> detail**: this file points, and restates only what bites an agent in their first hour. The
> structural principle is the one the codebase itself converged on (declare once, project
> many — the registry, the Voice, the config plane): substance has ONE definition site, and
> this file is a projection of pointers over those sites, never a second copy.

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
5. `PERF_HARNESS.md` — the active build program (design RESOLVED; the measurement fleet).

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
| what V1 donated | `ADMIRE.md` (the editorial-inheritance ledger) |

**Tier 3 — provenance (read for depth on the fragment, never for current state):**
`NORTH_STAR.md` (where the bullseye was first named), `VISION.md` (cutover-era frame),
`WAVE_6_ONTOLOGY.md` / `WAVE_6_MORPHOLOGY.md`, `CRYSTALLINE_FORM.md` (the at-rest audit;
Intent/Quotient, the writer sinks, false symmetry), the dated audits, the chapter closes.
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
   Use `scripts/test.sh` (`fast` / `docker` / `canary` / `focus <name>` / `all`).
2. **A batch of `Could not open a connection` failures means the warm SQL container died**,
   not a regression. `scripts/warm-sql.sh restart`. Check `scripts/test.sh status` first,
   always.
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
    letter history is the operating surface.
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
  `ArtifactByKind.create` (not aspirational; the incumbent's note is stale).
- **Writer-fidelity + the Kleisli pipeline.** Passes are `Pass<'a,'b> =
  'a -> Lineage<Diagnostics<'b>>`; writer construction flows through the CE builders or the
  canonical primitives; A24's chronological bind extends to the stacked writer; laws
  property-tested.
- **T12–T16 + A43.** The change algebra (torsor round-trip, FTC replay, channel orthogonality,
  CDC-as-norm isometry, the commuting square, identity conservation) — all Bucket-A executable.
- **Total decisions, named skips; refusals named, downgrades never silent.** From strategy
  outcomes to the realization selector (`ReverseLegRealization.choose`) to the capability
  ladder (descend only on the named capability error; every descent on the report).
- **R6 + the cutover ladder.** V2 emits-but-doesn't-ship; per-pair V2-driver transition gates
  on N=10 consecutive green canaries + operator sign-off; V1 stays warm through cutover+30.
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
  (`SsKey`, `Name`), coordinates as VOs.
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
  loop without a label is unfinished code. The probe is identity on the value plane.
- Operator-facing strings obey the twelve-rule register; sites emit codes, the Voice owns
  copy, `code ⇔ copy` is tested.

## 7 — The F# surface, governed by triggers

The meta-rule stands: **purity-first core; adapters may use what Core forbids when their role
demands it.** Beyond that, this file no longer carries a hand-maintained feature inventory —
the incumbent's table drifted against the code within weeks (see header). What governs
instead:

- **The live inventory is the code.** Grep before assuming a combinator exists; the
  `AsyncStream` surface is exactly `nextBatch` / `toList` / `probe`, and grows per-combinator
  under real consumers only.
- **The trigger registry is `DECISIONS.md`'s Active deferrals index** — every consciously
  deferred feature (reflection, object expressions, type providers, SRTP, free monads,
  `IObservable`) has its re-open trigger there or in the entry that deferred it.
- **Currently-fired promotions awaiting their gates** are tracked in `CONSTELLATION.md` §9.7:
  `[<Struct>]` scoped to the row-grain carrier (fired by measured allocation priors; gated on
  harness slice 2) and units of measure scoped to the `Run.diff` delta surface (fired by
  mixed quantities in one expression). Neither lands until its gate passes.
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

## 9 — Closing

The codebase has earned its shape because the disciplines were operated, not admired. They
are not constraints; they are the load-bearing structure that lets each chapter carry more
weight than the last. Read the top letter, run the tests warm, name every refusal, count
every crossing, and leave the books balanced.

Hold the spine.
