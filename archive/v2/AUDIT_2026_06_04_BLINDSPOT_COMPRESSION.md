# AUDIT 2026-06-04 — Blind-Spot & Compression Survey (`/sidecar/projection`)

**Date:** 2026-06-04
**Method:** Seven-agent parallel fan-out, one agent per structural seam, each
carrying the full concern-lens set (performance · maintainability · suitability
· algebraic structure · design patterns · dynamic programming / memoization ·
testability) with an explicit bias toward **compression / collapse /
normalization / abstraction**. Each agent was read-only and instructed to hunt
*blind spots* — what the existing self-audits structurally cannot see — rather
than re-derive known framings.
**Status:** Findings only. No code was modified in producing this document.
**Provenance of the existing audit corpus this one is differential against:**
`AUDIT_2026_05_31_FIVE_AXIS_REDTEAM.md`, `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md`,
`AUDIT_2026_05_DDD_HEXAGONAL_FP.md`, `AUDIT_2026_06_02_FSHARP_EIGHT_AXIS_REDTEAM.md`,
`WAVE_6_MORPHOLOGY.md`. This audit is deliberately *orthogonal* to those: they
audited for algebraic completeness and per-axis correctness; this one audits for
**duplication, half-applied disciplines, dead abstraction, and prose drift**.

---

## Contents

- [0. Scope metrics](#0-scope-metrics)
- [1. The unifying thesis](#1-the-unifying-thesis)
- [2. Cross-cutting independent signals (whole-repo grep)](#2-cross-cutting-independent-signals-whole-repo-grep)
- [3. Consolidated tiered synthesis](#3-consolidated-tiered-synthesis)
- [4. Unabridged regional findings](#4-unabridged-regional-findings)
  - [4.1 Core passes & algebra](#41-core-passes--algebra)
  - [4.2 Core domain model](#42-core-domain-model)
  - [4.3 Pipeline orchestration](#43-pipeline-orchestration)
  - [4.4 Targets / emitters](#44-targets--emitters)
  - [4.5 Boundary adapters](#45-boundary-adapters)
  - [4.6 Test suite](#46-test-suite)
  - [4.7 Documentation corpus](#47-documentation-corpus)
- [5. Suggested sequencing](#5-suggested-sequencing)
- [6. Methodology notes & caveats](#6-methodology-notes--caveats)

---

## 0. Scope metrics

| Region | Files | LOC |
|---|---|---|
| `tests/Projection.Tests` | 244 | 63,260 |
| `src/Projection.Core` | 79 | 20,913 |
| `src/Projection.Pipeline` | 28 | 9,626 |
| `src/Projection.Targets.SSDT` | 15 | 6,876 |
| `src/Projection.Adapters.Sql` | 11 | 4,725 |
| `src/Projection.Adapters.Osm` | 1 | 2,518 |
| `src/Projection.Cli` | 5 | 1,854 |
| `src/Projection.Adapters.OssysSql` | 5 | 1,850 |
| `src/Projection.Targets.Data` | 7 | 1,788 |
| `src/Projection.Targets.OperationalDiagnostics` | 6 | 1,214 |
| `src/Projection.Targets.Json` | 2 | 1,176 |
| `src/Projection.Targets.Distributions` | 1 | 344 |
| `src/Projection.Analyzers` | 1 | 199 |
| **Total `.fs`** | **542** | **116,343** |

- **Total `.fs` bytes (src+tests):** 5,825,467
- **Root markdown bytes:** 5,692,654 — the prose corpus is **~98% the size of the entire codebase**.
- Largest single files: `CatalogReader.fs` (2518), `ScriptDomBuild.fs` (1783), `ReadSide.fs` (1731), `Program.fs` (1659), `Catalog.fs` (1584), `Deploy.fs` (1470), `Profile.fs` (1314), `Config.fs` (1296), `LiveProfiler.fs` (1246), `MetadataSnapshotRunner.fs` (1196).

---

## 1. The unifying thesis

Seven agents, each scoped to a different region and blind to the others,
independently converged on one meta-pattern that none of the existing
self-audits names:

> **The codebase is over-abstracted along the *symmetry* axis and
> under-abstracted along the *duplication* axis. It builds each good discipline
> exactly once, then applies it in exactly *one* of the N places it belongs — so
> the duplication is invisible from inside any single file and surfaces only when
> sibling files are read side by side.**

The five disciplines the project is proudest of are each **half-applied**:

| Discipline | Applied in | Silently skipped in |
|---|---|---|
| EvidenceCache "discover-once / derive-many" | `LiveProfiler` | `ReadSide` (the canary's primary gate) — 15 hand-rolled reader loops, 2N round-trips |
| Index-precompute / Big-O audit | adapters (`ColumnsByKey`) | Pipeline `*Binding` resolvers (O(K·N·attrs) per bind); 5 graph passes rebuild adjacency |
| Topological-order hoist | Data composer (`emitWithTopo`) | SSDT-DDL + 7 other emitter sites recompute Kahn |
| `fanOut` pass-shape collapse | registered-intervention passes | 6 analytics passes hand-roll an identical `Touched`+Info epilogue |
| Shared test fixtures (`IRBuilders`) | thinly (51 LOC) | `mustOk` redefined in **63** files, `mkName` in 47, `mkKey` in 31 |

In the *opposite* direction it **over-builds**: `Prism`, `PassContext`
(comonad), `LineageTree` (branching writer), and `Pass.product`/`&&&` each ship
fully tested with **zero production callers** — each justified by a symmetry
argument ("completes the duo/trinity") in direct violation of the project's own
"IR grows under evidence / two-consumer threshold" rule.

The deeper reading: **the existing audits applied the optimization disciplines to
the boundary (adapters/profiler) but never turned them on the core passes layer
or on the test/doc surfaces** — which is how five independent adjacency rebuilds,
a `SchemaComplexityPass`-over-empty-topology wrong-answer, 63-way fixture
duplication, and a 5.7 MB prose corpus all accreted under a regime that believed
itself rigorously deduplicated.

---

## 2. Cross-cutting independent signals (whole-repo grep)

These were gathered by the lead (not the regional agents) to give an independent
check on cross-region claims. They corroborate the regional findings below.

**Suspected-speculative algebra — production-caller census** (`src/` files
referencing each symbol; a count of 1 means *definition file only*):

| Symbol | `src` files | Verdict |
|---|---|---|
| `PassContext` | 1 (`Diagnostics.fs`) | **dead in production** |
| `LineageTree` | 1 (`Lineage.fs`) | **dead in production** |
| `DiagnosticLattice` | 1 (`Diagnostics.fs`) | **dead in production** |
| `Prism` | 2 (`Optics.fs` def + `Diagnostics.fs` def) | **effectively dead** |
| `Certificate` | 3 | thin real use |
| `applyDiff` | 5 | **live** |
| `CatalogDiff.between` | 10 | **live** |
| `ChangeManifest` | 6 | live |
| `Episode` | 11 | live |
| `LifecycleStore` | 8 | live |

**Diff/lifecycle machinery is wired into production** (refuting the docs'
"zero production callers"): non-Core callers of `applyDiff`/`CatalogDiff.*` =
`RefactorLogEmitter.fs`, `SchemaMigrationEmitter.fs`, `Statement.fs`,
`Pipeline.fs`, `RenameProjection.fs`, `MigrationRun.fs`, `EjectRun.fs`,
`TransferRun.fs`.

**Test-fixture duplication** (files redefining a byte-identical private helper):
`let private mkKey` in **24** files (the agent's symbol-level count, which also
catches non-`private` forms, found 31), `mkCatalog` in 9, `mkKind` in 7.

**Discipline-vs-reality:** 34 `StringBuilder`/`sprintf` sites in `Targets.SSDT`;
179 `LINT-ALLOW` markers repo-wide; `AxiomTests.fs` carries 34 `Skip` against 86
`[<Fact>]` (≈40% of the runnable axiom catalog asserts nothing — the test agent's
finer count over the whole file was 34 Skip / 120 Fact ≈ 28%).

**Doc vs code size:** root `.md` = 5.69 MB vs `src`+`tests` `.fs` = 5.83 MB.

---

## 3. Consolidated tiered synthesis

### Tier 1 — Latent correctness bugs (ship-first; small & verifiable)

1. **`SchemaComplexityPass` computes FK metrics over an EMPTY topology.**
   `RegisteredTransforms.fs:69,124,190` wire it as
   `liftDecisionPass (SchemaComplexityPass.registered None)`, so
   `topoDefault = TopologicalOrder.empty` is baked in at registration — even
   though the chain computed the real `TopologicalOrder` one step earlier and
   threaded it into `state.TopologicalOrder`. Every
   `cyclomatic`/`coupling`/`cohesion`/`depth`/`OverallScore` is degenerate. Fix:
   the `liftTopologyPass`-style threading Centrality/BoundedContext already use
   (or a `liftCatalogTopologyPass` supplying `state.TopologicalOrder`).
   *High severity / High confidence.*

2. **`BoundedContextPass.pickLabel` tie-break compares a key to itself.**
   `BoundedContextPass.fs:80`: `-CompareOrdinal(rootOriginal lbl, rootOriginal lbl)`
   is always `0`; the intended label-ASC secondary sort is a no-op. Determinism
   survives only by upstream-sort luck. *Low severity / High confidence.*

3. **`ReadSide` exception leakage + retry gap.** Only 2 `try` blocks for 15 query
   sites; `cdcTrackedTables`/`cdcCaptureCount` return raw `Task<…>` (no `Result`)
   and don't catch; the top-level catch-all flattens every failure into one
   opaque `"readside.query.failed"`. `Retry.fs` (Polly transient-SQL) is wired
   *only* in `OssysSql` — `ReadSide` and `LiveProfiler` have **no retry**.
   *High severity / High confidence.*

### Tier 2 — Highest-leverage compression (apply your own disciplines)

| # | Finding | Location | Recoverable |
|---|---|---|---|
| 4 | Missing `Emitter.perKind` / `ArtifactByKind.ofCatalog` combinator (the `allKinds → map → ofList → create` body copied verbatim across 6 emitters) | `JsonEmitter:184`, `DistributionsEmitter:215`, `SsdtDdlEmitter:840`, `DecisionLogEmitter:127`, `DataEmissionComposer:83`, `RefactorLogEmitter:328` | ~6 copies → 1 |
| 5 | Analytics-pass epilogue (`Touched`-per-node + one Info diagnostic + `lineageDiagnostics` tail) hand-rolled in 6 graph passes | Centrality:150, BoundedContext:169, SchemaComplexity:131, QueryHint:93, ProfileAnomaly:129, TopologicalOrder ×2 | ~90 LOC → 1 combinator |
| 6 | `ReadSide` materialize loop (15 identical reader blocks); the `readResultSet<'T>` combinator already exists in `MetadataSnapshotRunner.fs:410` | `ReadSide.fs:225-664` | ~400 LOC → ~11 lines |
| 7 | `CatalogDiff` C1 channels — 4 identical records + 4 near-verbatim `between`/`isEmpty`/`changedFacets`/`apply` copies → generic `ChannelDiff<'facet>` per A40 | `CatalogDiff.fs:60-172, 296-464, 707-849` | ~400 LOC |
| 8 | `CatalogReader` God-file: JSON & rowset paths structurally parallel; ~52 `getProperty→validate→build` sites are an open-coded applicative | `CatalogReader.fs` | decompose + `JsonReader` applicative |
| 9 | `*Binding` catalog resolution reimplemented 5× as O(modules·kinds·attrs) re-scan per entry | `TighteningBinding:53`, `EmissionFoldersBinding:146`, `SpecialCircumstancesBinding:59` | ~100 LOC + perf |
| 10 | `*Diagnostics` profile-scan family → `DiagnosticRule` registry (the two threshold rules are identical, fanned-in by a hardcoded chain at `Pipeline.fs:873`) | Pipeline diagnostics | rule table |
| 11 | `MigrationRun` 2^N `executeXxx` explosion over {measure-CDC × record-episode × data-load} — the "overdifferentiated middle-tier" anti-pattern CLAUDE.md forbids | `MigrationRun.fs:375-607` | ~250 of 646 LOC |
| 12 | Test fixtures: `mustOk`×63, `mkName`×47, `mkKey`×31 redefine byte-identical helpers; promote to `IRBuilders.fs` | suite-wide | ~300-500 LOC |

**Deepest normalization (domain agent):** a single **`ColumnType` value object**
would single-source the facet tuple
`(Type, Length, Precision, Scale, IsIdentity, SqlStorage, Default, Computed)`
currently hand-enumerated in *five* places — `Attribute` (logical IR),
`PhysicalColumn` (physical IR), `AttributeFacet`/`changedFacets` (diff IR),
`SqlStorageType` (DU), `renderColumn` — plus a 6th **dead** copy
(`SqlStorageType.to/ofPrimitiveType`, zero callers, duplicating
`SqlTypeCorrespondence`'s live table).

### Tier 3 — Speculative-algebra dead weight

- Four fully-built, fully-tested surfaces with **zero production callers**:
  `Prism`, `PassContext` comonad, `LineageTree`, `Pass.product`/`&&&`; plus the
  self-flagged-dead lenses `CatalogLenses.sequences` / `indexesOf`. Quarantine in
  an `Algebra.Speculative` module behind a defer-with-trigger.
- The `Lineage<Diagnostics<'a>>` stack is heavier than its consumption: ~4 passes
  pay the `Diagnostics` tax to emit a single Info *status string* (a trace line,
  not an actionable finding) — that belongs in the existing `Bench`/trace sink.
- The emitter algebra is **not uniform**: three output shapes coexist
  (`PerKindArtifact` / `FlatStatementStream` / `CatalogSummary`), so the T11
  keyset-commutativity theorem silently holds for only 5 of ~11 emitters. The
  split is partly principled but **undocumented** — name it as a closed taxonomy
  in Core.
- `CatalogCodec` (887 LOC) enforces its round-trip law only by property test;
  write/read field-name literals sit ~400 lines apart with nothing coupling them,
  atop the `{ create … with … }` default-substitution hazard — a rename-drift
  time-bomb that compiles clean.

### Tier 4 — Documentation corpus (~45-55% losslessly compressible)

- **34% (~1.96 MB) is closed-chapter sediment** (`CHAPTER_*_OPEN/CLOSE`,
  `HANDOFF_CHAPTER_*`, `*PRESCOPE*`, `WAVE_6_*`) → `git mv` to `archive/`.
- **DECISIONS.md (1.47 MB)** violates its own "resolved questions, not session
  narrative" rule → ~300-440 KB prunable.
- **Stale load-bearing numeric claims** (triple-corroborated): "zero production
  callers" is now false; axiom count drifts A41/A42/A43 across docs; test counts
  (882/790/631/588) are all stale vs ~2,729 actual.
- **Index-of-indexes sprawl**: 3 docs each claim to be "the entry point"; the
  `THE_USE_CASE_ONTOLOGY` "subsumption" *added* a layer rather than collapsing one.

---

## 4. Unabridged regional findings

The seven sections below reproduce each agent's report in full, lightly edited
only for entity-unescaping and consistent file:line formatting. The caveats and
counter-signals (e.g. "this large file is healthy, NOT bloat") are retained
verbatim because they guard against over-correction.

### 4.1 Core passes & algebra

*Region: `src/Projection.Core/Passes/`, `Strategies/`, `Optics.fs`,
`Strategies/Composition.fs`, `PassChainAdapter.fs`, `ComposeState.fs`,
`Lineage.fs`, `Diagnostics.fs`, `Result.fs`, `TransformRegistry.fs`,
`RegisteredTransforms.fs`.*

The codebase already names its Kleisli arrow, writer trinity, optics duo,
`fanOut`, and the `opportunityEntry` N=3 defer. Below are the things those
framings **miss**.

#### Top 5 (highest leverage)

- **[DP/correctness] SchemaComplexityPass recomputes FK metrics over an EMPTY
  topology** — `RegisteredTransforms.fs:69,124,190` + `SchemaComplexityPass.fs:43-114`
  — In all three chains it is wired as
  `liftDecisionPass (SchemaComplexityPass.registered None)`, so its
  `topoDefault = Option.defaultValue TopologicalOrder.empty` bakes in an **empty**
  edge set / level structure at registration time. But the chain ran
  `TopologicalOrderPass` one step earlier and threaded the real result into
  `state.TopologicalOrder`. SchemaComplexity's `cyclomatic = edgeCount`,
  `coupling`, `cohesion`, and `depth = levels - 1` are therefore all computed from
  zero edges — `OverallScore` is degenerate whenever the pass runs in the
  registered chain. The fix is the same `liftTopologyPass`-style threading
  Centrality/BoundedContext already use, OR a `liftCatalogTopologyPass` that
  supplies `state.TopologicalOrder`. This is not just a DP miss; it's a latent
  **wrong-answer** that the "IR grows under evidence" discipline would have caught
  if the output were consumed. **Severity High, Confidence High.**

- **[compression] Six graph-analytics passes hand-roll the identical "Touched
  event per node + Info diagnostic + lineageDiagnostics tail" epilogue** —
  `CentralityPass.fs:150-169`, `BoundedContextPass.fs:169-188`,
  `SchemaComplexityPass.fs:131-150`, `QueryHintPass.fs:93-106`,
  `ProfileAnomalyPass.fs:129-142`, `TopologicalOrderPass.fs:690-695,795-800` —
  Every one of these ends with the literal triple:
  `let events = allKinds/nodes |> List.map (fun k -> { PassName; PassVersion; SsKey=k; TransformKind=Touched; Classification=DataIntent })`,
  then `lineageDiagnostics { do! writeLineages events; do! writeDiagnostic(s) …; return result }`.
  That is ~15 lines × 6 sites of pure scaffolding. `fanOut` collapsed the
  *registered-intervention* shape but left this **"analytics pass" shape**
  entirely un-abstracted. A single combinator — call it
  `Analytics.emit (passName, version) (nodes: SsKey list) (diagnostics: DiagnosticEntry list) (result)`
  producing the `Touched`/`DataIntent` events and the writer tail — would absorb
  all six. This is the single largest mechanical compression available in the
  region. **Severity High, Confidence High.**

- **[algebraic structure] Four named-but-dead algebraic surfaces ship with ZERO
  production callers** — confirmed by grep across `src/` (only definition sites +
  tests match):
  - `Prism<'a,'b>` — `Diagnostics.fs` (module Prism) — no caller; docstring even
    admits "Catalog ↔ DDL integration defers; the algebraic surface ships."
  - `PassContext<'env,'a>` reader comonad — `Diagnostics.fs` (H-062) —
    "Pass-driver adoption defers; the algebraic surface ships." Zero
    `extend`/`extract`/`applyEnv` callers.
  - `LineageTree<'a>` branching writer + `lineageTree { }` CE — `Lineage.fs`
    (H-005) — 26 tests, zero production `fork`/`paths`/`bifurcate`/`commit`
    callers. Its stated consumer (Cluster C policy diffing) does not exist yet.
  - `Pass.product` / `PassOperators.&&&` / `Pass.first` / `Pass.second` —
    `Diagnostics.fs:581-597` — monoidal product; only self-reference at
    definition.

  This is precisely the "speculative abstraction ahead of a second consumer" the
  codebase's own **two-consumer / "IR grows under evidence"** disciplines forbid.
  The codebase has erected a category-theory cathedral (comonad dual, branching
  monad, monoidal product, partial optic) on the strength of *symmetry arguments*
  ("completes the duo/trinity"), not consumer demand. Recommendation: move these
  to a clearly-marked `Algebra.Speculative` staging module or gate behind the
  same defer-with-trigger the lenses use, so the dead surface is structurally
  quarantined rather than interleaved with load-bearing code. **Severity Med (it's
  dead weight, not a bug), Confidence High.**

- **[DP/memoization] Each graph pass independently rebuilds adjacency/reverse-
  adjacency from `t.Edges`** — `CentralityPass.buildAdjacency` (out-degree +
  reverse adj), `BoundedContextPass.buildUndirectedAdj`,
  `TopologicalOrderPass.runIslandDetection` (undirected), `runCascadeShockZones`
  (cascade-only adj), `SchemaComplexityPass` (per-module intra/inter scans over
  `t.Edges`) — five distinct re-derivations of adjacency structure from the same
  edge list, each O(E) or worse, none memoized. `TopologicalOrder` already carries
  `Edges` but exposes no precomputed adjacency. Per the codebase's own
  **EvidenceCache "discover-once, derive-many"** discipline (chapter B.3) and
  **Big-O-audit-at-multiple-derivation-sites** discipline, the natural move is to
  compute `forward`/`reverse`/`undirected` adjacency maps **once** (on
  `TopologicalOrder`, or a `GraphView` derived from it) and thread them. The
  disciplines exist; they simply were never applied to the Cluster-D graph passes.
  **Severity Med, Confidence High.**

- **[testability/over-engineering] The `Lineage<Diagnostics<'a>>` stack is heavier
  than its consumption: every analytics pass emits exactly ONE Info diagnostic (a
  logging line), and 3 of the registered-intervention passes emit none
  structurally distinct** — Counting actual diagnostic production: NullabilityPass
  / UniqueIndexPass / ForeignKeyPass / ProfileAnomalyPass / QueryHintPass emit
  *decision-derived* Warning entries (genuine observer value); CentralityPass /
  BoundedContextPass / SchemaComplexityPass emit a **single Info "computed: N
  nodes, M edges, converged in K iterations"** line — that is a *trace/log*, not a
  diagnostic, and it forces the full dual-writer stack onto passes whose only
  "finding" is a status string. CategoricalUniquenessPass and
  TopologicalOrderPass's core `run` return *plain* `Lineage<_>` and get
  `Lineage.map Diagnostics.ofValue`'d into the stack — i.e. they pay the
  `Diagnostics` tax with an always-empty channel. The blind spot: the
  writer-trinity framing treats "produces a status string" and "produces
  operator-actionable findings" as the same channel. A `Bench`/trace sink (which
  already exists) is the right home for the Info lines; reserving `Diagnostics` for
  genuinely actionable Warning/Error entries would let ~4 passes drop to plain
  `Lineage<_>`. **Severity Med, Confidence Med.**

#### Additional findings

- **[design pattern] `StrategyEvaluator` seam pays off, but each `*Rules.evaluate`
  re-implements the same `if-gate / elif-structural / match-profile / mkDecision`
  skeleton** — `NullabilityRules.fs:223-277`, `UniqueIndexRules.fs:201-252`,
  `ForeignKeyRules.fs:241-315` — All three share the exact control-flow spine: (1)
  policy-disabled gate → keep-reason; (2) structural short-circuit
  (`AlreadyUnique`/`PrimaryKey`/`PhysicallyNotNull` / target-exists); (3) profile
  probe → reliable? → evidence-vs-threshold → outcome; every branch wrapped in a
  local `mkDecision`. The `StrategyEvaluator` alias unified the *signature*; it did
  **not** unify the *skeleton*. This is the codebase's own noted "active-patterns
  when the nested match recurs 3×" trigger — and it has now recurred 4×. A
  `DecisionSkeleton` combinator parameterized on
  `{ gate; structuralSignals; probe; threshold }` (or at minimum active patterns
  `(|PolicyDisabled|Structural|ProfileDriven|)`) would collapse the spine. The
  N=3-of-two-shapes defer was applied to `opportunityEntry`; the *evaluate
  skeleton* (N=4-of-one-shape) was never put through the same analysis. **Severity
  Med, Confidence Med.**

- **[compression] `decisionsOf` / `opportunityEntry`-tail / Bench-`iterMap` triple
  is copy-pasted across the 3 diagnostic-emitting fanOut passes** —
  `NullabilityPass.fs:238-273`, `UniqueIndexPass.fs:174-208`,
  `ForeignKeyPass.fs:290-330` — Each has the identical post-fanOut tail:
  `lineage.Value.Decisions |> Bench.iterMap "pass.X.Y" opportunityEntry |> List.choose id`,
  then the
  `lineageDiagnostics { let! value = lineage; do! writeDiagnostics entries; return value }`,
  then an identical `decisionsOf = LineageDiagnostics.payload`. The `FanOutConfig`
  could carry an optional `DiagnosticOf : 'decision -> DiagnosticEntry option` and
  `Composition.fanOut` could emit the diagnostic stream itself — folding the tail
  into the primitive that already owns the decision list. Currently `fanOut`
  returns `Lineage<'decisionSet>` and the diagnostic layering is re-bolted-on at
  each of the 3 sites. **Severity Med, Confidence Med.**

- **[maintainability] `ComposeState` is a 12-field flat option-bag with a
  hand-written `with*` setter per field** — `ComposeState.fs:17-99` — 11
  `Some`-wrapping setters, all structurally identical, plus an 11-field `initial`.
  Every new analytics pass adds: one field + one setter + one `initial` line + one
  `liftDecisionPass`/`writeBack` wiring. This is exactly the substructure the
  codebase's own `CatalogLenses` were built to address, yet `ComposeState` has no
  lens surface — the setters are the un-lensed record-spread the lens-adoption
  sweep retired everywhere else. A `ComposeState` lens family (or a
  `Map<DecisionKey, obj>`-free typed heterogeneous bag) would remove the per-field
  boilerplate. **Severity Low, Confidence Med.**

- **[algebraic structure] `CatalogLenses.sequences` and `indexesOf` are shipped
  lenses with only test callers** — `Optics.fs:111-115,138-142` — the file's own
  docstring admits "(no production consumer yet; defer-with-trigger pending)" for
  both. Per the IR-grows-under-evidence discipline these two lenses should not have
  shipped ahead of a consumer; they are the same speculative-surface smell as the
  dead algebra above, just self-flagged. (Lower severity because they're cheap and
  honestly documented.) **Severity Low, Confidence High.**

- **[performance] `BoundedContextPass.pickLabel` tie-break compares a key to
  ITSELF** — `BoundedContextPass.fs:80` —
  `-System.String.CompareOrdinal(SsKey.rootOriginal lbl, SsKey.rootOriginal lbl)`
  compares `lbl` against `lbl`, which is always `0`. The intended secondary sort
  key (label ASC) is therefore a no-op; ties resolve by `List.maxBy`'s positional
  stability, not by label. Determinism is preserved only incidentally (the upstream
  `List.sort` on neighbors). This is a latent bug masquerading as a tie-break.
  **Severity Low (output still deterministic), Confidence High.**

- **[maintainability] Per-pass `version` doc-comment ritual + LINT-ALLOW-FILE
  prose header is duplicated ~16×** — every pass opens with a near-identical "Pass
  version. Bump when: …" block and a "LINT-ALLOW-FILE: pass-driver … sprintf …
  per DECISIONS 2026-05-09" banner. This is documentation boilerplate the audit
  framing counts as "self-aware" but which still represents ~10 lines × 16 of
  non-load-bearing prose; a single referenced discipline doc + a
  `[<Literal>] version` convention would suffice. **Severity Low, Confidence Med.**

#### Biggest blind spot (synthesis)

The codebase audited itself for *algebraic completeness* and
*registered-intervention* duplication, and in doing so built a category-theoretic
surface (Prism, PassContext comonad, LineageTree, monoidal `&&&`) on symmetry
arguments while leaving its **second structural family — the Cluster-D "analytics
pass" (graph-in → metric-out → Touched-events + one Info line)** — completely
un-abstracted across six near-identical modules. The deeper miss is that the
codebase's own load-bearing disciplines (EvidenceCache "discover-once",
Big-O-audit-at-multiple-derivations, IR-grows-under-evidence, two-consumer
threshold) were rigorously applied to the *adapter/profiler* layer but never
turned on the *passes* layer — which is how five independent adjacency rebuilds
and a `SchemaComplexityPass`-over-empty-topology bug slipped through unnoticed. In
short: the engine proved theorems about its abstractions but stopped consuming its
own optimization disciplines at the door of `Passes/`, so the speculative algebra
grew while a real DP-threading correctness gap sat one `liftTopologyPass` away
from being fixed.

### 4.2 Core domain model

*Region totals: 9,038 LOC across 23 files; the three largest (`Catalog.fs` 1584,
`Profile.fs` 1314, `CatalogDiff.fs` 1017) hold 43% of it.*

#### Top 5

- **[COMPRESSION/NORMALIZATION] The SQL-type mapping is encoded twice; half of one
  copy is dead** — `SqlStorageType.fs:79-128` vs `SqlTypeCorrespondence.fs:56-124`
  — Two modules own the same `PrimitiveType ↔ SQL-Server-vocabulary`
  correspondence. `SqlTypeCorrespondence.baseName`/`ofSqlDataType` is the *live*
  table (consumed by `ReadSide.fs:56`); `SqlStorageType.toPrimitiveType`/
  `ofPrimitiveType` is a *second, finer* table over the same 9 `PrimitiveType`
  variants and the same ~28 SQL base names — and has **zero production callers**
  (grep: only doc-comment + test references). The collapse arises because
  `ofSqlType` (the parser, *which is* used) lives in `SqlStorageType.fs` while
  `ofSqlDataType` (a coarser parser of the same strings) lives in
  `SqlTypeCorrespondence.fs`. **Recommendation:** make `SqlStorageType` the single
  normalized table — derive `PrimitiveType` mapping via `toPrimitiveType` and
  define `SqlTypeCorrespondence.ofSqlDataType = ofSqlType >> Option.map toPrimitiveType`,
  deleting the parallel string-match. Severity: **Medium** / Confidence: **High**
  (caller absence verified by grep).

- **[DENORMALIZATION] `Attribute` is ~22 fields = three glued sub-records** —
  `Catalog.fs:446-559` — One record carries (a) **identity/logical**: SsKey, Name,
  Type, Column, IsPrimaryKey, IsMandatory, IsActive, Description, OriginalName,
  ExtendedProperties; (b) a **SQL-type-realization cluster**: Length, Precision,
  Scale, IsIdentity, SqlStorage, ExternalDatabaseType, plus Type — *exactly* the
  6-7 fields `PhysicalColumn` and `SqlStorageType` independently model; (c) a
  **DEFAULT/computed cluster**: DefaultValue, DefaultName, Computed. The (b)
  cluster is a denormalized inline copy of `SqlStorageType` (which can already
  represent length/precision/scale/identity-implied facets *as DU payload*).
  **Recommendation:** extract a `ColumnType` VO grouping
  {Type; Length; Precision; Scale; SqlStorage; ExternalDatabaseType} — collapses
  `changedFacets` (CatalogDiff) and `toPhysicalColumns` (PhysicalSchema) into one
  structural comparison and removes the per-field facet enumeration. Severity:
  **Medium** / Confidence: **Medium** (the smart-constructor-absorbs-fields pattern
  at `Catalog.fs:1004` is deliberately fighting this width).

- **[SUITABILITY — STALE CLAIM REFUTED] The diff/lifecycle cluster is NOT dead — it
  has broad production callers** — the docs' "ZERO production callers"
  (`WAVE_6_MORPHOLOGY.md`, 2026-06-01) is **stale**. Confirmed live callers:
  `MigrationRun.fs:116,141,179,207` · `EjectRun.fs:34-43` · `LifecycleStore.fs:202-219`
  · `Pipeline.fs:1075,1091,1104` · `RefactorLogEmitter.fs:208,250,320` ·
  `SchemaMigrationEmitter.fs` · `TransferRun.fs:673,725` ·
  `Cli/Program.fs:221,1084,1097,1185`. The cluster (`CatalogDiff` 1017 +
  `Migration` 230 + `Lifecycle` 198 + `Episode` 243 + `ChangeManifest` 101 ≈
  **1,789 LOC**) is an *activated* subsystem post-Wave-6.A/D/H (per the 2026-06-02
  debrief). **Recommendation:** none on the code; flag that the morphology/algebra
  docs describe the pre-6.A snapshot and should not be read as current dead-code
  evidence. Severity: **Low (code)** / Confidence: **High**.

- **[ABSTRACTION] Four identity-remap concepts are one parameterized remap wearing
  four coats** — `SurrogateRemap.fs` (230) · `UserRemap.fs` (200) ·
  `Reconciliation.fs` (173) · `Identity/UserIdentity.fs`. `UserRemapContext`
  (`SourceUserId→TargetUserId` + Unmatched + diagnostics) is *structurally*
  `SurrogateRemapContext` specialized to one kind — and the code already admits
  this: `UserRemap.toSurrogate` (`UserRemap.fs:183`) and
  `Reconciliation.ofUserMatching` both *project into* `SurrogateRemapContext`, and
  `ReconciliationStrategy` is documented as "the surjective image of
  `UserMatchingStrategy`" (`Reconciliation.fs:41-45`). So three acquisition
  surfaces (user-matching, reconcile-against-sink, operator-supplied) already
  converge on one carrier. The residual duplication is the *typed-orientation pair*
  (`SourceUserId`/`TargetUserId` vs `SourceKey`/`AssignedKey`) and the parallel
  `RemapDiagnostic`/`UnresolvedReference` types. **Recommendation:** the
  convergence is 80% done; the remaining win is unifying the two diagnostic types
  and noting `UserRemapContext` could be `SurrogateRemapContext` + a kind tag rather
  than a parallel record. Severity: **Low** / Confidence: **Medium** (the
  `toSurrogate`/`ofUserMatching` bridges are deliberate, not accidental).

- **[PARALLEL IR — QUANTIFIED] Logical IR ⊃ Physical IR, and
  `PhysicalSchema.ofCatalog` already proves it's a pure projection** —
  `PhysicalSchema.fs:686-736` — `PhysicalSchema` is *not* maintained in parallel;
  it is **derived** from `Catalog` by `ofCatalog` (a one-way functor). The genuine
  duplication is at the *field* level: `PhysicalColumn`
  (`Schema,Table,Column,Type,Nullable,IsPrimaryKey,Length,Precision,Scale,IsIdentity,Default,Computed`
  = 12 fields) is a flattened, string-typed re-spelling of `Attribute`'s (b)+(c)
  clusters; `PhysicalForeignKey` re-spells `Reference`+target-PK resolution;
  `PhysicalIndex` re-spells `Index`. This is the documented-and-accepted "separate
  IR domain" (`Coordinates.fs:52-66`) — the string-typing is intentional (it's the
  SQL-Server-catalog-readback comparison surface, where logical VOs would be a
  category error). **The real blind spot:** the duplication isn't structural
  maintenance cost (it's generated), it's that the *facet list* "(type, nullable,
  pk, length, precision, scale, identity, default, computed)" is hand-enumerated in
  **three** places — `PhysicalColumn`/`toPhysicalColumns`
  (`PhysicalSchema.fs:430-450`), `AttributeFacet`/`changedFacets`
  (`CatalogDiff.fs:32-41, 269-279`), and `renderColumn`
  (`PhysicalSchema.fs:818-838`). **Recommendation:** the `ColumnType` VO from
  finding #2 would single-source this facet list and make the three enumerations
  one. Severity: **Medium** / Confidence: **High**.

#### Additional findings

- **[ALGEBRAIC STRUCTURE] `PhysicalSchemaDiff` is 14 hand-mirrored fields where one
  keyed structure would do** — `PhysicalSchema.fs:269-297` — Seven axes ×
  `Missing`/`Extra` = 14 list fields, with `diff` (758-775), `isEqual` (778-792),
  `isSchemaEqual` (802-812), and `renderDiff` (913-931) each re-enumerating all 14.
  Adding an 8th axis touches 5 sites. A `Map<Axis, (Missing list × Extra list)>` or
  a small `DiffAxis` DU would collapse the four enumerations to folds. Severity:
  **Low-Medium** / Confidence: **High** (the `setDifference`/`countTier`/`block`
  helpers already hint the author felt the repetition).

- **[DENORMALIZATION] `CatalogDiff` C1 channels are four copies of one shape** —
  `CatalogDiff.fs:60-172` — `AttributeDiff`, `ReferenceDiff`, `IndexDiff`,
  `SequenceDiff` are *identical* records
  (`Added:Set<SsKey>; Removed:Set<SsKey>; Renamed:Map<SsKey,RenameRecord>; Changed:'TChange list`),
  and their `between`/`isEmpty`/`changedFacets`/`apply*Diff` functions (lines
  296-464, 707-849) are four near-verbatim copies differing only in the facet
  predicate. This is ~400 LOC that a generic `ChannelDiff<'facet>` + a
  `facetsOf: 'a -> 'a -> Set<'facet>` parameter would reduce to one.
  **Recommendation:** parameterize per the codebase's own A40
  harmonization-via-parameterization discipline. Severity: **Medium** /
  Confidence: **High** (the comment at `:326-331` literally says "Each mirrors
  `attributeDiff` exactly").

- **[TESTABILITY/HAZARD] Default-substitution sites are confined to
  evidence-ingestion, not round-trip — low risk, but two adapter sites under-set**
  — `{ X.create … with … }` reconstruction sites in production:
  `CatalogCodec.fs:753,784,813,846` (JSON round-trip — **all fields threaded
  explicitly**, disciplined); `ReadSide.fs:1310`,
  `CatalogReader.fs:1169,1335,1885,2110` (adapter ingestion — set only a subset,
  *intentionally* defaulting the rest). No silent-default bug found in the
  round-trip path (the one that matters for the codec law). The adapter sites
  silently default e.g. `Index.AllowRowLocks/AllowPageLocks=true`,
  `Reference.IsConstraintTrusted=true` — correct-by-intent for evidence that
  doesn't carry those, but invisible if a future evidence source *does*.
  **Recommendation:** none urgent; the codec discipline holds. Confidence: **High**.

#### Biggest blind spot (synthesis)

The region's real compression debt is a **single column-type concept fractured
across the codebase**: the facet tuple "(Type, Length, Precision, Scale,
IsIdentity, SqlStorage, Default, Computed)" is independently hand-enumerated in
`Attribute` (logical IR), `PhysicalColumn` (physical IR),
`AttributeFacet`/`changedFacets` (diff IR), `SqlStorageType` (type-realization
DU), and `renderColumn` — five surfaces that must be kept in lockstep by
convention, with `SqlStorageType.to/ofPrimitiveType` being a sixth, *dead* copy of
the type-mapping half. Extracting one `ColumnType` value object would normalize
the noun, single-source the facet list, and let `CatalogDiff`'s four-times-
duplicated channel machinery (~400 LOC) collapse under the codebase's own A40
parameterization discipline. The documentation hazard layered on top is that the
canonical Wave-6 docs still assert this cluster has "zero production callers,"
which is false at HEAD — the diff/lifecycle/episode subsystem is fully wired into
MigrationRun, Pipeline, the SSDT emitters, and the CLI.

### 4.3 Pipeline orchestration

*Region: `src/Projection.Pipeline/` (~28 files) and `src/Projection.Cli/`.*

#### Top 5 highest-leverage findings

- **[COMPRESSION] `*Diagnostics.fs` profile-scan family collapses to a rule
  registry** — `FkSelectivityDiagnostics.fs:53`, `JointDependencyDiagnostics.fs:55`,
  `InactiveAttributeDiagnostics.fs:34` — Three of the four diagnostics share a
  near-identical shape: `emit (profile: Profile) : DiagnosticEntry list`, body is
  `profile.<List> |> List.choose (guard → threshold → DiagnosticEntry.create source severity code msg)`.
  The two threshold-based ones (`Fk`, `Joint`) are *structurally identical* —
  distinct-count guard, `sumBy snd` total, ratio computation, threshold compare,
  one entry with `{distinctCount; ratio; isTruncated}` metadata — differing only in
  (field selected, ratio direction, threshold, code/source strings, metadata key
  names). They're invoked by a hand-listed `@`-chain at `Pipeline.fs:873-876`,
  *not* a registry — so adding a fifth diagnostic means editing the chain.
  **Recommendation:** introduce
  `type DiagnosticRule = { Source; Code; Severity; Scan: Profile -> (SsKey * decimal * metadata) list; ... }`
  plus a `registry : DiagnosticRule list` that `Pipeline.fs` folds over; the two
  threshold rules become two records of a parameterized `selectivityRule`. ~3 files
  × ~30 LOC of choose-fold shape + a hardcoded fan-in collapse to a table.
  **Severity: Medium. Confidence: High.** (Caveat: `SpecialCircumstancesDiagnostics`
  is a genuine outlier — it scans `ComposeState`/`Catalog`, not `Profile`, and
  carries acceptance-annotation logic; it should *not* be forced into the same
  registry.)

- **[COMPRESSION] `*Binding.fs` catalog-resolution is copy-pasted across 4 binders**
  — `TighteningBinding.fs:53-76`, `EmissionFoldersBinding.fs:146-165`,
  `SpecialCircumstancesBinding.fs:59-105` — The "resolve operator-supplied
  `LogicalName`/dotted-ref → kind/attribute `SsKey` by scanning
  `catalog.Modules |> List.tryPick … List.tryFind`" logic is reimplemented 5 times
  (2 in `TighteningBinding` for logical+physical, once each in the other three).
  `EmissionFoldersBinding.resolveKindByLogical` even has a docstring admitting
  "*Mirrors `SpecialCircumstancesBinding.resolveKindByLogical`*." The
  error-aggregation `match keyR, targetR with Ok/Error ×4` ladder is also copied
  verbatim in `RenameBinding.fs:48-52`, `EmissionFoldersBinding.fs:173-177`,
  `SpecialCircumstancesBinding.fs:153-161`. **Recommendation:** extract a
  `CatalogResolution` module (`resolveKindByLogical`, `resolveKindByPhysicalTable`,
  `resolveAttributeRef`) into Core or a shared Pipeline module — this is the
  EvidenceCache "discover-once" discipline applied to name resolution, and it would
  also fix the **O(modules×kinds) linear re-scan per entry** (see perf finding). The
  Result-pair-merge ladder collapses to the existing `Result.map2`/applicative. ~5
  resolution copies (~20 LOC each) + ~4 merge ladders → 1 module. **Severity:
  Medium-High. Confidence: High.**

- **[PERFORMANCE] N×M catalog re-scan per binder entry (no index)** —
  `TighteningBinding.fs:54-76`, `EmissionFoldersBinding.fs:151`,
  `SpecialCircumstancesBinding.fs:64,93` — Every
  `resolveAttributeRef`/`resolveKindByLogical` call does a fresh
  `catalog.Modules |> List.tryPick (fun m -> … m.Kinds |> List.tryFind …)` — a full
  linear walk of the module/kind tree (and for attributes, a third nested
  `List.tryFind` over columns). Called once per config entry inside a `List.map`, so
  binding *K* tightening overrides against a *N*-kind catalog is O(K·N·attrs). At the
  documented 300-table production scale with non-trivial override lists this is a
  repeated full-catalog scan in the config-bind hot path. The codebase already has
  the `ColumnsByKey`/index precompute discipline (CLAUDE.md "Big-O audit at
  multiple-derivation sites") — it just wasn't applied here. **Recommendation:**
  build `Map<logicalName,Kind>` / `Map<SsKey,Kind>` / `Map<dottedRef,SsKey>` indices
  *once* per binding pass and pass them to the per-entry resolvers. **Severity:
  Medium. Confidence: High.**

- **[DESIGN/SUITABILITY] `MigrationRun.fs` has 8 overlapping `executeXxx` variants —
  a combinator is latent** — `MigrationRun.fs:375,478,506,536,559,607` (plus
  `preview`/`previewFromStore`) — `execute`, `executeAndMeasureCdc`,
  `executeAndRecord`, `executeFromLive`, `executeWithData`, `executeWithDataAndRecord`
  form a power-set explosion over three orthogonal axes: **{measure-CDC?} ×
  {record-episode?} × {data-load?}** plus a source-resolution axis ({known A | read-
  live A | from-store A}). Each variant re-threads the same
  `task { match … with Error e → return Error e | Ok … }` skeleton and re-brackets
  `cdcCaptureTotal` / `nextCoordinate` / `record`. This is exactly the
  **sibling-wrapper "overdifferentiated middle-tier" anti-pattern named in CLAUDE.md**
  (2^N subset wrappers; principled count is 2). **Recommendation:** one `execute`
  returning the verified `MigrationOutcome`, plus orthogonal post-combinators
  (`measuringCdc cnn`, `recording path …`, `withData …`) that decorate it — the
  operator/CLI composes the legs it wants. Would absorb ~250 LOC of the 646.
  **Severity: Medium. Confidence: Medium-High** (some variants have genuinely
  different gating, e.g. CDC-confounded verification in `executeWithDataAndRecord`,
  so the collapse needs care — not a blind merge).

- **[PERFORMANCE/CORRECTNESS] Sync-over-async `GetAwaiter().GetResult()` pervades the
  boundary — the documented deadlock footgun** — `FullExportRun.fs:223,249`,
  `Deploy.fs:1027`, `Program.fs` ×15 (285, 307, 338, 375, 412, 460, 526, 799, 923,
  982, 1025, 1123, 1145, 1360, 1514) — Every CLI verb and `FullExportRun.execute`
  blocks on a `Task` via `.GetAwaiter().GetResult()`. CLAUDE.md documents this
  codebase's *exact* deadlock history (the `TaskSync.run` `Task.Run`-offload
  mitigation in the test suite, the xUnit bounded-sync-context starvation). In the
  CLI `main` these run on the entry thread with no sync context so they don't
  deadlock *today*, but `FullExportRun.execute` is consumed by both `Program.fs` and
  the test harness (per its own docstring) — a test running it under a bounded sync
  context is the known hang. The pattern is also a blanket suitability smell: 17
  blocking bridges instead of a `task`-returning `main`/handlers with one top-level
  block. **Recommendation:** make handlers `Task`-returning and block exactly once at
  `[<EntryPoint>]`, or route every bridge through the existing `TaskSync.run`.
  **Severity: Medium (latent High if `FullExportRun` is sync-context-hosted).
  Confidence: High.**

#### Additional findings

- **[MAINTAINABILITY] `Config.fs` (1296 LOC) is a parser monolith, but cohesive —
  not a god-object** — `Config.fs:36-1296` — It is ~24 record/DU type defs + ~10
  default values + ~50 `parseX`/`getX` private helpers, all serving one
  responsibility: `JsonElement → Config`. The smell is *length*, not *coupling*: the
  section types (`ModelSection`, `TighteningSection`, `OverridesSection`, …) and
  their parsers could split into per-section partial modules (`Config.Model`,
  `Config.Tightening`, …) co-located with each section's record. Note also
  `scanForCredentials`/`looksLikeCredentialName` (432-460) is a security concern
  living inside the config parser — a candidate to lift to its own module.
  **Recommendation:** mechanical split by section; low risk. **Severity:
  Low-Medium. Confidence: High.**

- **[DESIGN] `Deploy.fs` (1470 LOC) IS a god-object — at least 5 distinct
  responsibilities** — `Deploy.fs` — It bundles: (1) Docker container lifecycle
  (`module Docker`, 76-248), (2) connection-string building (`module ConnectionString`,
  330-375), (3) DB create/drop + batch execution (`executeBatch`, `executeStream`,
  379-870), (4) **server-CPU-probe-driven parallelism autotuning**
  (`detectParallelism`/`resolveParallelism`/`capParallelismToPool` + a module-level
  `parallelismCache` mutable, 459-668), and (5) the wide-canary orchestration
  (`runWideCanary*`, `runFromV1Json`, 1305-1470). These are independently testable
  concerns glued by file boundary only. The `parallelismCache` mutable (459) is the
  one piece of near-module-level mutable state in the region. **Recommendation:**
  split into `Deploy.Container`, `Deploy.Connection`, `Deploy.Execution`,
  `Deploy.Parallelism`, `Deploy.Canary`. **Severity: Medium. Confidence: High.**

- **[SUITABILITY] CLI dispatch is a hand-maintained argv-array `match` with ad-hoc
  flag parsers** — `Program.fs:1520-1657` — Dispatch mixes Argu-driven sub-handlers
  (`dispatchFullExport`, `dispatchTransfer`) with literal-array patterns
  (`[| "emit"; "--config"; configPath |]`) and four repeated inline
  `valueOf`/`multiValueOf` closures (defined fresh at 1530, 1571, 1579, 1595, 1600).
  The `approve` verb alone has 4 explicit array arms enumerating the
  rationale×store option cross-product — itself a small 2^N subset explosion.
  **Recommendation:** one flag-parser helper hoisted to module scope; consider
  migrating the manual arms to Argu sub-commands for uniformity. **Severity:
  Low-Medium. Confidence: High.**

- **[TESTABILITY] Run-family error handling is consistent (Result), but the LogSink
  side-channel couples I/O into the run core** — `FullExportRun.fs:149-211` — The
  Run/Binding families *do* use `Result`/typed-error-DU uniformly (`MigrationError`,
  `FullExportStoreError`, `RunOutcome` — no exception/Result mixing except the
  deliberate `try…with ex → Aborted` boundary at `FullExportRun.fs:197` and
  `MigrationRun.fs:445`, which are correct fail-loud catches). The testability
  friction is different: `executeCore` interleaves `LogSink.emit`/`LogSink.reset`/
  `Bench.reset` (global mutable singleton state) directly into the orchestration, so
  the run can't be exercised without the global sink. This is a known, documented
  seam (the test harness consumes it via `withWriter`), so it's acceptable but worth
  noting as the family's main test-isolation cost. **Severity: Low. Confidence:
  Medium.**

- **[MAINTAINABILITY] `[<Literal>] decimal` cctor-bomb scar tissue is correctly
  avoided but duplicated as prose** — `FkSelectivityDiagnostics.fs:41-45`,
  `JointDependencyDiagnostics.fs:42-46` — Both files carry the identical 5-line
  comment explaining why `decimal` thresholds use `let private` not `[<Literal>]`.
  Harmless, but if the diagnostics collapse to a registry (finding 1) the threshold
  lives in one record and the comment dedupes too. **Severity: Trivial. Confidence:
  High.**

#### Biggest blind spot (synthesis)

The region's parallel-file families are **not uniformly collapsible**, and treating
them as one problem would be the mistake: the `*Diagnostics.fs` and `*Binding.fs`
families share genuine, quantifiable shape (a profile-scan-to-entry registry; a
catalog-resolution-plus-Result-merge module — each ~100 LOC of recoverable
duplication, with one principled outlier per family), but the `*Run.fs` files are
*heterogeneous* — `DriftRun` (34 LOC) and `EjectRun` (65 LOC) are thin and rightly
distinct, while the real compression target hides *inside*
`MigrationRun`/`TransferRun` as a 2^N `executeXxx` wrapper explosion over orthogonal
{measure × record × data-load} axes that the codebase's own sibling-wrapper
discipline already forbids. The actual highest-leverage blind spot, though, is
cross-cutting and invisible at the file-family level: the **repeated linear catalog
re-scan in the binders** violates the codebase's own documented "discover-once /
index-precompute" Big-O discipline at the exact 300-table production scale the
canary is tuned for, and the **17 sync-over-async blocking bridges** sit on top of a
documented deadlock history with only the test suite hardened (`TaskSync.run`) while
the production CLI and the dual-consumer `FullExportRun.execute` remain bare.

### 4.4 Targets / emitters

*Region: all emitter targets under `src/` (SSDT, Json, Data, Distributions,
OperationalDiagnostics).*

#### Top 5 highest-leverage findings

- **[COMPRESSION] The `allKinds → map → Map.ofList → ArtifactByKind.create` scaffold
  is hand-rolled in 6+ emitters** — `JsonEmitter.fs:184-189`,
  `DistributionsEmitter.fs:215-220`, `SsdtDdlEmitter.fs:840-854`,
  `DecisionLogEmitter.fs:127-135`, `DataEmissionComposer.fs:83-88`,
  `RefactorLogEmitter.fs:328-335` — every per-kind Π re-implements the identical
  body:
  `let allKinds = Catalog.allKinds catalog; allKinds |> List.map (fun k -> k.SsKey, render k) |> Map.ofList |> ArtifactByKind.create catalog`.
  `ArtifactByKind` (Core) exposes only `create`/`toMap`/`tryFind`/`keys` — there is
  **no `ArtifactByKind.ofCatalog (render: Kind -> 'e) catalog` combinator** (grep of
  Core confirms zero). This is the single highest-leverage collapse: one combinator
  `Emitter.perKind : (Kind -> 'e) -> Emitter<'e>` (plus a Profile-bearing sibling)
  would erase ~6 copies of the walk, centralize the `Bench.scope`, and make every
  new per-kind Π a one-liner. **Severity: Medium-High. Confidence: High.**

- **[PERFORMANCE / COMPRESSION] `TopologicalOrderPass.runWith` is recomputed
  independently in 8 emitter sites** — `SsdtDdlEmitter.fs:779`,
  `StaticPopulationEmitter.fs:127`, `StaticSeedsEmitter.fs:391`,
  `BootstrapEmitter.fs:118`, `MigrationDependenciesEmitter.fs:434`,
  `DataEmissionComposer.fs:208/297/363`. The Data family already learned this lesson
  once (the composer "hoists" the pass and threads it via `emitWithTopo` per its own
  docstring at lines 25-33), but the hoist is **local to the Data composer** —
  SSDT-DDL and the Data leaf emitters still each run Kahn's algorithm on the full
  catalog. At 300-table production scale a schema+data bundle recomputes the same
  topo sort 3-5×. Worse, there are **two SelfLoopPolicy variants in play**
  (`SkipSelfEdges` for SSDT/StaticPopulation vs `TreatAsCycle` for the Data
  triumvirate), so a naive shared cache must be keyed by policy. Recommendation:
  thread a single `TopologicalOrder` (per policy) from the pipeline/bundle composer
  into every emitter that needs it; the `emitWithTopo` seam already exists on the
  Data side — extend it to SSDT-DDL. **Severity: Medium. Confidence: High.**

- **[ALGEBRAIC STRUCTURE] `ArtifactByKind<'element>` is NOT the uniform emitter
  output — 4 distinct output algebras coexist** — `Emitter<'e>` returns
  `Result<ArtifactByKind<'e>,EmitError>` (Json, Distributions, SSDT-DDL, DecisionLog,
  Data), but **SchemaMigrationEmitter returns `Diagnostics<Statement list>`**
  (`SchemaMigrationEmitter.fs:413`), **ManifestEmitter returns a bare `Manifest`**
  (`ManifestEmitter.fs:959`), **RefactorLogEmitter returns
  `ArtifactByKind<RefactorLogEntry list>` keyed over the *target* catalog not the
  source** (`RefactorLogEmitter.fs:318-335`), and
  **DacpacEmitter/DockerImageEmitter return `Result<byte[]>`/`Result<DockerImageContext>`**
  (`DacpacEmitter.fs:147`, `DockerImageEmitter.fs:279`). The "every Π is a
  `Catalog → ArtifactByKind`" framing in the navigation docs is aspirational; the
  diff-consuming and bundle-consuming emitters break the uniform algebra by design.
  This is partly principled (a `Manifest` is a catalog-wide summary, not a per-kind
  slice; SchemaMigration is a flat ALTER stream), but it means **the T11
  keyset-commutativity theorem only holds for the 5 per-kind emitters** — the
  diff/summary emitters are silently outside it. Recommendation: name the three
  output shapes explicitly (`PerKindArtifact` / `FlatStatementStream` /
  `CatalogSummary`) as a closed taxonomy in Core so the non-uniformity is a
  documented sum type rather than ad-hoc per-file return types. **Severity: Medium.
  Confidence: High.**

- **[COMPRESSION / MAINTAINABILITY] CatalogCodec encodes the round-trip law as 60
  hand-paired write/read functions with NO structural symmetry enforcement** —
  `CatalogCodec.fs` (887 LOC): every IR type has a `wX`/`readX` pair that
  **independently** names the same JSON fields as string literals (e.g., `wAttribute`
  writes `"isPrimaryKey"` at line 305, `readAttribute` reads `"isPrimaryKey"` at line
  737 — the two literals are 432 lines apart and nothing couples them). The
  round-trip law `deserialize (serialize c) = Ok c` is asserted by property test, not
  enforced by construction, so a **rename-drift bug** (rename a field on the write
  side, forget the read side) compiles cleanly and only fails at runtime. The
  `byTag`/`wField`/`wOpt`/`wList`/`field`/`optField`/`listField` helpers already
  abstract the *plumbing*, but the **field-name × codec pairing is duplicated
  field-by-field**. Compounding risk flagged in the file's own comments:
  `readAttribute`/`readIndex`/`readKind` use the `{ X.create … with … }`
  record-update-over-smart-constructor pattern (lines 753, 813, 846) — the CLAUDE.md
  "default-substitution hazard" — where any field `create` doesn't set AND the `with`
  block omits silently inherits a constructor default. Recommendation: a
  `Codec<'a> = { Write: ...; Read: ... }` applicative paired at one site per field
  (codec-combinator style) would make rename-drift a compile error; failing that, a
  totality test that diffs the write-side field set against the read-side field set
  per type. **Severity: Medium-High. Confidence: High.**

- **[DESIGN / NORMALIZATION] Three "describe what changed" emitters overlap but along
  DIFFERENT axes — partial false-overlap** — `SchemaMigrationEmitter` (ALTER
  differential from `CatalogDiff`), `RefactorLogEmitter` (rename channel from
  `CatalogDiff`), and `ManifestEmitter` (coverage/predicate summary from `Catalog`).
  The first two genuinely partition the same `CatalogDiff` and the code already
  documents the partition contract carefully (`SchemaMigrationEmitter.fs:36-42`:
  renames go to RefactorLog, shape changes go to SchemaMigration, "never touch the
  same attribute"). **But there is no shared `CatalogDiff` traversal**:
  SchemaMigration's `foldByKind` (line 417) and RefactorLog's rename walk
  independently enumerate the diff's per-kind maps. ManifestEmitter is genuinely
  different (it summarizes a single catalog, doesn't consume a diff at all) and
  should NOT be merged. Recommendation: extract one `CatalogDiff.foldByKind`
  traversal in Core (the fold at `SchemaMigrationEmitter.fs:417-425` is generic over
  `'d` already) shared by both diff-consuming emitters; leave Manifest separate.
  **Severity: Low-Medium. Confidence: Medium** (the "describe changes" grouping in
  the prompt conflates a diff-emitter pair with a summary-emitter; only the pair
  normalizes).

#### Additional findings

- **[MAINTAINABILITY] The typed-AST discipline IS followed — but ScriptDomBuild ↔
  ScriptDomGenerate is a build/dispatch pair, not a duplication** —
  `ScriptDomBuild.buildStatement` (`ScriptDomBuild.fs:1743`) is a 40-case dispatch
  from the `Statement` DU to ScriptDom typed nodes; `ScriptDomGenerate.toText`
  (`ScriptDomGenerate.fs:168`) is the *only* `StringBuilder` in the SSDT emit path
  and it's the sanctioned terminal realization (build AST → `generateOne` per
  statement → frame with `\n`). There is **no "build the AST" / "generate text from
  AST" duplication** — the generate side delegates entirely to
  `Sql160ScriptGenerator`. The 1783 LOC of ScriptDomBuild is ~50 small `buildX`
  helpers (one per SQL construct), which is inherent to wrapping ScriptDom's verbose
  mutable API, not accidental bloat. **Severity: Low (no action). Confidence: High.**

- **[MAINTAINABILITY] LINT-ALLOW sites are concentrated and substantive, not
  scattered escape hatches** — the typed-AST discipline holds: the only
  `StringBuilder`/`String.Concat`/`String.Join` sites are (a) the sanctioned
  `ScriptDomGenerate.toText` terminal, (b) V1-naming-convention constraint names
  (`SsdtDdlEmitter.fs:204` PK, `:260` FK — pre-unwrapped VOs, defensible), (c)
  cross-platform relative paths (`SsdtDdlEmitter.fs:520`, `ManifestEmitter.fs:645` —
  `Path.Combine` rejected for T1), (d) terminal GO-batch suffixes in the Data
  MERGE/UPDATE emitters (`StaticSeedsEmitter`, `MigrationDependenciesEmitter` —
  file-level `LINT-ALLOW-FILE`), (e) `RefactorLogEmitter`/`SchemaMigrationEmitter`
  file-level XML/SQL terminal markers. **The aggregate that the
  "text-builder-as-first-instinct" discipline warns about (6 LINT-ALLOWs at one
  MERGE site = migration never attempted) does NOT appear** — each is a genuine
  terminal-text boundary. One worth noting: `StaticSeedsEmitter.fs:303` admits "the
  typed `Statement` DU does not yet model UPDATE so `ScriptDomGenerate.toText` is not
  applicable" — the Data UPDATE path bypasses the `Statement` stream and concatenates
  ScriptDom-rendered strings directly, a real (documented) gap in the typed-stream
  algebra. **Severity: Low. Confidence: High.**

- **[TESTABILITY] The non-uniform emitter outputs each invent their own
  "unreachable" escape** — `DistributionsEmitter.emit` (`:266`) and
  `SsdtDdlEmitter.emitSlices` (`:852`) both `invalidOp (sprintf …)` on the
  `ArtifactByKind.create` error / missing-module case. These are the same
  "smart-constructor-can't-fail-here" pattern reimplemented per emitter. A shared
  `ArtifactByKind.createOrInvalidOp` (or having the `perKind` combinator own the
  unreachability) would centralize the defensive boundary. **Severity: Low.
  Confidence: Medium.**

#### Biggest blind spot (synthesis)

The audit prompt frames the emitters as a uniform
`Catalog × Profile → ArtifactByKind<element>` algebra, but the **load-bearing blind
spot is that the uniformity is already broken into three output shapes** (per-kind
artifact, flat statement/byte stream, catalog summary) and the codebase has no
Core-level taxonomy naming that split — so T11 commutativity quietly applies to only
5 of ~11 emitters while the diff-consuming and bundle emitters live outside it
undocumented. The single highest-leverage compression is the missing
`Emitter.perKind` / `ArtifactByKind.ofCatalog` combinator: the identical "walk
allKinds → map → ofList → create" body is copied across 6 files when Core could own
it in 4 lines, and the same omission forces every emitter to re-run
`TopologicalOrderPass` and re-invent its own `invalidOp` unreachability. CatalogCodec
is the quieter time-bomb — 887 LOC of write/read pairs with field names duplicated
across a 400-line gap and a round-trip law enforced only by property test, sitting on
top of the exact `{ create … with … }` default-substitution hazard the project's own
discipline table warns is invisible to the compiler.

### 4.5 Boundary adapters

*Region: `Adapters.Osm/CatalogReader.fs` (2518), `Adapters.Sql/` (ReadSide 1731,
LiveProfiler 1246, EvidenceCache, AsyncStream, …), `Adapters.OssysSql/`
(MetadataSnapshotRunner 1196, …).*

#### Top 5 highest-leverage findings

- **[COMPRESSION/DESIGN] ReadSide bypasses the EvidenceCache discipline entirely —
  15 hand-rolled reader loops** — `Adapters.Sql/ReadSide.fs:225-664` (15×
  `ExecuteReaderAsync`, 19× `while reader.ReadAsync()`, 16× `CreateCommand`) — Each
  of `readColumnRows`, `readIdentityColumns`, `readPrimaryKeys`,
  `readDefaultConstraints`, `readComputedColumns`, `readTriggers`,
  `readCheckConstraints`, `readIndexes`, `readSequences`, `readExtendedProperties`,
  `readForeignKeys` re-implements the *identical*
  `use cmd / set CommandText / ExecuteReaderAsync / while ReadAsync → ResizeArray.Add / List.ofSeq`
  scaffold. `ReadSide.fs` contains **zero references to `EvidenceCache`** — the
  discovery-then-derive cache that was supposed to unify this is used only by
  `LiveProfiler`. The `readResultSet<'T> (name) (reader) (mapper)` combinator in
  `MetadataSnapshotRunner.fs:410` is *exactly* the missing abstraction; ReadSide
  should adopt a `readRows<'T> cmd mapper` materialize combinator collapsing ~400 LOC
  of loop boilerplate to ~11 one-liners. **Severity: High · Confidence: High.**

- **[PERFORMANCE] ReadSide.read has a per-table N+1 row-materialization loop NOT
  covered by any cache** — `ReadSide.fs:1685-1692`
  (`for k in kindsWithRefs do let! rowsOpt = readRows cnn k`) where `readRows`
  (`:973`) itself issues a `SELECT COUNT(*)` (`ExecuteScalarAsync`) *then* a full
  `readRowsStream` per kind → **2N round-trips** at the readback path. This is the
  exact N+1 shape EvidenceCache eliminated for profiling (~6000→~900), but the
  readback path predates/sidesteps it. At 300-table scale this is ~600 extra
  round-trips. The COUNT-then-stream could fold into the stream (read up to maxRows+1,
  decide inline) and the row pull could share LiveProfiler's single-pass
  `discoverKind` substrate. **Severity: High · Confidence: High.**

- **[MAINTAINABILITY/COMPRESSION] CatalogReader is a 2518-LOC God-file running two
  near-duplicated ingestion paths** — `Adapters.Osm/CatalogReader.fs` — **24 `parse*`
  functions** in one module. The JSON path (`parseAttribute :895`,
  `parseReference :1129`, `parseIndex :1227`, `parseKind :1430`, `parseModule :1593`)
  and the rowset path (`parseAttributeRow :1727`, `parseReferenceRowFor :1822`,
  `parseIndexRowFor :2049`, `parseKindRow :2197`, `parseModuleRow :2310`) are
  structurally parallel — both build the *same* IR records via the *same*
  `Name.create`/`SsKey`/`resolveAttributeType`/`propagateOrFallback` helpers (the
  rowset versions' own comments say "Same resolution as the JSON path"). The
  divergence is purely the field-source (JSON `getString` vs typed `row.Field`).
  Candidate normalization: a single record-builder taking a typed field-accessor
  record, with two thin field-extraction front-ends. Decompose into per-concern files
  (`Json/`, `Rowset/`, `TypeTranslation`, `Identity`, `JsonCombinators`). **Severity:
  High · Confidence: High.**

- **[COMPRESSION] The "getProperty → validate → build" JSON shape is a latent
  combinator (~52 call sites)** — `CatalogReader.fs:556-691` — `getString`(14) +
  `getBool`(9) + `getOptionalString`(10) + `getIntFlag`(4) + `getProperty`(4) +
  `getOptionalInt`(4) + `getOptionalBool`(3) + `getOptionalIntFlag`(4) ≈ 52
  invocations feeding **19 multi-Result `| Ok a, Ok b, ... ->` merge blocks** and
  **23 `propagateOrFallback` tails**. This is an applicative-validation pattern
  open-coded by hand. A small `JsonReader` applicative (`req`/`opt` combinators + a
  `<*>`-style merge) would collapse each ~50-line parse block to a flat field list and
  remove the repeated 5-tuple `match … with | Ok,Ok,Ok,Ok,Ok ->` ceremony. The
  codebase already has `Validation` combinators in `Result.fs`; this is the
  boundary-side sibling. **Severity: Medium · Confidence: High.**

- **[ERROR HANDLING] Exception leakage + non-uniform error contracts in the Sql
  adapter; retry applied to only one of three I/O paths** — `ReadSide.fs` has **only
  2 `try` blocks for 15 query sites**: just the top-level `read` (`:1622`) and
  `cdcCaptureCount` partially. The 11 `read*` helpers, `readRows`, and
  `cdcTrackedTables` (`:1568`, returns raw `Task<string list>` — no `Result`) all let
  SqlExceptions propagate; they're caught only by `read`'s single catch-all that
  flattens *every* failure into one opaque `"readside.query.failed"` code (losing
  which query failed). Meanwhile **`Retry.fs` (Polly, transient-SQL classification) is
  referenced only inside `Projection.Adapters.OssysSql`** — `ReadSide` and
  `LiveProfiler` (the canary readback + profiling paths, equally cloud-facing) have
  **no retry at all**. Retry is ad hoc by project, not a shared boundary policy.
  **Severity: High · Confidence: High.**

#### Secondary findings

- **[SUITABILITY/DESIGN] Ordinal-coupled mapper fragility across the rowset contract**
  — `MetadataSnapshotRunner.fs:446-615` — the 23 `mapXRow` functions read by
  hard-coded positional ordinal (`readInt r 0`, `readString r 1`, …) tightly coupled
  to the SELECT column order in the carbon-copied
  `Resources/outsystems_metadata_rowsets.sql` (1184 LOC, byte-identical to V1).
  `ExpectedResultSets = 23` with **17 result sets skipped** via `skipResultSet`. A
  column reorder in the .sql silently corrupts the mapping with no compile-time
  signal. The `// V1 SELECT at outsystems_metadata_rowsets.sql:NNNN` line-citation
  comments are the only linkage. Lower-priority given the SQL is frozen-by-carbon-copy,
  but it's the most brittle seam. **Severity: Medium · Confidence: High.**

- **[DESIGN — positive] The three ingestion paths DO converge correctly** —
  `MetadataSnapshotRunner.toBundle (:954)` → `CatalogReader.RowsetBundle` →
  `CatalogReader.parseRowsetBundle (:2356)`. So OssysSql does *not* rebuild IR
  independently — it reuses CatalogReader's rowset core. The genuine duplication is
  JSON-path-vs-rowset-path *within* CatalogReader (finding 3), and
  ReadSide-vs-LiveProfiler scaffolding (findings 1-2), not a third IR-construction
  core. **Severity: n/a (mitigates concern 6) · Confidence: High.**

- **[SECURITY — positive] No SQL-injection surface** — all dynamic identifier
  interpolation in `ReadSide`/`LiveProfiler` routes through ScriptDom
  `Identifier.EncodeIdentifier` (`ReadSide.fs:868,984`; `LiveProfiler.fs:74`); all
  *values* flow via `SqlParameter`/`AddWithValue` (`MetadataSnapshotRunner.fs:658-671`).
  The 16 `CommandText <-` sites in ReadSide are static literals or pre-encoded
  segments. Parameterization is clean. **Severity: Low (informational) · Confidence:
  High.**

- **[TESTABILITY] Adapters take concrete `SqlConnection`, no seam for the reader loop**
  — every `read*`/`discoverKind` takes `cnn: SqlConnection` directly and inlines the
  loop, so the materialize logic can't be unit-tested without a live DB (hence the
  Docker test pool). Extracting the `readResultSet`-style combinator (finding 1) would
  also make the mapper functions independently testable against a fake reader.
  **Severity: Medium · Confidence: Medium.**

- **[MAINTAINABILITY] Comment-to-code ratio is extreme at the boundary** — many parse
  blocks carry 15-25 line multi-paragraph rationale docstrings (e.g.,
  `CatalogReader.fs:584-600`, `1042-1068`) that the project's own style guide ("No
  multi-paragraph docstrings; no multi-line comment blocks") forbids. Much of this is
  session-narrative ("session-20 placeholder was X") that belongs in DECISIONS, not
  inline. Contributes materially to the 2518 LOC. **Severity: Low · Confidence:
  Medium.**

#### Biggest blind spot (synthesis)

The EvidenceCache "discover-once / derive-in-pure-F#" discipline was applied to the
*profiling* path (`LiveProfiler`) but the *schema-readback* path (`ReadSide`) — which
runs on every canary, the project's primary integration gate — silently kept the
pre-cache architecture: 15 hand-rolled reader loops and a 2N per-table
row-materialization loop, with no shared materialize combinator and no retry, even
though the exact combinator (`readResultSet<'T>`) and retry policy
(`Retry.defaultPipeline`) already exist *in sibling adapter projects*. The team built
the right abstractions but applied each in exactly one of the two or three places it
belongs, so the duplication is invisible from inside any single file and only
surfaces when the three adapters are read side-by-side. The highest-leverage single
move is to extract one boundary kernel — a `readRows<'T> cmd mapper` materialize
combinator plus a shared transient-retry wrapper — and route ReadSide, LiveProfiler,
and MetadataSnapshotRunner through it, which simultaneously collapses ~400 LOC, closes
the retry gap, and gives the row-read loop a cache substrate to share.

### 4.6 Test suite

*Scope verified: 244 test files, 63,260 LOC. ~3.0x `Projection.Core` (20,913) and
~1.19x all of `src` (53,083). Suite-wide: **2,928 `[<Fact>]` vs 168 `[<Property>]`**
(17:1 example:property), 41 files use properties, **195 files are example-only**.*

#### Top 5 highest-leverage findings

- **[COMPRESSION] `mustOk`/`mkName`/`mkKey` re-defined in dozens of files — the
  two-consumer rule never fired** — `tests/Projection.Tests/*` (e.g.
  `DataEmissionComposerTests.fs:32`, `StaticSeedsEmitterTests.fs:25`,
  `ReconciliationTests.fs:10`) — `let mustOk` is redefined in **63 files**, `mkName`
  in **47 files**, `mkKey` in **31 files** (often the byte-identical body
  `SsKey.synthesizedComposite "OS_TEST" parts |> mustOk`). A canonical
  `testKey`/`mustOk` already exists in `Fixtures.fs:62` and `Fixtures.fs:221`, so the
  shared home exists and is simply under-adopted. Concrete fix: promote `mustOk`,
  `mkName`, the `(string list -> SsKey)` `mkKey`, and the `(string -> SsKey)` variant
  into `IRBuilders.fs`/`Fixtures.fs` and delete the 60+ local copies — this alone
  removes ~300–500 boilerplate LOC and makes constructor-signature changes a one-file
  edit. **Severity: High. Confidence: High.**

- **[COVERAGE] Genuinely untested Core modules (zero symbol references in any test)**
  — `src/Projection.Core/Optics.fs` (154 LOC), `StructuredString.fs` (115),
  `LineageBuffer.fs` (92), `PinnedWriting.fs` (62), `SchemaComplexityMetrics.fs` (24),
  `QueryHints.fs` (11), plus `Adapters.Sql/SqlPolicy.fs` (56) and
  `Pipeline/InactiveAttributeDiagnostics.fs` (48, 3 real functions, named after a
  user-facing diagnostic) — these names appear in **0** test files (verified by symbol
  grep, not just filename match). Most are small/pure (Optics, StructuredString,
  LineageBuffer carry real logic worth a property test each). Concrete fix: add a
  focused test file for `Optics`, `StructuredString`, `LineageBuffer`, and
  `InactiveAttributeDiagnostics`; the trivial ones (`QueryHints`,
  `SchemaComplexityMetrics`) can be left or covered transitively. **Severity:
  Med-High. Confidence: High** (note: the raw filename-match list of ~50 "untested"
  modules is misleading — CatalogReader, ReadSide, ScriptDomBuild, etc. are covered
  under differently-named integration/differential test files; the 8 above are the
  real gaps).

- **[COVERAGE] AxiomTests.fs has a 28% skip ratio — claimed-but-untested axioms** —
  `tests/Projection.Tests/AxiomTests.fs` — of **120 `[<Fact>]`, 34 are
  `[<Fact(Skip=...)>]`** (28%) with only **1 `[<Property>]`**. Bucket mentions: A=37,
  B=25, **C=13, D=5**. So ~18 axioms sit in Bucket C/D (weakly-covered or unnamed-gap)
  and a quarter of the runnable catalog is a skip-stub asserting nothing. The
  Verified:Convention:Skip shape is roughly **37 : 25 : 34**. The stubs are
  well-documented (each Skip string names the promotion path), so this is honest, not
  deceptive — but a runner that's 28% no-ops gives false comfort. Concrete fix: track
  the Skip count as a CI metric with a ratchet (cannot increase), and prioritize
  promoting the 5 Bucket-D entries (A27 pointer-swap atomicity, A21 refresh
  idempotence) since those are genuine untested invariants. **Severity: Med.
  Confidence: High.**

- **[COMPRESSION] Large example files dominated by setup, not assertion** —
  `tests/Projection.Tests/OsmRowsetReaderTests.fs` (1,371 LOC) — 30 facts but only
  **112 assertion lines (~8%)** against **81 setup `let`-binding lines** plus inline
  literal rowset fixtures; the file is mostly hand-rolled row/column scaffolding that
  an `IRBuilders`-style rowset builder would absorb. Same shape in
  `MigrationCanaryTests.fs` (1,197 LOC, with `mkKey` re-defined **three times**
  internally at lines 22/677/748). Concrete fix: extract a `mkRowset`/`mkOsmRow`
  builder into shared fixtures; target the ~33 files using triple-quoted literal
  fixtures. **Severity: Med. Confidence: Med** (assertion-line heuristic is
  approximate).

- **[FRAGILITY] Golden full-output string assertions concentrated in emitters** —
  **33 test files assert against triple-quoted literal SQL/DDL blocks**, but only
  **14 files apply `.Replace()` whitespace normalization** — so ~19 files do
  exact-match on serialized output that breaks on any rename, reformat, or whitespace
  change in the emitter. `SsdtDdlEmitterTests.fs` (1,202 LOC) and
  `StaticSeedsEmitterTests.fs` are the highest-exposure. Concrete fix: route golden
  comparisons through a shared normalizer (collapse whitespace, sort independent
  statements) so tests assert behavior (correct DDL semantics) rather than byte-exact
  rendering. **Severity: Med. Confidence: Med.**

#### Secondary findings

- **[COMPRESSION] Property-candidate example clusters** —
  `tests/Projection.Tests/ForeignKeyRulesTests.fs` (25 facts, 1 property): inspected
  and these are **legitimately distinct decision-table branches** (each a different
  `EnforceConstraint`/`DoNotEnforce` outcome variant), so this is a healthy
  example-table, NOT bloat — a counter-signal worth noting. By contrast, files like
  `IsActiveCarryThroughTests.fs` (397 LOC, 9 facts, **0 properties**) enumerate
  carry-through across attribute permutations and are stronger candidates for collapse
  into one FsCheck property over attribute lists. **Severity: Low. Confidence: Med.**

- **[COMPRESSION] `mkCatalog`/`mkConfig`/`entry` partial duplication** — `mkCatalog`
  redefined in 10 files (a shared `mkCatalog` already exists at `IRBuilders.fs:50`, so
  the 10 locals are drift), `mkConfig` in 8, `entry` in 21. Lower individual leverage
  than `mustOk`/`mkKey` but same root cause. **Severity: Low. Confidence: High.**

- **[MAINTAINABILITY] Shared-fixture surface is thin relative to demand** —
  `IRBuilders.fs` is only **51 LOC** exposing essentially `mkModule` + `mkCatalog`;
  `Fixtures.fs` is 276 LOC. Given 63K LOC of consumers redefining the same 4–5 helpers
  hundreds of times, the shared layer is under-built — the abstraction exists but stops
  one level too shallow (no shared key/name/rowset/config builders). **Severity: Med.
  Confidence: High.**

#### Biggest blind spot (synthesis)

The single largest issue is **boilerplate decentralization, not missing coverage**:
the suite already has excellent disciplines (decision-table example sets like
ForeignKeyRules, FsCheck properties, the axiom catalog), but its own two-consumer
collapse rule was never applied to test infrastructure — `mustOk` lives in 63 files,
`mkName` in 47, `mkKey` in 31, while the shared `IRBuilders.fs` that should hold them
is a thin 51 LOC. The true coverage gaps are narrow and identifiable (8
zero-referenced Core modules + 34 skip-stubs = 28% of the axiom runner asserting
nothing), so confidence is high that the 3x-Core LOC is *partly* genuine behavioral
coverage but materially inflated by copy-pasted setup that a fuller shared fixture
layer would compress by an estimated 5–10%. Fix the fixture centralization first
(cheap, mechanical, high-leverage), then close the
InactiveAttributeDiagnostics/Optics/StructuredString gaps and ratchet the AxiomTests
skip count.

### 4.7 Documentation corpus

*Scope measured: 105 markdown files, ~5.7 MB at root — the doc corpus now exceeds the
code surface.*

#### Top 5 highest-leverage findings

- **[DRIFT — CRITICAL] "Zero production callers" claim is now FALSE in code** —
  `WAVE_6_MORPHOLOGY.md:98`, `AUDIT_2026_06_02_FSHARP_EIGHT_AXIS_REDTEAM.md:389`,
  `CLAUDE.md`, `DECISIONS.md:20369`,
  `THE_USE_CASE_ONTOLOGY.{acceptance,fitness,obligations}.md` — These docs assert the
  diff/`Lifecycle` machinery has "zero production callers… amino acids with no
  protein." But `Lifecycle.fs` is referenced by **5 non-test src files**
  (`Program.fs`, `Pipeline.fs`, `MigrationRun.fs`, `EjectRun.fs`, `LifecycleStore.fs`),
  and `between`/`applyDiff`/`compose` have ~218 non-definition call sites in `src/`.
  The ontology files even contradict the audits internally (claiming it's "resolved at
  the composition level" while audits still say "dark"). **Recommendation:** state this
  fact in ONE place (the ontology acceptance matrix), date-stamp it, and have all
  others link to it. Sev: **High** / Conf: **High** / Reclaim: removes a load-bearing
  false claim from ~7 files.

- **[DRIFT — HIGH] Axiom count disagrees across the index layer** — `AXIOMS.md:24`
  prose says "**A1–A42**" but the file actually contains **A43**; `PRODUCT_AXIOMS.md`
  max is A42; `README.md` still says **A41**; `CLAUDE.md` reaches A43. Three different
  "current counts" depending on which index you open. **Recommendation:** AXIOMS.md is
  the single source; README/PRODUCT_AXIOMS/CLAUDE should cite a count, not restate it.
  Sev: **High** / Conf: **High** / Reclaim: drift-removal, ~small bytes but high
  trust-cost.

- **[DRIFT — HIGH] Test-count claims are stale by ~3-4x and mutually inconsistent** —
  across the corpus: "**882 passing**", "**790 tests**", "**631 passing**", "**588
  tests**" all appear as if current. Actual code has **2,729 `[<Fact>]`/`[<Theory>]`
  attributes**. Every cited baseline is a historical snapshot frozen into prose.
  **Recommendation:** ban absolute test counts from prose entirely; reference "see test
  run" or a generated badge. Sev: **High** / Conf: **High** / Reclaim: eliminates a
  whole class of guaranteed-stale numbers.

- **[COMPRESSION — HIGH] 73 sediment files = ~1.96 MB (34% of corpus) belong in
  `archive/`** — all `CHAPTER_*_OPEN/CLOSE.md`, `HANDOFF_CHAPTER_{1,2,3,C}.md`
  (`HANDOFF_CHAPTER_3` alone is **280 KB**), `*PRESCOPE*.md`, `WAVE_6_*.md`. These are
  closed-chapter historical records. They are not redundant *with each other*, but they
  are pure sediment on the working surface. **Recommendation:** `git mv` to `archive/`
  (or `chapters/`); leave a one-line pointer in the ontology's provenance §6. This
  nearly halves the root working surface with **zero information loss**. Sev: **High** /
  Conf: **High** / Reclaim: **~1.96 MB / 34%** off the active surface.

- **[NAVIGABILITY — HIGH] Index-of-indexes: 3 docs each claim to be the entry point** —
  `CLAUDE.md` ("the single index"), `THE_USE_CASE_ONTOLOGY.md` ("this is the index"),
  `KICKOFF.md` ("Read this first"); and **36 of 105 files** contain "read first / index
  / start here / canonical entry" language. The self-declared normalization (ontology
  subsumes NORTH_STAR/VISION/WAVE_6/SPINE → "provenance") **added a 6th index layer
  rather than collapsing the existing ones** — and the subsumed docs all still sit
  full-size at root (NORTH_STAR 28 KB, VISION 47 KB, SPINE 38 KB, WAVE_6 trio ~100 KB,
  PRODUCT_AXIOMS 45 KB). **Recommendation:** designate exactly ONE root entry (CLAUDE.md
  → ontology), strip "index/read-first" language from the other 34 files, and physically
  move the "now-provenance" docs into `archive/`. Sev: **High** / Conf: **High** /
  Reclaim: navigability + ~260 KB movable.

#### Additional findings

- **[NORMALIZATION — MED] DECISIONS.md (1.47 MB) violates its own discipline** —
  `DECISIONS.md:4007/4023` codifies "DECISIONS is for resolved questions, **not session
  narrative**… would this still be useful in six months?" Yet the file has **263
  session-date headers**, **847 narrative markers** (session/today/we-then/handoff), and
  **421 lines** matching the file's own forbidden categories (rent-paying /
  forward-signal / test-baseline / recap / "this session"). The discipline exists but is
  not enforced. **Recommendation:** one-time prune pass deleting
  forward-signals/baselines/recaps per the file's own substance test. Sev: **Med** /
  Conf: **High** / Reclaim: est. **20-30% of 1.47 MB ≈ 300-440 KB**.

- **[REDUNDANCY — MED] Load-bearing-commitments list duplicated across 16 files** —
  `CLAUDE.md`, `HANDOFF_CHAPTER_3.md`, `KICKOFF.md`, `PLAYBOOK.md`, `EXECUTION_PLAN.md`,
  `STAGING.md`, `V2_DRIVER.md`, `README.md`, etc. (the docs admit it lives in "CLAUDE.md
  AND HANDOFF.md"). Same for the **eight-promise covenant (8 files)** and the
  **verifiability triangle (7 files)**. **Recommendation:** canonicalize each list in
  one home; replace the other 7-15 copies with a link. Sev: **Med** / Conf: **High** /
  Reclaim: tens of KB plus drift-prevention.

- **[COMPRESSION — MED] Redundant restatement ratio across CLOSE/HANDOFF/AUDIT/VISION_REVIEW**
  — these genres each re-narrate the same chapter's outcome (the OPEN states intent, the
  CLOSE restates it as done, the HANDOFF restates the CLOSE, the AUDIT re-examines it,
  VISION_REVIEW re-examines the VISION). Sampling indicates **~40-55% restatement** of
  facts already fixed elsewhere. Sev: **Med** / Conf: **Med**.

#### Biggest blind spot (synthesis)

The project built a sophisticated *self-described* normalization layer
(THE_USE_CASE_ONTOLOGY declaring six predecessor docs "provenance"), but it **added a
layer instead of collapsing one** — the subsumed docs remain full-size and
root-resident, and three separate files still each claim to be "the index," so a new
reader has *more* entry points than before, not fewer. The deeper blind spot is that the
corpus freezes **point-in-time numeric claims** (test counts, axiom counts, "zero
callers") into permanent prose; every one spot-checked has since drifted from the code —
the "zero production callers" assertion is now flatly false, which is dangerous because
it's load-bearing for the project's central "amino-acids-with-no-protein" narrative. The
discipline to prevent exactly this ("DECISIONS is for resolved questions, not session
narrative") is *written down* but *not enforced*, so 1.47 MB of append-only log keeps
accreting session sediment.

**Headline number: roughly 45-55% of the doc corpus (~2.7-3.0 MB of 5.7 MB) is
compressible without information loss** — ~34% via archiving closed-chapter sediment,
plus ~300-440 KB of prunable DECISIONS narrative, plus de-duplication of the repeated
commitment/promise/triangle lists and stale numeric claims.

---

## 5. Suggested sequencing

1. **Fix Tier-1 bugs** (SchemaComplexity topology threading; `pickLabel` tie-break;
   ReadSide error/retry) — small, high-value, and #1 is a silently-wrong metric.
2. **Extract three kernels**, each closing a half-applied discipline:
   `Emitter.perKind` / `ArtifactByKind.ofCatalog`; an `Analytics.emit` pass epilogue; a
   `readRows<'T>` materialize combinator (the last also gives ReadSide a cache substrate
   + retry seam). Highest compression-per-risk.
3. **The `ColumnType` VO** — biggest structural normalization; unlocks the `CatalogDiff`
   channel collapse (~400 LOC) under the existing A40 discipline.
4. **Test-fixture centralization** — cheapest mechanical win (~300-500 LOC; future
   constructor changes become one-file edits).
5. **Quarantine speculative algebra** (`Prism`/`PassContext`/`LineageTree`/`&&&` →
   `Algebra.Speculative`) + name the emitter-output taxonomy
   (`PerKindArtifact`/`FlatStatementStream`/`CatalogSummary`).
6. **Doc compression** — archive ~1.96 MB of closed-chapter sediment; prune
   ~300-440 KB of DECISIONS narrative; fix the three stale numeric claims (zero-callers,
   axiom count, test counts) and designate one entry point.

Each item is independently shippable and verifiable against the existing test suite.

---

## 6. Methodology notes & caveats

- **Seven agents, regionally scoped, blind to each other.** Convergence on the unifying
  thesis (§1) is therefore independent corroboration, not echo.
- **Cross-region claims were independently grep-verified by the lead** (§2) before being
  promoted into the synthesis — in particular the four dead-algebra surfaces and the
  now-false "zero production callers" claim.
- **Counter-signals are retained deliberately.** Several "large file" suspicions were
  *cleared* by the agents (`ScriptDomBuild` is an inherent ScriptDom wrapper, not bloat;
  `Config.fs` is cohesive-not-coupled; `ForeignKeyRulesTests` is a healthy decision-table,
  not redundant enumeration; the LINT-ALLOW aggregate the discipline warns about does
  *not* appear; SQL parameterization is clean). Acting on a "compress everything" reading
  of this audit would damage those.
- **This is a findings document, not a change.** No source was modified. Severity and
  confidence are the agents' own calibrations.
