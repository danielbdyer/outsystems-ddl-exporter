# BACKLOG — Operational backlog for V2 cutover (Bridge-integrated)

**Status:** re-canonicalized 2026-05-16 (Bridge wave). Supersedes the
prior "forwarding pointer to V2_DRIVER.md" framing. The operational
backlog now lives here as a sibling to V2_DRIVER.md, interweaving the
V2-driver KPI's per-phase chapter sequence with the Bridge wave's
gradient transitions, cross-cutting infrastructure work, V1-side
adoption opportunities, and risk register.

**Strategic relationship:**

- **`V2_DRIVER.md`** is the strategic destination — the *why* the
  cutover ladder bends toward V2-driver mode, the per-axis correctness
  stakes, the chapter ownership map. Slowest-rhythm strategic surface
  after the manifesto.
- **`BACKLOG.md`** (this document) is the operational ledger — *what
  is in flight, what is scheduled, what is blocked, what is shipped,
  and what is sunset*. Refreshed at every chapter close and at every
  Bridge method gradient transition.
- **`CSHARP_FSHARP_MANIFESTO.md`** is the architectural canonical —
  *how* the C#/F# partition and the Bridge wave's commitments work in
  practice. Cited by section number from backlog items.
- Per-chapter pre-scope documents (`CHAPTER_<N>_PRESCOPE_*.md`,
  `CHAPTER_<N>_OPEN.md`) carry slice-level scope. The backlog
  cross-references; it does not duplicate.
- **`ADMIRE.md`** carries per-V1-component placement records. The
  backlog cites ADMIRE entries by section header.
- **`DECISIONS.md`** is the append-only resolved-questions log. The
  backlog cites DECISIONS by date + title.

This is the single document a contributor reads when they want to know
*what's next* in the V2 effort. The strategic *why* is in V2_DRIVER;
the architectural *how* is in the manifesto; the operational *what
and when* is here.

---

## Table of contents

- [I. Frame and operating principles](#i-frame-and-operating-principles)
- [II. Status vocabulary](#ii-status-vocabulary)
- [III. Master Bridge-methods table](#iii-master-bridge-methods-table)
- [IV. Per-phase backlog](#iv-per-phase-backlog)
  - [Phase 0.5 — Bridge bring-up](#phase-05--bridge-bring-up-prerequisite-for-phase-3-4-5-parallelization)
  - [Phase 1 — Π port keystone](#phase-1--π-port-keystone-chapter-35)
  - [Phase 2 — Schema-as-driver](#phase-2--schema-as-driver-chapter-41a)
  - [Phase 3 — Data-as-driver](#phase-3--data-as-driver-chapter-41b-parallelizes-after-05)
  - [Phase 4 — Identity-as-driver](#phase-4--identity-as-driver-chapter-42-parallelizes-after-05)
  - [Phase 5 — Operational diagnostics](#phase-5--operational-diagnostics-chapter-43-parallelizes-after-05)
  - [Phase 6 — DACPAC](#phase-6--dacpac-chapter-3x-conditional)
  - [Phase 7 — SnapshotRowsets](#phase-7--snapshotrowsets-chapter-32-closed)
  - [Phase 8 — Pragmatic close + sunset](#phase-8--pragmatic-close--sunset)
- [V. Cross-cutting infrastructure work](#v-cross-cutting-infrastructure-work-horizontal)
- [VI. V1-side adoption opportunities](#vi-v1-side-adoption-opportunities)
- [VII. Risk register](#vii-risk-register-wave-wide)
- [VIII. Sequencing graph](#viii-sequencing-graph)
- [IX. Cutover+30 gate condition (formalized)](#ix-cutover30-gate-condition-formalized)
- [X. Lifecycle protocol and ownership](#x-lifecycle-protocol-and-ownership)
- [XI. Cross-references](#xi-cross-references)

---

## I. Frame and operating principles

The backlog interweaves five dimensions in one document. Each dimension
has its own structure, but each phase brings them together:

1. **Strategic chapters** (sourced from V2_DRIVER.md's chapter
   sequencing under V2-driver KPI). Each phase has a chapter
   identifier, scope statement, and gate criteria.
2. **Bridge methods** (the unit of inheritance under the gradient).
   Each anticipated method has its V1 source citation, current state,
   declared target state, frequency / determinism class, gating
   equivalence witness.
3. **Chapter slices** (the unit of execution at session cadence).
   Each pending slice has its scope, deliverables, witnesses, and
   dependencies.
4. **Cross-cutting infrastructure** (the horizontal work that supports
   multiple chapters). Wall analyzer rules, manifest test growth,
   equivalence-test fixture growth, F# consumer helpers, documentation
   deltas.
5. **V1-side adoptions** (the V2-for-V1 capabilities V1 might consume
   during dual-track). Each has a V1 consumer site, the V2 capability,
   the demonstrated or plausible benefit, and the configuration-flag
   shape.

Plus the wave-wide risk register and the sequencing graph that ties
everything together.

**Operating principles:**

- **Append-mostly.** Items move through statuses; items rarely
  disappear. Cancellation requires a DECISIONS entry citing the
  rationale. Removal without rationale is forbidden.
- **Cross-references over duplication.** The backlog's job is to
  surface what's in flight, what's blocked, what's done — not to
  restate the canonical commitments. When an item references a chapter
  pre-scope or a DECISIONS entry or a manifesto section, the
  reference is the substantive content; the backlog row is the index.
- **Codify every transition.** When a Bridge method moves from
  `Delegated` to `Vendored`, the chapter that performs the transition
  cites the slice that did it. When an item moves from `scheduled` to
  `in-flight`, the commit that started it is named. The backlog is
  the operational ledger, not a snapshot.
- **Witness every claim.** Items declare exit criteria; the witnesses
  (property tests, equivalence relations, manifest tests, analyzer
  rules) are named. An item without a witness cannot move to
  `shipped`.
- **Per-phase reviews at chapter close.** Every chapter close walks
  this document, updates statuses, surfaces silent drift, and refreshes
  the cross-references. The chapter-close ritual (`DECISIONS
  2026-05-14`) gains a backlog-review dimension.

---

## II. Status vocabulary

Backlog items move through this fixed status set. Status transitions
are documented in commits; the transition itself is small (one line
change) but the audit trail compounds.

| Status | Meaning |
|---|---|
| `proposed` | Listed for consideration; no chapter has committed to it; can be discussed and refined without consequence |
| `scheduled` | A chapter has committed to it; the chapter's open or pre-scope document references it; estimated effort acknowledged |
| `blocked` | Scheduled but cannot start; the blocker is named (often another item by row index) |
| `in-flight` | Active work in the current session(s); the relevant commits are accumulating; the witness is not yet green |
| `shipped` | Work complete; witness green; the chapter close ritual has reviewed it |
| `sunset` | Item's purpose has ended (V1 sunset, deprecated capability, structural retirement); item retained for historical citation |
| `canceled` | Item explicitly canceled with DECISIONS rationale; item retained for historical citation |

A `blocked` item moves to `scheduled` when the blocker resolves; the
blocker reference is preserved as a citation but the status changes.

A `shipped` item rarely changes; if a regression breaks it, a new item
is added (not a status revert), citing the regression.

The `sunset` status is structurally important. It is the cutover+30
terminal state for every Bridge method (once `Current = Target` and
the trunk reference is gone). It is also the natural lifecycle of
V2-for-V1 capabilities once V1 retires.

---

## III. Master Bridge-methods table

One row per anticipated Bridge method (across both `Projection.Bridge.Core`
and `Projection.Bridge.Runtime`). The table is the canonical inventory
of the inheritance surface; the per-phase sections below cross-reference
this table when listing the methods a chapter consumes.

**V1→V2 inheritance methods (`Projection.Bridge.Core/Capabilities/`):**

| # | Method | V1 Source | Chapter | Current → Target | Frequency | Determinism | Status |
|---|---|---|---|---|---|---|---|
| 1 | `ExtractMetadataAsync` (Catalog/) | `Osm.Pipeline.Application.ExtractModelApplicationService.RunAsync` | 0.5 | Delegated → RefinedInPlace | OneShot | Deterministic | scheduled (slice γ; later refinement chapter) |
| 2 | `BuildProfileQueriesAsync` (Profile/) | `Osm.Pipeline.Profiling.{NullCountQueryBuilder,UniqueCandidateQueryBuilder,ForeignKeyProbeQueryBuilder}` | 4.x | Delegated → Vendored | OneShot | Deterministic | proposed (chapter to be named when profile querying needs Bridge surface) |
| 3 | `RenderSsdtAsync` (Smo/) | `Osm.Smo.SmoEntityEmitter` | 3.x (DACPAC chapter) | Delegated → RefinedInPlace | PerTable | Deterministic | proposed (conditional on Phase 6 opening; current SSDT path is ScriptDom-direct so Bridge surface only opens if DACPAC parity demands it) |
| 4 | `RenderDacpacAsync` (Smo/) | `Osm.Smo.DacpacBuilder.CreateDacpac` (V1 has DacFx wrapper) | 3.x | Delegated → RefinedInPlace | OneShot | Deterministic | proposed (Phase 6; conditional on deploy path) |
| 5 | `CompareWithDmmAsync` (Dmm/) | `Osm.Dmm.DmmComparator` + lenses | Later | Delegated → RefinedInPlace | OneShot | Deterministic | proposed (no current chapter demand; defer per IR-grows-under-evidence) |
| 6 | `ParseRefactorLogAsync` (Refactor/) | `Osm.Emission.RefactorLogReader` (or in-Bridge XML parser) | 3.5 (closed) or later | n/a | OneShot | Deterministic | sunset (chapter 3.5 closed without Bridge; F# port is canonical) |
| 7 | `LoadOverrideBindingsAsync` (Overrides/) | `Osm.Pipeline.Application.NamingOverridesBinder` | Later | n/a (V2 rebuilds fresh in F#) | OneShot | Deterministic | canceled (V1's seven scattered override-binding mechanisms are explicitly NOT inherited per manifesto § XII; V2 has its own structured builder) |
| 8 | `MatchUsersAsync` (Users/) | `Osm.Pipeline.UatUsers.UserMatchingEngine.Execute` | 4.2 | Delegated → TranslatedToFSharp | PerTable (batched) | Deterministic | scheduled (chapter 4.2; small algorithm; F# closed-DU strategy DU beats C# rewrite) |
| 9 | `GenerateMergeInsertAsync` (Data/) | `Osm.Emission.PhasedDynamicEntityInsertGenerator` (MERGE generation) | 4.1.B | Delegated → Vendored | PerTable | Deterministic | scheduled (chapter 4.1.B slices δ-θ) |
| 10 | `ReadCdcRowsAsync` (Cdc/) | `Osm.Pipeline.Cdc.DbTableChangeHelper` | 4.1.B | Delegated → TranslatedToFSharp | PerRow → IAsyncEnumerable | NonDeterministic | scheduled (chapter 4.1.B; PerRow constraint forces streaming; F# adapter via Projection.Adapters.Sql.AsyncStream may be simpler than vendoring) |
| 11 | `DetectStaticSeedCyclesAsync` (Data/) | `Osm.Emission.Seeds.EntityDependencySorter` cycle-resolution heuristics | 4.1.B | n/a | OneShot | Deterministic | canceled (V2 already has `TopologicalOrderPass.SelfLoopPolicy` via A40 harmonization; lifting would break harmonization-via-parameterization per manifesto § XII and DECISIONS 2026-05-30) |
| 12 | `AggregateUniqueIndexEvidenceAsync` (Profile/) | `Osm.Validation.Tightening.UniqueIndexEvidenceAggregator` | Later (consumer-dependent) | Delegated → Vendored → RefinedInPlace | OneShot | Deterministic | scheduled (the only one of three V1 evaluators with clean SPLIT seam per chapter 0.5 close ADMIRE corrections; minor refactor of V1's enforce*Unique policy gates) |

**V2→V1 inheritance methods (`Projection.Bridge.Runtime/Capabilities/V2ForV1/`):**

| # | Method | V2 Source | Chapter | Current → Target | Demonstrated Consumer | Status |
|---|---|---|---|---|---|---|
| R1 | `InvokeV2TopologicalOrderAsync` | `Projection.Core.Passes.TopologicalOrderPass` | 0.5 | Delegated → RefinedInPlace | YES — V1's `BuildSsdtStaticSeedStep.cs:80-90` cross-category FK-order bug | scheduled (slice η) |
| R2 | `InvokeV2FlattenDiagnosticsAsync` | `Projection.Core.Diagnostics` | 4.3 (conditional) | n/a | PLAUSIBLE — V1 operator dashboard if scoped | proposed (defer until V1 dashboard consumer materializes; chapter 4.3 close re-evaluates) |
| R3 | `InvokeV2RenderAsync` | `Projection.Targets.SSDT.SsdtDdlEmitter` (rendered) | Future | n/a | NO DEMONSTRATED CONSUMER | proposed (deferred per IR-grows-under-evidence; no V1 site requests V2-render for verification) |
| R4 | `InvokeV2FlattenLineageAsync` | `Projection.Core.Lineage` | Future | n/a | NO DEMONSTRATED CONSUMER | proposed (deferred; V1 has no lineage trail concept; no manifest/audit consumer for V2 lineage) |

**Methods are added only when a chapter demands them.** The table grows
under evidence, not under speculation. The `proposed` entries above
(esp. items 2, 3, 4, 5; R2, R3, R4) sit on the table to make the
field of options visible to future agents; they do not commit V2 to
shipping them.

**Methods are canceled with explicit DECISIONS rationale.** Items 7
and 11 above are canceled because the editorial discipline judges
they should not be inherited (V1 mental-model trap; V2 has a better
harmonization). The cancellations are codified in DECISIONS and cited
here.

**The total inheritance surface across the wave** is ~8 V1→V2 methods
shipped (items 1, 2, 8, 9, 10, 12 plus possibly 3, 4) and 1-2 V2→V1
methods shipped (R1; R2 conditional). The asymmetric volume reflects
the truth of the inheritance: V1 contributes a lot to V2; V2
contributes a smaller set back to V1 during dual-track.

---

## IV. Per-phase backlog

Each phase mirrors V2_DRIVER's chapter sequencing, expanded with the
Bridge wave's transitions, slices, cross-cutting work, V1-side
adoption opportunities, and per-phase risks. Phases that have shipped
are preserved for historical reference; phases in flight or pending
carry the operational detail.

### Phase 0.5 — Bridge bring-up (prerequisite for Phase 3, 4, 5 parallelization)

**Status:** in-flight (slice α audit primitives + scaffold shipped
2026-05-16; slices β-η pending).

**Strategic frame.** The V1↔V2 seam transitions from data boundary to
inheritance phylogeny. Two new C# projects (`Projection.Bridge.Core`,
`Projection.Bridge.Runtime`) make the inheritance machinery structural;
F# adapters consume `Bridge.Core` to inherit V1 capabilities under the
four-position gradient (`Delegated` → `Vendored` → `RefinedInPlace` →
`TranslatedToFSharp`). The reflection-scanned `BridgeManifest` is the
auditable witness; the `Projection000BridgeWallDiscipline` analyzer
enforces the eight wall rules structurally. See
`CHAPTER_0_5_OPEN.md` for the seven-slice arc;
`CSHARP_FSHARP_MANIFESTO.md` § VI–X for the canonical architecture;
`DECISIONS 2026-05-16 — Bridge wave: V2 inherits from V1` for the
codifying entry.

**Pending slices:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| α | Audit primitives (`BridgeMethodAttribute`, `BridgeManifest`, `SunsetDisposition`, `Determinism`, `Frequency`, `BridgeResult<T>`, `BridgeError`); `BridgeManifestTests` with three Facts | manifest test deterministic + well-formed; sunset gate `[Fact(Skip)]` reserved | shipped (2026-05-16 commit `48c8c27`) |
| β | Wall discipline analyzer (`Projection000BridgeWallDiscipline` in `Projection.Analyzers`); eight rules; negative-test fixtures per rule | analyzer fires on each deliberate violation; clean on all current Bridge code | scheduled (next slice after α; F# Analyzers SDK precedent `NoUnsafeTimeInCoreAnalyzer.fs`) |
| γ | First inheritance: `ExtractMetadataAsync` with `Current = Delegated, Target = RefinedInPlace`; method delegates to V1's `ExtractModelApplicationService`; Wire records (rowset shape, not OsmModel aggregate root); BCL-typed input `ExtractMetadataInput` | method compiles; F# consumer in slice δ exercises it | scheduled (depends on β for analyzer enforcement) |
| δ | F# consumption: `Projection.Adapters.Osm.CatalogReader.SnapshotSource.LiveOssysViaBridge` variant; `bundleOfBridgeOutput` translation function (~30 lines); `parseRowsetBundle` reused unchanged via A40 harmonization | F# adapter compiles; smoke test produces non-empty Catalog | scheduled (depends on γ) |
| ε | First adoption: V1 source files (`MetadataSnapshotRunner.cs`, `SnapshotJsonBuilder.cs`, result-set processor chain) copied into `Projection.Bridge.Core/Adopted/Catalog/`, namespaced `Projection.Bridge.Adopted.Catalog`; `ExtractMetadataAsync` body switches to local copy; `[BridgeMethod].Current` transitions to `Vendored` | adoption test asserts vendored implementation byte-equivalent to delegated; commits show V1 trunk unchanged | scheduled (depends on δ + ζ) |
| ζ | Equivalence witness: `tests/Projection.Bridge.Tests/Fixtures/bridge-ossys-seed.sql` + paired `BridgeFixtures.canonicalBundle` F# literal + `CatalogEquivalence.normalizeForEquivalence` (quotient over 6 collection-ordering axes; preserve 5 semantic axes) + property test `equivalent (parse (SnapshotRowsets canonicalBundle)) (parse (LiveOssysViaBridge input))` | property test green on canonical fixture; CatalogDiff output empty modulo normalization | scheduled (depends on δ; load-bearing structural witness of entire wave) |
| η | V2-for-V1 first method: `InvokeV2TopologicalOrderAsync` in `Projection.Bridge.Runtime/Capabilities/V2ForV1/`; paired V1 test demonstrating cross-category static-seed FK-order bug closing at `BuildSsdtStaticSeedStep.cs:80-90` | V1 test currently failing on cross-category fixture; passes with `InvokeV2TopologicalOrderAsync` consumed | scheduled (depends on α; independent of γ-ζ chain) |
| θ | Documentation deltas: ADMIRE re-classification corrections (NullabilityEvaluator + ForeignKeyEvaluator revert to PURE PASS; UniqueIndexEvidenceAggregator SPLIT confirmed; OSSYS catalog producer gradient); HANDOFF entry; chapter close DECISIONS entry codifying AM-1..AM-7 + A41 Bridge clause + A42 candidate | docs consistent; cross-references resolve | partially shipped (2026-05-16 commit `48c8c27` + `3f8d16d`); chapter close ritual completes at slice η close |

**Bridge methods landed in this phase:**

- Item 1 (`ExtractMetadataAsync`) at slice γ, advancing to `Vendored`
  at slice ε.
- Item R1 (`InvokeV2TopologicalOrderAsync`) at slice η.

**Cross-cutting work in this phase:**

- Bridge wall analyzer (slice β; the substrate that makes every
  subsequent slice's audit metadata structurally enforceable).
- `BridgeManifestTests` (shipped at slice α; grows as Bridge methods
  land).
- Equivalence fixture for OSSYS path (slice ζ; the canonical
  reference for the load-bearing equivalence witness).
- `Projection.Bridge.Tests` project (shipped at slice α).

**V1-side adoption opportunity landed in this phase:**

- Item R1 (`InvokeV2TopologicalOrderAsync`) lands at slice η. V1
  adopts via configuration flag enabling `BuildSsdtStaticSeedStep` to
  call into Bridge.Runtime. V1's existing sort logic is preserved;
  the V2 sort is opt-in. The adoption is V1's choice on V1's rhythm.

**Per-phase risks:**

- *Cycle violation* if any F# project ProjectReferences Bridge.Runtime.
  Mitigation: documented in `CSHARP_FSHARP_MANIFESTO.md` § VII; the
  Bridge.Core / Bridge.Runtime split makes this structural; reviewer
  catches at PR.
- *Analyzer false positives* during slice β as the wall rules land.
  Mitigation: negative-test fixtures per rule; analyzer ships with
  the fixtures so the failure modes are demonstrated structurally.
- *Equivalence-test brittleness* on V1 SQL's implicit row ordering.
  Mitigation: `CatalogEquivalence.normalizeForEquivalence` quotients
  out six collection-ordering axes per the equivalence-test spec
  (slice ζ); preserves five semantic axes that must match.
- *V1 trunk source modification* by accident. Mitigation: the
  Bridge wave's commit boundaries hold inside `sidecar/projection/`
  per manifesto § III; reviewer catches any commit touching
  `src/Osm.*` under the wave's authority.

**Exit criteria.** Slices β-η green; manifest well-formed across
shipped Bridge methods; equivalence test green; ADMIRE re-classification
corrections landed; V1's static-seed FK-order bug closed via opt-in
flag; documentation deltas consistent. Chapter close ritual completes
with a backlog refresh.

**Sequencing rationale.** Phase 0.5 is the prerequisite for Phases 3,
4, and 5 to parallelize. Without the Bridge substrate (analyzer +
manifest + first vendoring + equivalence witness), those phases
cannot consume V1 capabilities through Bridge; they would have to
rebuild from scratch. Chapter 0.5's bring-up is small (~1 session)
but unblocks ~6 weeks of parallel chapter work.

---

### Phase 1 — Π port keystone (chapter 3.5)

**Status:** substantially shipped. Chapter 3.5 closed 2026-05-09;
`Projection.Targets.SSDT.SsdtDdlEmitter`, JSON emitter,
Distributions emitter all surface typed `seq<Statement>` per A35;
RefactorLog round-trip + CatalogDiff smart constructor shipped.

**Strategic frame.** See V2_DRIVER.md Phase 1 + `CHAPTER_3_5_CLOSE.md`.
Π's output is a deterministic statement stream (A35); realization
layers (`Render.toText`, `Deploy.executeStream`) consume the stream
and choose their emission form; the algebra holds at the stream level.

**Bridge dependencies:** none. Chapter 3.5's emitters use ScriptDom's
typed AST directly (the gold-standard library per pillar 7);
Bridge surface would add no value and would violate the
text-builder-as-first-instinct discipline if it duplicated ScriptDom.

**Cross-cutting work:** none Bridge-related; chapter 3.5's
contributions to the codebase (typed Statement DU, sibling-Π
commutativity T11 structurally encoded via `ArtifactByKind<'element>`,
writer-fidelity discipline) are inherited by subsequent chapters
without modification.

**Pending items (chapter 3.5 follow-ons):** none open. Items the
chapter cashed (A35, A36, A38, T11) are recorded in AXIOMS.md.

**Per-phase risks (residual):**

- *Sibling-Π drift* — if a future emitter slips out of the
  `seq<Statement>` shape and reverts to ad-hoc string rendering. Caught
  by structural-type encoding (T11) at compile time + parse-roundtrip
  property tests at runtime. No active mitigation needed; the
  structural witness holds.

---

### Phase 2 — Schema-as-driver (chapter 4.1.A)

**Status:** substantially shipped (chapter 4.1.A close 2026-05-10).
SsdtDdlEmitter for production SSDT DDL emission; ManifestEmitter;
SsdtBundle composition; Tolerance taxonomy (M4); multi-environment
property test.

**Strategic frame.** V2_DRIVER.md Phase 2 + `CHAPTER_4_1_A_CLOSE.md`.
Production schema axis. The CDC-silence property test (Phase 3)
depends on this phase's SSDT emission being deterministic; Phase 4
(UserFkReflow) depends on this phase's Reference IR shape.

**Pending slices (chapter 4.1.A close arc):**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| 6 | Cross-module FK IR refinement | property test asserts cross-module FK fixtures round-trip | deferred per Active deferrals index (`DECISIONS 2026-05-19`; trigger condition unchanged) |
| 7-default | `Attribute.Default : SqlLiteral option` + DEFAULT constraint emission | round-trip property | deferred per Active deferrals index (`DECISIONS 2026-05-11`; SnapshotRowsets adapter surface required first) |
| 8 | `Kind.Description` + `Attribute.Description` + extended-properties emission | round-trip + per-property test | deferred per Active deferrals index (`DECISIONS 2026-05-11`; same trigger as slice 7-default) |

**Bridge dependencies:** none. Chapter 4.1.A's SsdtDdlEmitter consumes
ScriptDom directly. The pending slices (cross-module FK, defaults,
descriptions) are IR-shape concerns that don't lift via Bridge — V2's
Catalog reconstruction from rowsets is the source of truth.

**Cross-cutting work:** Tolerance taxonomy (codified at chapter
4.1.A close); future emitter Tolerance entries cite this surface.

**Per-phase risks:**

- *Cross-module FK fixture surfacing the IR gap* — when chapter 4.1.A
  slice 6 trigger fires (a fixture exercises a cross-module FK at the
  emit layer that topological ordering cannot satisfy), the IR
  refinement opens as a discrete slice. No date-driven action needed.

---

### Phase 3 — Data-as-driver (chapter 4.1.B; PARALLELIZES after Phase 0.5)

**Status:** α/β/γ shipped (chapter 4.1.B close 2026-05-11);
StaticSeedsEmitter v0, Profile.CdcAwareness + change-detection MERGE,
CDC-silence-on-idempotent-redeploy canary GREEN. Slices δ-θ pending.

**Strategic frame.** V2_DRIVER.md Phase 3. The CDC-silence property
test is the highest-leverage single deliverable in the entire chapter
sequence. Asserts zero records in `cdc.change_tables` after second
deploy on every CDC-tracked table at operator-reality canary scale.
See `CHAPTER_4_1_B_CLOSE.md` for shipped slices; `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md`
for full pre-scope.

**Pending slices:**

| Slice | Scope | Bridge dependency | Witness | Status |
|---|---|---|---|---|
| δ | Two-phase insertion / DeferredFkSet refinement | item 9 (`GenerateMergeInsertAsync`) | property test asserts FK cycle resolution preserves order | scheduled (parallelizes after Phase 0.5 close) |
| ε | MigrationDependenciesEmitter typed-AST adoption (full UPDATE shape) | none (typed-AST direct) | round-trip; idempotent-redeploy | scheduled |
| ζ | BootstrapEmitter typed-AST adoption | none (typed-AST direct) | round-trip; UserRemapContext slot wiring | scheduled (depends on Phase 4 for UserRemapContext) |
| η | Partition assertion + per-partition CDC discovery | item 10 (`ReadCdcRowsAsync`) | property test asserts partition coverage | scheduled |
| θ | Full data triumvirate close + chapter close ritual | n/a | every Bridge method's gradient transition documented | scheduled |

**Bridge methods consumed in this phase:**

- Item 9 (`GenerateMergeInsertAsync`) at slice δ, `Delegated`. Target
  `Vendored` at later refinement (V1's MERGE generation is correct
  and worth vendoring). Frequency: PerTable. Cross-target dep on
  `Projection.Targets.SSDT` per pre-scope §6.
- Item 10 (`ReadCdcRowsAsync`) at slice η, `Delegated`. Target
  `TranslatedToFSharp` (PerRow constraint forces `IAsyncEnumerable<T>`
  shape; the F# rewrite via `Projection.Adapters.Sql.AsyncStream`
  may be simpler than vendoring V1's row-by-row code). Determinism:
  NonDeterministic (V1 CDC reads are not byte-deterministic on row
  ordering; the analyzer flags downstream T1 claims accordingly).
- Item 11 (`DetectStaticSeedCyclesAsync`) is canceled. V2's existing
  `TopologicalOrderPass.SelfLoopPolicy` (A40 harmonization, chapter
  3.1 contribution) absorbs the cycle-resolution capability via
  parameterization; lifting V1's heuristic via Bridge would break
  harmonization-via-parameterization per manifesto § XII.

**Cross-cutting work in this phase:**

- CDC test fixture (per chapter pre-scope) — adds CDC-aware rows to
  the canary fixture so the CDC-silence property has real coverage.
- Data-path equivalence test — extends slice ζ's pattern to cover
  the Bridge-supplied MERGE generation; structurally similar to
  the OSSYS equivalence test.
- `BridgeWire.fromResult` helpers for `Projection.Targets.Data` F#
  consumers (one helper per consumer site, per the F# consumption
  pattern).

**Per-phase risks:**

- *CDC-silence property failing on real production fixtures* — the
  highest-stakes risk of the entire cutover. Mitigation: chapter
  4.1.B slice θ runs the property at operator-reality canary scale
  (50K rows × 300 tables) per the perf-gate baseline. Disagreement
  blocks PR.
- *PerRow marshaling cost on `ReadCdcRowsAsync`* — Bridge.Core
  marshaling at million-row scale could exceed perf-gate baseline by
  10x if signature is wrong. Mitigation: the analyzer's frequency-
  shape contract rejects `Task<T>` per-call; structural enforcement.
  If V1's source cannot be reshaped, drop from lift list per the
  prior performance-analysis agent's recommendation; F# rewrite
  via `Projection.Adapters.Sql.AsyncStream` is the alternative.

**Parallelization note.** This phase parallelizes with Phases 4 and 5
after Phase 0.5 closes. Estimated ~2-3 weeks at session cadence for
slices δ-θ (V2_DRIVER estimate, refined under Bridge wave's LOC
savings to ~1.5-2 weeks).

---

### Phase 4 — Identity-as-driver (chapter 4.2; PARALLELIZES after Phase 0.5)

**Status:** not started. Pre-scope at `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md`.
Phase 4 of V2-driver KPI critical path.

**Strategic frame.** V2_DRIVER.md Phase 4. User FK reflow across
environments. Every CreatedBy/UpdatedBy FK in target environment
must resolve to a valid target User. Per-strategy property tests
cover ByEmail, BySsKey, ManualOverride, FallbackToSystemUser.

**Pending slices:** TBD per chapter open (the pre-scope names α-η; the
open document at chapter 4.2 open lists them).

**Bridge methods consumed in this phase:**

- Item 8 (`MatchUsersAsync`) at chapter open, `Delegated`. Target
  `TranslatedToFSharp` at chapter close (the user-matching algorithm
  is small enough and F# closed-DU strategy DU is a clear win over
  C# rewrite). Frequency: PerTable (batched). The Bridge method's
  lifespan is one chapter; its purpose is to make the inheritance
  auditable and the F# rewrite verifiable via the equivalence test.

**Cross-cutting work in this phase:**

- User-FK equivalence fixture — covers ByEmail / BySsKey / ManualOverride
  / FallbackToSystemUser strategies and the disjointness invariant
  on `UserRemapContext.Mapping.Keys ∩ Unmatched = ∅`.
- Multi-environment property test (T11 specialization) — same source
  population + ByEmail strategy against four distinct target
  populations yields four `UserRemapContext` values whose source-keyset
  agrees across all four; smart-constructor invariant holds for each;
  per-environment differences live entirely in `TargetUserId` values.

**Per-phase risks:**

- *User-matching strategy DU exhaustiveness* — a future strategy
  (e.g., fuzzy-name matching) lands but the closed DU prevents
  silent skip. Mitigation: F#'s exhaustiveness checking; the strategy
  DU's expansion is empirically tested per `DECISIONS 2026-05-13`
  closed-DU expansion discipline.
- *Per-strategy property test coverage gap* — a strategy's edge case
  not covered by property tests. Mitigation: each strategy ships
  with its own property test; chapter close audits coverage.

**Parallelization note.** Parallelizes with Phases 3 and 5 after Phase
0.5 closes. Estimated ~1-1.5 weeks at session cadence (V2_DRIVER
estimate, refined under Bridge wave's F# rewrite scope to ~1 week —
`MatchUsersAsync` is one Bridge method targeting `TranslatedToFSharp`
within the chapter).

---

### Phase 5 — Operational diagnostics (chapter 4.3; PARALLELIZES after Phase 0.5)

**Status:** not started. Pre-scope at `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md` Part 1.
Phase 5 of V2-driver KPI critical path.

**Strategic frame.** V2_DRIVER.md Phase 5. Three-channel Diagnostics
split (DecisionLogEmitter / OpportunitiesEmitter / ValidationsEmitter)
routing via Code-prefix table. The three V1 artifacts (`decision-log.json`,
`opportunities.json`, `validations.json`) ARE the three channels;
routing happens at emit time. See `CHAPTER_4_3_OPEN.md`.

**Pending slices:** TBD per chapter open.

**Bridge methods consumed in this phase:** none (pure V2 algebra).
The three emitters consume `Diagnostics<T>` directly via the existing
writer monad.

**V2-for-V1 opportunity:** item R2 (`InvokeV2FlattenDiagnosticsAsync`)
is plausible but defers — V1's operator-facing diagnostic dashboard
is not currently scoped. If a dashboard consumer materializes during
chapter 4.3 (e.g., a slice exposes the diagnostic stream to an
operator UI), the Bridge.Runtime method ships at that slice's
landing. Otherwise the V2-for-V1 surface for diagnostics remains
proposed-only.

**Cross-cutting work in this phase:**

- Per-channel routing property test — `Code` prefix → channel
  mapping is exhaustive; every diagnostic lands in exactly one
  channel.
- Lineage trail audit completes for at least one full pass cycle at
  canary scale (the chapter gate).

**Per-phase risks:**

- *Routing correctness regression* — a future diagnostic emission
  uses a `Code` prefix not in the routing table, leading to silent
  drop. Mitigation: exhaustiveness property test asserts every
  emitted `Code` matches a prefix; missing-prefix emissions fail
  the test.

**Parallelization note.** Parallelizes with Phases 3 and 4 after Phase
0.5 closes. Estimated ~1.5-2 weeks at session cadence (V2_DRIVER
estimate, unchanged under Bridge wave — chapter 4.3 has no Bridge
methods).

---

### Phase 6 — DACPAC (chapter 3.x; conditional)

**Status:** not started; deploy-path-conditional. If operator's deploy
path requires DACPAC, this phase opens; otherwise defers indefinitely.

**Strategic frame.** V2_DRIVER.md Phase 6. DacFx adoption +
DacpacEmitter; A37 named erasure axes; T1 binary-normal-form amendment;
T11 sibling-Π commutativity at the structural level between SSDT and
DACPAC. See `CHAPTER_3_X_OPEN.md`.

**Bridge methods consumed (if pursued):**

- Item 4 (`RenderDacpacAsync`) — V1's `DacpacBuilder.CreateDacpac`
  via Bridge if a faithful inheritance is preferred over an F# rewrite.
  Target `RefinedInPlace` (DacFx is C#-idiomatic; F# rewrite would
  be expensive without value). Alternative: F# `Projection.Targets.SSDT.DacpacEmitter`
  uses `Microsoft.SqlServer.Dac` directly per the existing chapter
  3.x pre-scope's wrapper pattern. The choice between Bridge-lift
  and F# direct-wrap is determined at chapter open by the operator's
  DACPAC fidelity requirements.

- Item 3 (`RenderSsdtAsync`) — V1's `SmoEntityEmitter` via Bridge if
  DACPAC parity requires SMO-style emission. Otherwise V2's existing
  SsdtDdlEmitter via ScriptDom is canonical.

**Cross-cutting work in this phase:**

- A37 named erasure axes — Tolerance entries naming what DACPAC
  erases (index names, default constraint names, check constraint
  names) so the structural witness between SSDT and DACPAC holds
  modulo named tolerances.
- T1 binary-normal-form amendment — DACPAC binary representation
  IS the normal form for byte-equality at the package level.

**Per-phase risks:**

- *SMO lifecycle wrapping complexity* — if Bridge lifts V1's SMO
  emission, the IDisposable lifecycle (`Server` connection,
  `ScriptingOptions`, `Scripter`) must stay scoped per call without
  leaking. Mitigation: manifesto § VI codifies the discipline; the
  wall analyzer's "no `IDisposable` returned" rule enforces.
- *DacFx version compatibility* — V1 may pin a DacFx version V2
  cannot adopt without conflict. Mitigation: build/dependency
  feasibility audit (prior agent's output) confirmed buildability;
  pinning resolved.

**Conditional opening.** If the operator's deploy path is SSDT-direct,
this phase defers indefinitely per V2_DRIVER. If DACPAC is required,
this phase opens with ~2.5 weeks at session cadence (V2_DRIVER
estimate).

---

### Phase 7 — SnapshotRowsets (chapter 3.2; closed)

**Status:** shipped (chapter 3.2 close 2026-05-10). Five slices
shipped: SnapshotRowsets variant + RowsetBundle; reference rowsets;
EspaceKind activation; IsSystemEntity → ModalityMark.SystemOwned;
cross-source parity. A1's JSON-projection-lossiness bound structurally
resolved.

**Strategic frame.** V2_DRIVER.md Phase 7. See `CHAPTER_3_2_CLOSE.md`.

**Bridge integration.** The Bridge wave's `LiveOssysViaBridge` variant
(chapter 0.5 slice δ) is the third source variant of `SnapshotSource`,
sibling to `SnapshotJson` and `SnapshotRowsets`. The Bridge variant
calls Bridge.Core's `ExtractMetadataAsync`, which delegates (initially)
to V1's full extraction chain — including the SQL extraction that
produces V1's rowsets. The Bridge variant and `SnapshotRowsets`
variant cross-verify each other via the equivalence property test
at slice ζ.

**Pending items (chapter 3.2 follow-ons):** none open. Future class
members (per-table column structure rowset 6; check constraints
rowset 7; triggers rowset 18) remain deferred per Active deferrals
index until fixture pressure surfaces them.

**Per-phase risks:** none active.

---

### Phase 8 — Pragmatic close + sunset

**Status:** indefinite cadence; consumer-pressure-driven.

**Strategic frame.** V2_DRIVER.md Phase 8 + `CHAPTER_5_OPEN.md`.
F# Analyzers SDK custom analyzers; Coordinates Stage 2 typed VOs;
operator runbook; V1 sunset planning.

**Pending items:**

| Slice | Scope | Status |
|---|---|---|
| ν | F# Analyzers SDK custom analyzer (proof-of-concept rule) — the Bridge wall analyzer (chapter 0.5 slice β) generalizes the pattern; slice ν cashes the generalization | scheduled (after chapter 0.5 closes; the wall analyzer is the canonical example) |
| θ | Coordinates Stage 2 typed `SchemaName` / `TableName` / `ColumnName` VOs | deferred (per `DECISIONS 2026-05-11`; empirical adapter-ripple justification required) |
| Chapter 6 | Bridge inventory + sunset operationalization | scheduled (cutover-15 to cutover+30) |
| Operator runbook | Cutover-day procedures; V1 sunset checklist; per-method Bridge sunset disposition | scheduled (cutover-30 onward) |

**Bridge methods relevant to this phase:**

The cutover+30 sunset gate (Chapter 6) asserts every Bridge method's
`Current = Target`. The gate's structural witness is
`BridgeManifestSunsetGateTest`. Phase 8 includes the per-method
sunset rehoming work: each Bridge method's vendored copy under
`Adopted/` either stays (target `RefinedInPlace`), gets ported to F#
(target `TranslatedToFSharp` triggers F# port), or remains delegated
(if the method's chapter has not yet shipped — must be resolved
before cutover+30).

**Cross-cutting work:**

- Operator runbook section for Bridge sunset — names each Bridge
  method, its sunset disposition, the dependency on V1's csproj
  reference removal, the consumer's adoption status.
- `BridgeManifestSunsetGateTest` activation (currently
  `[Fact(Skip = "Active at cutover+30 chapter close")]`; flips at
  Chapter 6 open).

**V1-side sunset.** Per manifesto § XVII, V1 sunset is a condition,
not a deadline. The four conditions: (1) V2 emissions in every
environment for one full schema-evolution cycle without canary
divergence; (2) every Bridge method's `Current = Target`; (3) V1
csproj references removed from `Projection.Bridge.Core.csproj`; (4)
operator sign-off. When all four hold, V1 sunset begins administratively.

**Per-phase risks:**

- *Sunset gate failing at cutover+30* — a Bridge method has not
  reached its target state because the chapter that owned the
  transition slipped. Mitigation: the gradient transition is a
  chapter-level commitment; chapter close cannot complete with the
  transition unfinished; the structural commitment compounds across
  chapters.
- *V1 csproj reference lingering* — a Bridge method delegating to V1
  has not been vendored. Mitigation: the analyzer at sunset-gate
  activation enforces; the test surfaces remaining references.

---

## V. Cross-cutting infrastructure work (horizontal)

Items that span multiple chapters; tracked here so the horizontal
visibility holds.

**Wall analyzer rules** (`Projection000BridgeWallDiscipline` in
`Projection.Analyzers`; lands at chapter 0.5 slice β):

| Rule | Scope | Status |
|---|---|---|
| 1 | BCL types only across the wall | scheduled (slice β) |
| 2 | Capability-shaped (verbs, not nouns) | scheduled (slice β) |
| 3 | V2 vocabulary in record names | scheduled (slice β) |
| 4 | `CancellationToken` at every public entry | scheduled (slice β) |
| 5 | Never throws across the wall | scheduled (slice β) |
| 6 | One public method per file | scheduled (slice β) |
| 7 | Frequency-shape contract | scheduled (slice β) |
| 8 | `[BridgeMethod]` attribute required | scheduled (slice β) |

Each rule ships with a negative-test fixture demonstrating the rule
fires. The analyzer ships as the second analyzer in
`Projection.Analyzers` (after `NoUnsafeTimeInCoreAnalyzer.fs`).

**Manifest test growth** (`BridgeManifestTests` in
`Projection.Bridge.Tests`):

| Test | Scope | Status |
|---|---|---|
| Deterministic ordering | Manifest scan is idempotent | shipped (slice α) |
| Well-formedness | Every required field populated; AddedDate parseable; Current ≤ Target | shipped (slice α) |
| Sunset gate `[Fact(Skip)]` | Reserved for cutover+30 activation | shipped (slice α; flipped at Chapter 6 open) |
| Per-method audit completeness | New Bridge methods land with all seven attribute fields | grows as methods land |
| Coverage audit | Every public Bridge method has at least one F# consumer or a `proposed/scheduled` chapter | scheduled (chapter close ritual) |

**Equivalence-test fixture growth:**

| Fixture | Scope | Status |
|---|---|---|
| `bridge-ossys-seed.sql` + `BridgeFixtures.canonicalBundle` | OSSYS path equivalence (chapter 0.5 slice ζ) | scheduled (load-bearing structural witness) |
| Data-path equivalence | MERGE generation, CDC reads (chapter 4.1.B slices δ, η) | scheduled |
| User-FK equivalence | UserMatchingStrategy fixtures across four strategies (chapter 4.2) | scheduled |
| DACPAC equivalence (conditional) | SSDT vs DACPAC modulo A37 erasure axes (chapter 3.x) | conditional |

**F# consumer helpers** (`BridgeWire.fromResult` and similar adapter
patterns):

| Helper | Scope | Status |
|---|---|---|
| `BridgeWire.fromResult` in `Projection.Adapters.Osm` | Translates `BridgeResult<T>` to `Result<'a, ValidationError list>` for the OSSYS adapter consumer | scheduled (chapter 0.5 slice δ) |
| `BridgeWire.fromResult` in `Projection.Targets.Data` | Translates for the data emitters | scheduled (chapter 4.1.B slice δ) |
| `BridgeWire.fromResult` in `Projection.Adapters.Sql` | Translates for CDC reads | scheduled (chapter 4.1.B slice η) |
| Generalization of the helper into `Projection.Bridge.Core` | If three F# consumers use identical translation, extract per two-consumer threshold | proposed (deferred per `DECISIONS 2026-05-13`) |

**Documentation deltas at every chapter close:**

| Document | Update | Cadence |
|---|---|---|
| `ADMIRE.md` | Per-V1-component entry's Current/Target gradient pair refreshed at every chapter close that touches the component | every chapter close |
| `HANDOFF.md` | Top entry refreshed at every chapter close | every chapter close |
| `DECISIONS.md` | Codifying entry for any new discipline; gradient transitions cited | every chapter close |
| `BACKLOG.md` (this document) | Status transitions; new items added; canceled items annotated | every chapter close + every significant slice |
| `V2_DRIVER.md` | Chapter sequencing updates if a phase's scope shifts | rare (only on strategic reframe) |
| `CSHARP_FSHARP_MANIFESTO.md` | Updated only when architecture's deep premises change | rare |

---

## VI. V1-side adoption opportunities

V2-for-V1 capabilities (Bridge.Runtime methods) V1 might consume during
dual-track. Each opportunity names the V1 consumer site, the V2
capability, the benefit, the configuration-flag shape for adoption, and
the status.

### V1.1 Static-seed FK-order bug (chapter 0.5 slice η; SHIPPING)

- **V1 consumer site:** `src/Osm.Pipeline/Orchestration/BuildSsdtStaticSeedStep.cs:82-86`
- **Bridge.Runtime method:** R1, `InvokeV2TopologicalOrderAsync`
- **Benefit:** V1's current sort orders within static category only;
  cross-category dependencies violate FK constraints. V2's
  `TopologicalOrderPass.SelfLoopPolicy` provides globally-correct
  topological order.
- **Adoption shape:** V1 configuration flag `OSM_USE_V2_TOPOLOGICAL_ORDER`
  (or similar; V1 maintainers choose the flag name). When set, V1's
  static-seed step calls `InvokeV2TopologicalOrderAsync` and uses the
  returned order. When unset, V1's existing sort logic runs.
- **V1 maintainer authority:** the adoption is V1's choice on V1's
  rhythm. V2 ships the V2-for-V1 capability; V1 chooses whether and
  when to enable.
- **Status:** scheduled (chapter 0.5 slice η; lands with the V1 paired
  test demonstrating bug closing on cross-category fixture).

### V1.2 Operator-facing diagnostic dashboard (PLAUSIBLE; deferred)

- **V1 consumer site:** none currently scoped. Plausible site:
  `src/Osm.Pipeline/SqlExtraction/SqlMetadataDiagnosticsWriter.cs` +
  `src/Osm.Pipeline/Orchestration/BasicDataIntegrityChecker.cs` —
  both emit `ValidationError[]` without severity classification.
- **Bridge.Runtime method:** R2, `InvokeV2FlattenDiagnosticsAsync`
- **Benefit:** V2's `Diagnostics<T>` provides structured severity +
  lineage + source attribution; V1's flat `ValidationError[]` could
  be elevated to V2-shaped findings.
- **Adoption shape:** V1 dashboard consumer (if scoped) calls
  `InvokeV2FlattenDiagnosticsAsync` and renders the returned
  `IReadOnlyList<Finding>` with severity-aware UI.
- **Trigger condition:** V1 operator-facing diagnostic dashboard
  scoped during chapter 4.3 (Phase 5) or after.
- **Status:** proposed (deferred per IR-grows-under-evidence).

### V1.3 V2 SSDT render for parity verification (NO DEMONSTRATED CONSUMER)

- **V1 consumer site:** would be `SsdtEmitter` with a parity-comparison
  hook; no such hook exists today.
- **Bridge.Runtime method:** R3, `InvokeV2RenderAsync`
- **Benefit:** V1 maintainers could compare V1's SSDT emission to
  V2's typed-Statement-rendered output for verification during
  dual-track.
- **Status:** proposed (deferred; no V1 site requests V2-render for
  verification).

### V1.4 V2 Lineage trail for V1 manifest (NO DEMONSTRATED CONSUMER)

- **V1 consumer site:** would be `ManifestBuilder.cs`; V1's manifest
  emits coverage summaries, not pass-decision provenance. No site
  consumes lineage.
- **Bridge.Runtime method:** R4, `InvokeV2FlattenLineageAsync`
- **Benefit:** V1's manifest could include V2's pass-decision audit
  trail for cross-version identity tracking.
- **Status:** proposed (deferred; V1 has no lineage trail concept).

### V1-side authority and adoption rhythm

Per manifesto § XXI, V1 maintainers retain full authority over V1.
The V2-for-V1 capabilities are *opt-in offers*; V1 maintainers choose
whether and when to adopt each. V2 will not modify V1's trunk source
under any circumstance to force adoption. If V1 maintainers prefer to
keep V1's existing logic across all sites (e.g., keep V1's static-seed
sort even though V2's is correct), that's V1's call; V2 records the
opportunity here but does not press.

V1-side adoptions are reversible. If V1 enables the configuration flag
for `InvokeV2TopologicalOrderAsync` and later finds an edge case where
V2's order is wrong, V1 turns the flag off and reverts to the existing
logic. The opt-in shape protects V1's stability.

---

## VII. Risk register (wave-wide)

Bridge-specific risks that span the wave. Per-phase risks are listed
in each phase's section above; this register catches the horizontal
risks.

### R-W-1: Cycle violation (F# project ProjectReferences Bridge.Runtime)

- **Failure mode:** F# adapter or pipeline component ProjectReferences
  `Projection.Bridge.Runtime`, creating cycle Bridge.Runtime →
  Projection.Pipeline → Projection.Adapters → Bridge.Runtime.
- **Early-warning signal:** `dotnet build` fails with cycle error;
  CI catches at PR.
- **Mitigation:** Manifesto § VII codifies the discipline. Cycle is
  prevented by the architectural commitment, not by build-system
  magic. Contributors who add a new F# project asserting a need for
  Bridge.Runtime must re-route the consumer through Bridge.Core or
  accept that the V2-for-V1 capability belongs on the Bridge.Runtime
  side of the partition.
- **Owner:** Bridge wave architect (chapter agents); reviewer catches
  at PR.
- **Status:** active (no incidents; mitigation structural).

### R-W-2: Sunset failure at cutover+30

- **Failure mode:** A Bridge method has not reached its declared
  target state because the chapter that owned the transition slipped.
  `BridgeManifestSunsetGateTest` fails at Chapter 6 open.
- **Early-warning signal:** Backlog status review at chapter close
  surfaces gradient transitions not landed.
- **Mitigation:** The gradient transition is a chapter-level
  commitment; chapter close cannot complete with the transition
  unfinished. The structural commitment compounds across chapters.
  Backlog review at every chapter close surfaces any drift.
- **Owner:** chapter agents (per-chapter); cutover+30 chapter agent
  (final).
- **Status:** active.

### R-W-3: Perf regression at Bridge wall

- **Failure mode:** A Bridge method's marshaling cost exceeds
  perf-gate baseline at canary scale (esp. `PerRow` methods that
  inadvertently ship with non-streaming signatures).
- **Early-warning signal:** Perf-gate fails on the canary's bench
  rollup; the offending Bridge call surfaces by label.
- **Mitigation:** The analyzer's frequency-shape contract structurally
  rejects `Task<T>` per-call on `PerRow` methods. Bench scopes at
  every Bridge entry. Per-label perf-gate baseline includes Bridge
  calls.
- **Owner:** chapter agents (per-method); Bridge wave architect (per
  rule).
- **Status:** active.

### R-W-4: V1-evolution divergence

- **Failure mode:** V1 maintainers fix a bug in V1's trunk source
  after V2 has vendored a copy; V2's vendored copy continues to
  exhibit the bug.
- **Early-warning signal:** V1 maintainer reports the fix; V2 must
  decide to pull forward or document divergence.
- **Mitigation:** Manifesto § III codifies the editorial discipline.
  V2 either pulls the V1 fix forward into `Adopted/` (citing the V1
  fix in DECISIONS) or leaves the divergence (citing the divergence
  rationale in DECISIONS). The relationship between V1's trunk source
  and V2's vendored source is editorial, not authoritative.
- **Owner:** V2 chapter agent for the affected method.
- **Status:** active (no incidents through chapter 0.5).

### R-W-5: Analyzer false positives blocking legitimate work

- **Failure mode:** `Projection000BridgeWallDiscipline` analyzer
  flags a legitimate signature as a violation, blocking a slice from
  shipping.
- **Early-warning signal:** Analyzer error during build.
- **Mitigation:** Each wall rule ships with negative-test fixtures
  demonstrating the failure mode. The analyzer's false-positive rate
  is monitored at chapter close. A confirmed false positive is fixed
  in the analyzer's logic, not worked around in the Bridge method.
- **Owner:** Bridge wave architect (analyzer maintainer).
- **Status:** active.

### R-W-6: Equivalence-test brittleness on V1 non-determinism

- **Failure mode:** V1 SQL emission's implicit row ordering changes
  between V1 sessions; the equivalence test fails on a change V2
  considers semantically equivalent.
- **Early-warning signal:** Equivalence property test fails on a
  fixture that previously passed.
- **Mitigation:** `CatalogEquivalence.normalizeForEquivalence` quotients
  out six legitimate collection-ordering axes per the equivalence-test
  spec. If a new non-determinism source surfaces, the relation gains
  a deliberate axis (with comment naming the source) per the
  evidence-grows-under-pressure discipline. The relation does not
  grow under speculation.
- **Owner:** chapter agents (equivalence test maintainers).
- **Status:** active.

### R-W-7: Bridge method scope creep (more methods than necessary)

- **Failure mode:** A chapter adds Bridge methods for capabilities V2
  could rebuild fresh in F#, accumulating wall-ceremony for
  duplicated IR fields. The post-SPLIT counter-examples (Nullability,
  ForeignKey) named this trap.
- **Early-warning signal:** ADMIRE re-classification review at
  chapter open surfaces the trap.
- **Mitigation:** Manifesto § XIX codifies the SPLIT discipline.
  Three-of-three criteria: V1's contribution is joined/derived
  evidence; the split point is structurally clean; the Bridge surface
  is small enough to be worth the wall ceremony. If any criterion
  fails, defer to PURE PASS in F#.
- **Owner:** chapter agent (per-Bridge-method decision).
- **Status:** active.

### R-W-8: Wave indefinite extension (cutover+30 not reached)

- **Failure mode:** Chapter sequence extends past V2-driver KPI's
  cutover estimate (~late July 2026 per operator); V1 sunset deferred
  indefinitely.
- **Early-warning signal:** V2_DRIVER's chapter sequencing slips;
  backlog phases stack up.
- **Mitigation:** The Bridge wave's purpose is to *shorten* the path
  to cutover, not extend it. Parallelization of Phases 3, 4, 5 after
  Phase 0.5 close is the load-bearing acceleration. If the
  parallelization fails to deliver, the operator-side fallback ladder
  (R6 governance, T-30 / T-15 gates) determines whether V2-augmented
  becomes the sustained mode rather than V2-driver.
- **Owner:** operator + Bridge wave architect.
- **Status:** active.

---

## VIII. Sequencing graph

```
                          ┌─────────────────────────────────────┐
                          │  Phase 0.5 — Bridge bring-up        │
                          │  (chapter 0.5; slices α-η)          │
                          │  PREREQUISITE                       │
                          │  ~1 session                         │
                          └────────────────┬────────────────────┘
                                           │
                                           ▼
        ┌──────────────────────────────────┼──────────────────────────────────┐
        │                                  │                                  │
        ▼                                  ▼                                  ▼
┌───────────────────┐         ┌─────────────────────┐         ┌──────────────────────┐
│ Phase 3 — Data    │         │ Phase 4 — Identity  │         │ Phase 5 — Diagnostics│
│ (chapter 4.1.B)   │         │ (chapter 4.2)       │         │ (chapter 4.3)        │
│ ~1.5-2 weeks      │         │ ~1 week             │         │ ~1.5-2 weeks         │
│ Bridge: items 9,10│         │ Bridge: item 8      │         │ Bridge: none         │
└──────────┬────────┘         └──────────┬──────────┘         └──────────┬───────────┘
           │                             │                               │
           └─────────────────────────────┼───────────────────────────────┘
                                         │
                                         ▼
                          ┌─────────────────────────────────────┐
                          │  Phases 3/4/5 close jointly         │
                          │  V2-driver KPI critical path        │
                          │  operationally complete             │
                          └────────────────┬────────────────────┘
                                           │
                                           ▼
                          ┌─────────────────────────────────────┐
                          │  Phase 6 — DACPAC (chapter 3.x)     │
                          │  CONDITIONAL on deploy path         │
                          │  ~2.5 weeks (if pursued)            │
                          │  Bridge: items 3, 4                 │
                          └────────────────┬────────────────────┘
                                           │
                                           ▼
                          ┌─────────────────────────────────────┐
                          │  Cutover (~late July 2026)          │
                          │  V2-driver or V2-augmented per pair │
                          │  V1 stays warm through cutover+30   │
                          └────────────────┬────────────────────┘
                                           │
                                           ▼
                          ┌─────────────────────────────────────┐
                          │  cutover+30                          │
                          │  Phase 8 / Chapter 6                  │
                          │  BridgeManifestSunsetGateTest        │
                          │  V1 sunset condition checked         │
                          └─────────────────────────────────────┘
```

**Parallelization notes:**

- Phase 0.5 is sequential — α → β → (γ + η) → δ → ε → ζ → θ. Slice η
  (V2-for-V1 first method) is independent of γ-ζ (V1→V2 chain) and
  can proceed in parallel within the chapter.
- Phases 3, 4, 5 parallelize after Phase 0.5 closes. Each phase is
  independent at the chapter level (no inter-chapter dependencies
  on Bridge surface).
- Phases 1, 2, 7 are substantially closed; their pending items
  (cross-module FK, defaults, descriptions) are independent of the
  Bridge wave.
- Phase 6 is conditional; opens only if the operator's deploy path
  requires DACPAC. Independent of Phases 3-5 in scheduling terms;
  can run in parallel or sequentially per chapter-cadence preference.
- Phase 8 (pragmatic close + Chapter 6 sunset) depends on every prior
  phase closing first; runs in the cutover-15 to cutover+30 window.

**Critical-path estimate:** Phase 0.5 (1 session) + parallel
Phases 3/4/5 (~2 weeks at session cadence with parallelization) +
cutover gating + soak + cutover+30 sunset = ~6 weeks of focused work
spread across the operator's cutover timeline.

---

## IX. Cutover+30 gate condition (formalized)

The cutover+30 gate is the structural witness that V1 sunset begins
administratively. The gate has four conditions; all four must hold
for the operator to authorize V1 sunset.

### Condition 1: V2 production stability

V2 emissions in every environment for at least one full
schema-evolution cycle without canary divergence.

- **Witness:** R6 governance protocol (per `DECISIONS 2026-05-22`);
  N=10 consecutive green canary runs across the pair-environment
  matrix.
- **Owner:** operator + V2 chapter agent.
- **Status:** evaluated at cutover-day; held through cutover+30 soak.

### Condition 2: Bridge gradient convergence

Every Bridge method's `Current` equals its `Target`.

- **Witness:** `BridgeManifestSunsetGateTest` in
  `Projection.Bridge.Tests.BridgeManifestTests` (currently
  `[Fact(Skip = "Active at cutover+30 chapter close")]`; activated
  at Chapter 6 open).
- **Owner:** Bridge wave architect; chapter 6 agent.
- **Status:** evaluated at Chapter 6 open (cutover+15).

### Condition 3: V1 trunk reference removal

The ProjectReference to V1's trunk assemblies has been removed from
`Projection.Bridge.Core.csproj`.

- **Witness:** `Projection.Bridge.Core.csproj` contains no
  `ProjectReference` to any `Osm.*` csproj. Verified by build:
  Bridge.Core builds without V1's csprojs present.
- **Owner:** Bridge wave architect.
- **Status:** evaluated when Condition 2 holds (all Bridge methods
  have transitioned past `Delegated`).

### Condition 4: Operator sign-off

The operator has authorized V1 sunset.

- **Witness:** DECISIONS entry recording the sign-off, the date, the
  conditions verified.
- **Owner:** operator.
- **Status:** at operator's discretion when Conditions 1-3 hold.

When all four conditions hold, V1's trunk source can be archived. The
csprojs under `src/Osm.*` no longer need to build; the test projects
no longer need to run; the CI pipelines no longer need to exercise
V1's emission path. V1 sunset is administratively complete.

The cutover+30 gate is a condition, not a deadline event. The
calendar of cutover+30 is a guideline for when the conditions are
*expected* to hold; the conditions themselves are the gate. If the
conditions hold earlier (e.g., V2 reaches V2-driver mode in every
environment before cutover+30 and the gradient converges), sunset
can begin earlier with operator sign-off. If the conditions hold
later (e.g., V2-augmented persists through cutover+30 in some
environments), sunset waits until they hold.

---

## X. Lifecycle protocol and ownership

### Backlog item lifecycle

```
proposed ───────────────────────► canceled (with DECISIONS rationale)
   │
   ▼
scheduled ─────► blocked (with named blocker)
   │                 │
   │                 ▼ (when blocker resolves)
   ▼            scheduled
in-flight
   │
   ▼
shipped ────────────────────────► sunset (when purpose ends)
```

Status transitions are documented in commits. The transition itself
is a one-line change (status field update); the audit trail compounds
across the chapter sequence. Status reverts are forbidden — a
regression on a `shipped` item is a new item, not a revert.

### Ownership

- **Bridge wave architect:** owns the master Bridge-methods table,
  the wall analyzer, the cycle-discipline rule, the manifest test
  growth, the equivalence-test fixture pattern, the cutover+30 gate
  activation, the documentation deltas. Cross-cuts every chapter.
- **Chapter agent (per chapter):** owns the chapter's slice scope,
  the Bridge methods the chapter consumes, the chapter's
  cross-cutting work, the chapter's per-phase risks. Per-phase
  authority.
- **Operator:** owns the cutover decision, the V2-driver-mode
  authorization per pair, the V1 sunset sign-off, the strategic
  scope decisions (e.g., whether Phase 6 opens, whether V2-for-V1
  surface adds capabilities).
- **V1 maintainer:** owns V1's trunk source, V1's evolution, V1's
  adoption of V2-for-V1 capabilities, V1's production stability.
  Independent of V2's chapter rhythm.

### Backlog refresh cadence

- **At every chapter close:** status transitions; new items added;
  canceled items annotated; cross-references resolved.
- **At every significant slice landing:** in-flight statuses
  updated; commit references attached.
- **At chapter open:** the chapter's items move from `scheduled` to
  `in-flight` for the slices it commits to.
- **At operator strategic reframe:** Phase scope shifts; V2_DRIVER
  amendments cascade into BACKLOG.

### Backlog drift detection

The chapter-close ritual (per `DECISIONS 2026-05-14`) gains a
backlog-review dimension:

- Walk every item with status `in-flight` and confirm the commits
  match.
- Walk every item with status `scheduled` and confirm the chapter
  open document references it.
- Walk every item with status `blocked` and confirm the blocker is
  still active.
- Walk the master Bridge-methods table; confirm `Current` matches
  the implementation; surface any drift to the chapter agent.

Drift detection is the structural complement to the chapter-mid
audit (per `DECISIONS 2026-05-19`).

---

## XI. Cross-references

### Strategic surfaces

- **`V2_DRIVER.md`** — destination KPI and chapter sequencing under
  V2-driver mode. The strategic *why*; this backlog is the
  operational *what and when*.
- **`CSHARP_FSHARP_MANIFESTO.md`** — architectural canonical for the
  C#/F# partition and the Bridge wave. Cite by section number from
  backlog items when the architectural rationale needs surfacing.
- **`VISION.md`, `SPINE.md`, `PLAYBOOK.md`, `STAGING.md`** — V2's
  strategic frame surfaces; cited at chapter scope, not at
  backlog-item scope.

### Chapter documents

- `CHAPTER_0_5_OPEN.md` — Bridge bring-up; seven-slice arc.
- `CHAPTER_3_5_CLOSE.md` — Π port keystone (Phase 1).
- `CHAPTER_4_1_A_CLOSE.md` — Schema-as-driver (Phase 2).
- `CHAPTER_4_1_B_CLOSE.md` + `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md`
  — Data-as-driver (Phase 3).
- `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` — Identity-as-driver (Phase 4).
- `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md` — Diagnostics
  (Phase 5).
- `CHAPTER_3_X_OPEN.md` — DACPAC (Phase 6, conditional).
- `CHAPTER_3_2_CLOSE.md` — SnapshotRowsets (Phase 7, closed).
- `CHAPTER_5_OPEN.md` — Pragmatic close (Phase 8).

### Per-component records

- **`ADMIRE.md`** — every V1 component's V2 placement record. Bridge
  wave entries gain Current/Target gradient pair (format amendment
  2026-05-16). Cite by entry header for per-component context.

### Decision log

- **`DECISIONS.md`** — append-only. Cite by date + title:
  - `DECISIONS 2026-05-16 — Bridge wave: V2 inherits from V1`
    (codifying entry; ADMIRE re-classification corrections;
    R6 Stage-2 specification)
  - `DECISIONS 2026-05-22 — R6: Split-brain governance rule`
  - `DECISIONS 2026-05-22 — T-30 / T-15 cutover fallback ladder gates`
  - `DECISIONS 2026-05-10 — V2-driver as destination KPI`
  - `DECISIONS 2026-05-15 (late) — Pillar 9: harvest-dichotomy`
  - `DECISIONS 2026-05-13 — IR grows under evidence, not speculation`
  - `DECISIONS 2026-05-13 — Anticipation vs. speculation in
    abstraction extraction`

### Axiom surfaces

- **`AXIOMS.md`** — formal axiom set. Cite by axiom number:
  - A41 candidate (Transform registry totality + Bridge clause)
  - A42 candidate (Inheritance citation discipline)
  - A18 amended (Π never consumes Policy)
  - A40 (Harmonization-via-parameterization)
  - T1 (byte-for-byte determinism)
  - T11 (Sibling-Π commutativity)
- **`PRODUCT_AXIOMS.md`** — L3 product axioms; the operator's
  promise. L3-CC-Transform-Totality covers data-intent /
  operator-intent separation.

### Structural artifacts

- `src/Projection.Bridge.Core/Audit/BridgeMethodAttribute.cs` —
  audit attribute definition.
- `src/Projection.Bridge.Core/Audit/BridgeManifest.cs` —
  reflection-scanned validator.
- `src/Projection.Bridge.Core/Audit/{SunsetDisposition,Determinism,Frequency}.cs`
  — gradient and class enums.
- `src/Projection.Bridge.Core/Wire/{BridgeResult,BridgeError}.cs` —
  BCL wire envelope.
- `src/Projection.Bridge.Core/Capabilities/Catalog/ExtractMetadata.cs`
  — first worked-example capability.
- `tests/Projection.Bridge.Tests/BridgeManifestTests.cs` —
  manifest well-formedness + reserved sunset gate.

### Audit surfaces

- `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` — L1↔L2↔L3 coverage
  audit; per-axiom delivery matrix.
- `AUDIT_2026_05_DDD_HEXAGONAL_FP.md` — chapter-3.1 close five-agent
  audit; Tier 1/2/3/4 epistemic backlog.

### Production cutover plan

- **`V2_PRODUCTION_CUTOVER.md`** — Draft 3 plan; integrates
  verifiability-triangle audit. §13.X carries the Bridge wave
  addendum.

---

This backlog is canonical. It supersedes the prior "forwarding pointer"
framing. The integrated operational surface — V2-driver chapter
sequence + Bridge wave gradient transitions + cross-cutting
infrastructure + V1-side adoptions + risk register — lives here. Cite
this document by section number when chapter pre-scopes, DECISIONS
amendments, or HANDOFF entries reference operational scope.

Refreshed at every chapter close. Held under the chapter-close ritual.
Drift caught at chapter-mid audit. Append-mostly. Owned across the
chapter agent + Bridge wave architect + operator triad.

Hold the spine.
