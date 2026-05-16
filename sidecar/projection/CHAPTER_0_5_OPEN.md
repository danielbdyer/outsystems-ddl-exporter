# Chapter 0.5 — Bridge bring-up (V1→V2 inheritance machinery)

**Status:** open. Chapter that introduces `Projection.Bridge.Core` and
`Projection.Bridge.Runtime` as the structural surface through which V2
inherits from V1. Sits between V1 (the trunk's C# implementation, donor of
working logic) and V2 (the F# sidecar, the inheritor). Chapter numbering
reflects its role as the load-bearing prerequisite for the V2-driver KPI
parallel phase — it opens before chapters 4.1.B-δ / 4.2 / 4.3 because each
of those chapters consumes the Bridge inheritance surface.

## Strategic frame

The V1↔V2 boundary V2 inherited from its first instinct was a **wall**:
V1 emits `osm_model.json`, V2 reads. Two systems on either side; the
boundary is data. This was correct for a stage of work where decoupling
was the load-bearing virtue.

Under V2-driver KPI, with V1 sunset committed at cutover+30, decoupling
has finished its job. The relationship is no longer international trade
between peer systems; it is **inheritance** — V2 descends from V1 by
adopting selected traits and refining them in its own genome.

This chapter introduces the inheritance machinery: a C# project that
ProjectReferences V1's six trunk assemblies and exposes V1's *operations*
as BCL-typed, V2-vocabulary, capability-shaped methods. F# adapters
consume these methods to lift V1's working logic into V2 territory. The
bidirectional sibling project surfaces V2 capabilities back into V1
during dual-track. Every public method carries audit metadata that
records its progression on the inheritance gradient; the reflection-
scanned manifest is the auditable witness.

See `README.md` § "V2 inherits from V1" for the prose framing of the
shape; `CLAUDE.md` operating-disciplines table for the inheritance row;
`AXIOMS.md` A41 / A42 candidates for the structural commitments;
`DECISIONS 2026-05-16 — Bridge wave: V2 inherits from V1` for the
codifying entry.

## The eight load-bearing axes (strategic frame)

1. **Direction matters.** V2 inherits from V1; V2 is not a peer of V1.
   A wall implies symmetry; inheritance implies asymmetry. V2 takes
   V1's working logic and refines it; V1 is sunsetting and does not
   inherit from V2 except via the bidirectional runtime surface during
   dual-track.

2. **Lift verbs, not nouns.** V1's domain types stop at the Bridge wall.
   The Wire records crossing the wall use V2 vocabulary (Kind, Module,
   Attribute — not Entity, Espace). V1's `OsmModel` aggregate root does
   not survive into V2 by name; F# reconstructs nouns from verb outputs.

3. **Adoption is continuous, not phased.** Every Bridge method declares
   both its current state and its target state on the inheritance
   gradient (`Delegated` → `Vendored` → `RefinedInPlace` →
   `TranslatedToFSharp`). Progression happens chapter-by-chapter as
   capabilities benefit. There is no "now we adopt, now we refine"
   calendar; only the cutover+30 gate that asserts every method has
   reached its declared target.

4. **The wall is structural, not stylistic.** A C# Roslyn analyzer
   (`Projection000BridgeWallDiscipline`, slice β below) enforces every
   wall rule: BCL types only, capability-shaped names, V2 vocabulary,
   `CancellationToken` at every entry, never throws, one file per
   capability, frequency-shape contract, `[BridgeMethod]` attribute
   required. The discipline is type-witnessed; mistakes do not compile.

5. **The audit metadata is the manuscript history.** Every public method
   carries `[BridgeMethod(Chapter, AddedDate, V1Source, Current, Target,
   Determinism, Frequency)]`. The seven fields are the citation
   apparatus — what V1 source the verb descends from, which V2 chapter
   demanded the lift, where the method sits on the gradient, what
   determinism class it falls in, what marshaling cost class it
   declares. The reflection-scanned manifest aggregates these citations
   as an auditable artifact.

6. **Pillar 9 holds structurally.** Bridge methods are pre-Enrichment
   data sources. They return raw V1 facts as `DataIntent`; V2 passes
   apply `OperatorIntent` downstream. Bridge method signatures cannot
   accept `Policy` as a parameter; the analyzer rejects the signature.
   A18 amended (Π never consumes Policy) gains a Bridge corollary: the
   chain is `Evidence → Bridge (DataIntent) → E-passes (OperatorIntent
   applied) → Π (consumes Catalog × Profile)`.

7. **Bidirectional, asymmetric.** `Projection.Bridge.Core` carries the
   V1→V2 lift surface (V2 consuming V1 capabilities). The sibling
   `Projection.Bridge.Runtime` carries the V2→V1 inheritance surface
   (V1 consuming V2 capabilities during dual-track). The split is not
   tactical; it is the natural factoring of two distinct roles, each
   sunsetting on its own schedule. The asymmetry of volume — many V1→V2
   lifts, few V2→V1 capabilities — reflects V1's transient consumer
   status.

8. **Sunset is the empty workshop, not a deadline event.** At cutover+30,
   the `BridgeManifestSunsetGateTest` asserts that every Bridge method
   has reached its declared target state. The manifest accumulates this
   schedule chapter-by-chapter. The cutover+30 chapter close is short
   because the work was done continuously.

## Slices

The bring-up ships as seven slices, each landing one piece of the
inheritance discipline. Each slice has a property witness; each closes
when its witness is green.

### Slice α — Audit primitives (`Projection.Bridge.Core/Audit/`)

`BridgeMethodAttribute` with seven required fields (Chapter, AddedDate,
V1Source, Current, Target, Determinism, Frequency). `BridgeManifest`
reflection-scanned at test build via
`Projection.Bridge.Tests.BridgeManifestTests`. Three structural witnesses:

- Manifest scan is deterministic (idempotent).
- Manifest is well-formed (every required field populated; AddedDate
  parseable; Current ≤ Target on the gradient).
- Sunset gate `[Fact(Skip = "Active at cutover+30 chapter close")]`
  reserved; flips at Chapter 6 open.

### Slice β — Wall discipline analyzer (`Projection.Analyzers`)

New analyzer `Projection000BridgeWallDiscipline` enforces eight rules
on every type in `Projection.Bridge` namespaces:

1. BCL types only across the wall (no `Osm.*` types in public signatures).
2. Capability-shaped names (verbs, not nouns).
3. V2 vocabulary in record names (Kind / Module / Attribute, not
   Entity / Espace).
4. `CancellationToken` at every public entry.
5. Never throws (returns `BridgeResult<T>` shape).
6. One public method per file (cherry-pick auditability).
7. Frequency-shape contract (`PerRow` requires `IAsyncEnumerable<T>`
   or batched input).
8. `[BridgeMethod]` attribute required on every public method, with
   all seven fields populated.

Each rule has a negative-test fixture. The analyzer is the structural
type encoding of the inheritance discipline.

### Slice γ — First inheritance: `ExtractMetadataAsync`

`Projection.Bridge.Core/Capabilities/Catalog/ExtractMetadata.cs` lifts
V1's metadata-extraction verb. Input is Bridge's own minimal record:
`ExtractMetadataInput(string ConnectionString, IReadOnlyList<string>?
ModuleFilter, bool IncludeSystem, bool IncludeInactiveModules, bool
OnlyActiveAttributes)` — not V1's `CliConfigurationContext`. Bridge
re-implements V1's configuration-flattening in ~30 lines; that is the
cost of the V2 surface being V2-shaped.

The output is a flat `ExtractMetadataOutput` of `IReadOnlyList<Row>`
records (modules / kinds / attributes / references / physical tables /
static populations / attribute foreign keys). **The surface lifted is
V1's rowset shape (`OutsystemsMetadataSnapshot`), not its aggregate-root
shape (`OsmModel`).** The aggregate root is V1's reconstruction; V2
reconstructs from evidence directly. Initial state `Delegated`, target
state `RefinedInPlace` (scheduled for slice ε).

### Slice δ — F# consumption (`Projection.Adapters.Osm.CatalogReader`)

`SnapshotSource` DU gains `LiveOssysViaBridge of input: ExtractMetadataInput`.
The translation function `bundleOfBridgeOutput` is ~30 lines of field
rename from Wire records to the existing `RowsetBundle` shape. The
existing `parseRowsetBundle` is reused unchanged — harmonization-via-
parameterization (A40) in practice. The `DataKind → IsStatic` string-
to-bool projection lives in the F# adapter with a citation comment
naming V1's `EntitiesResultSetProcessor.cs` as the source of truth.

### Slice ε — First adoption: vendoring the extraction

V1's relevant source files (`MetadataSnapshotRunner.cs`,
`SnapshotJsonBuilder.cs`, the result-set processor chain) are copied
into `Projection.Bridge.Core/Adopted/Catalog/`, namespaced under
`Projection.Bridge.Adopted`. The ProjectReference to `Osm.Pipeline` is
NOT removed at this slice (other capabilities still need it during
dual-track); rather, the `ExtractMetadataAsync` method's body switches
from delegating to V1's class to calling the local copy. The
`[BridgeMethod].Current` transitions from `Delegated` to `Vendored`.
The slice-ζ equivalence test is the witness that the transition was
correctness-preserving.

### Slice ζ — Equivalence witness

`tests/Projection.Bridge.Tests/Fixtures/bridge-ossys-seed.sql` plus
paired `BridgeFixtures.canonicalBundle` literal in an F# helper module
under `tests/Projection.Tests/`. Coverage cells: at least 2 modules
(axis #1), one module with ≥3 kinds (axis #2), one kind with ≥4
attributes (PK + FK + IDENTITY + NVARCHAR; axis #3 preserve), one kind
with ≥2 indexes (axis #5/#6), one static entity with ≥3 rows (axis #9),
one system-owned entity (axis #7), one external entity (axis #19).

`CatalogEquivalence.normalizeForEquivalence` quotients out the six
legitimate non-determinism axes (collection orderings: modules, kinds,
references, indexes, modality, static rows) and preserves five semantic
axes (attribute declaration order, index column ordinal, SsKey shape,
identifier casing/whitespace, optional-field carriage). The property
test asserts `equivalent (parse (SnapshotRowsets canonicalBundle))
(parse (LiveOssysViaBridge input))`. **This is the load-bearing
structural claim of the entire Bridge wave.**

### Slice η — V2-for-V1: first dual citation (`Projection.Bridge.Runtime`)

`Projection.Bridge.Runtime/Capabilities/V2ForV1/InvokeV2TopologicalOrder.cs`
exposes V2's `TopologicalOrderPass` as
`Task<BridgeResult<IReadOnlyList<TableRef>>>`. Demonstrated V1 consumer:
V1's static-seed FK-order bug at
`/home/user/outsystems-ddl-exporter/src/Osm.Pipeline/Orchestration/BuildSsdtStaticSeedStep.cs:82-86`
sorts within static category only; cross-category dependencies violate
FK constraints. The paired test demonstrates V1 calling
`InvokeV2TopologicalOrderAsync` and the bug closing. `[BridgeMethod].V1Source`
is `"OriginAuthoredInV2"`; `Target` is `RefinedInPlace`.

Three other V2-for-V1 capabilities (`InvokeV2RenderAsync`,
`InvokeV2FlattenLineageAsync`, `InvokeV2FlattenDiagnosticsAsync`) are
deferred — no demonstrated V1 consumer today. Per evidence-over-
speculation, they land at the chapter where their first consumer
materializes.

### Slice θ — Documentation as manuscript

- `CLAUDE.md` operating-disciplines table — new row for Bridge
  inheritance discipline (lands in this commit; codified as load-bearing
  on equal footing with A18 amended and pillar 9).
- `HANDOFF.md` — bring-up entry at top citing this chapter as in-flight
  and naming slices α–η.
- `README.md` — new "V2 inherits from V1" section between the existing
  "What this is" and "Layout" sections.
- `ADMIRE.md` — header amendment introducing `Current state / Target
  state` pair on every entry; existing entries updated.
- `AXIOMS.md` — A41 candidate body filled to codify Bridge methods as
  `DataIntent` sources; A42 candidate added codifying the audit-trail
  inheritance discipline (Bridge methods cite V1 source via attribute).
- `DECISIONS.md` — new top entry `2026-05-16 — Bridge wave: V2 inherits
  from V1`.

## Out of scope (this chapter)

- **Lifting `MatchUsersAsync`, `GenerateMergeInsertAsync`, `ReadCdcRows`,
  or other downstream chapter capabilities.** These land at chapters
  4.1.B / 4.2 / 4.3 respectively, each at their own initial state.
  Chapter 0.5 ships the substrate; chapters that consume it ship the
  specific lifts.
- **Refining `NullabilityEvaluator` / `ForeignKeyEvaluator` via Bridge.**
  The Bridge SPLIT-feasibility analysis found these are mode-bound rule
  engines and that V2's `NullabilityRules.fs` / `ForeignKeyRules.fs`
  already cover the rule space. ADMIRE re-classification: both revert
  to PURE PASS in F#; no Bridge lift. Only `UniqueIndexEvidenceAggregator`
  is a clean SPLIT (signal extraction lifts; rule application stays
  in F#) — and that lands at the chapter that consumes it, not at
  bring-up.
- **`ComputeTwoPhaseInsertOrderAsync`** as a Bridge lift. V2's existing
  `TopologicalOrderPass` is parameterizable via `SelfLoopPolicy` (A40);
  V1's two-phase strategy is absorbed via parameterization, not via the
  wall. Harmonization beats Bridge for this case.

## Success criteria

- `dotnet build` clean on all three new projects under
  `TreatWarningsAsErrors=true`.
- `BridgeManifestTests` passes (manifest scan is deterministic; manifest
  is well-formed).
- The analyzer fires on a deliberate violation (negative test fixture
  per slice β).
- `ExtractMetadataAsync` round-trips an OSSYS fixture into a Catalog
  byte-equivalent to the canonical-bundle path (slice ζ).
- `InvokeV2TopologicalOrderAsync` resolves V1's static-seed FK-order
  bug on a paired V1 test (slice η).
- Documentation deltas land per slice θ.
- The chapter close lands the `ADMIRE.md` re-classification corrections
  (Nullability and ForeignKey revert to PURE PASS; UniqueIndex SPLIT
  confirmed; OSSYS catalog producer placement gains Current/Target
  pair).
- Per-axiom delivery matrix update: A41 candidate body filled; A42
  candidate scheduled.

## Sequencing rationale

Chapter 0.5 is the prerequisite for chapters 4.1.B-δ / 4.2 / 4.3 to
proceed in parallel. The bring-up itself is sequential through slices
α–η in one session; the parallel phase opens after this chapter closes.
See `V2_DRIVER.md` chapter-sequencing update at chapter 0.5 close.
