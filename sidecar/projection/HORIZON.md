# HORIZON — Architectural backlog beyond V2 cutover

**Status:** composed 2026-05-20. Companion to `BACKLOG.md` (operational
V2 cutover ledger) and `VISION.md` (strategic cutover frame). Those
two documents govern *getting V2 to production*. This document governs
*what the system becomes once it is there*.

**Relationship to `BACKLOG.md`:** none of the items here are cutover-
blocking. They are the architectural ceiling — the features whose
realization would make this codebase the definitive formal system for
DDL schema intelligence, rather than a well-engineered exporter. Items
are grouped by the layer of the system they extend.

**Scope of this document:** 55 items in ten groups. Each item names
the specific gap, its location in the codebase, the implementation
sketch, and the formal or craft property it unlocks. Status vocabulary
is the same as `BACKLOG.md` (`proposed` / `scheduled` / `in-flight` /
`shipped` / `canceled`). All items open here as `proposed`.

---

## Table of contents

- [Group I — Kernel: categorical structure](#group-i--kernel-categorical-structure)
- [Group II — F# language features and idioms](#group-ii--f-language-features-and-idioms)
- [Group III — IR surface completion](#group-iii--ir-surface-completion)
- [Group IV — Statistical evidence layer](#group-iv--statistical-evidence-layer)
- [Group V — Policy intelligence shell](#group-v--policy-intelligence-shell)
- [Group VI — Graph topology](#group-vi--graph-topology)
- [Group VII — Schema algebra and delta types](#group-vii--schema-algebra-and-delta-types)
- [Group VIII — Certification and observability](#group-viii--certification-and-observability)
- [Group IX — Testing and verification laws](#group-ix--testing-and-verification-laws)
- [Group X — Targets and emission](#group-x--targets-and-emission)

---

## Group I — Kernel: categorical structure

These items make the mathematical structure of the pipeline visible,
composable, and law-checked. The Kleisli category already exists; these
items name it, test its laws, and extend it with new composition
operators.

---

### H-001 — Computation expression builder for Lineage

**Status:** proposed

**Gap.** `Lineage<'a>` is a writer monad. `Lineage.bind` and
`Lineage.ofValue` exist. There is no computation expression (CE)
builder, so consumer chains write `|> Lineage.bind (fun x -> ...)` at
every step. With a builder, the same computation reads:

```fsharp
lineage {
    let! decisions = NullabilityPass.run catalog policy profile
    let! ordered   = TopologicalOrderPass.run decisions policy profile
    return ordered
}
```

**Location.** `src/Projection.Core/Lineage.fs`. The builder is a dozen
lines: `Bind`, `Return`, `ReturnFrom`, `Zero` over the existing
primitives. No new semantics.

**Unlocks.** Every pass chain in `Compose.fs` and any future
composition surface reads as the pipeline it is, rather than as a
chain of `|> bind` applications. The CE also makes the monadic
structure legible to contributors who do not already know the
underlying algebra.

**Trigger.** When pass-chain composition sites in `Compose.fs` grow to
four or more sequential `Lineage.bind` calls — currently three at the
deepest site.

---

### H-002 — Computation expression builder for LineageDiagnostics

**Status:** proposed

**Gap.** `LineageDiagnostics<'a> = Lineage<Diagnostics<'a>>` is the
dual-writer stack used by every pass that produces both decisions and
observer-relevant findings. The same CE argument applies: today's
consumer chains are `|> LineageDiagnostics.bind (fun x -> ...)` steps;
a builder would give:

```fsharp
lineageDiagnostics {
    let! ctx    = UserFkReflowPass.discover catalog policy profile
    let! result = NullabilityPass.run ctx policy profile
    do! LineageDiagnostics.tellDiagnostics entries
    return result
}
```

**Location.** `src/Projection.Core/Lineage.fs` (alongside the
`LineageDiagnostics` module). The dual-writer builder needs `Bind`,
`Return`, `ReturnFrom`, `Zero`, and a `tell` method that lifts a
`Diagnostics<unit>` into the stack.

**Unlocks.** The writer-fidelity discipline (`DECISIONS 2026-05-30 —
writer-fidelity codification`) becomes readable as declarative code
rather than as a sequence of named primitive calls. The discipline
itself is unchanged; its surface becomes idiomatic.

---

### H-003 — Kleisli structure made explicit in code and documentation

**Status:** proposed

**Gap.** The pipeline IS a Kleisli category: each pass is an arrow
`Catalog → Lineage<Diagnostics<Catalog>>`, `PassChainAdapter.compose`
is Kleisli composition via `LineageDiagnostics.bind`, and
`Lineage.ofValue` is the identity arrow. None of this is named or
documented. A contributor reading `PassChainAdapter.compose` does not
know they are reading a category-theoretic composition operator.

**Location.** `src/Projection.Pipeline/PassChainAdapter.fs` and
`src/Projection.Core/Lineage.fs`.

**Implementation.** A type alias:
```fsharp
/// A Kleisli arrow in the pipeline category.
type Pass<'a, 'b> = 'a -> Lineage<Diagnostics<'b>>
```
and a module-level docstring on `PassChainAdapter.compose` citing the
Kleisli law it implements. No behavioural change; one compiler-checked
type alias + two docstrings.

**Unlocks.** Contributors can reason about pass composition using
categorical language. It also establishes the foundation for H-006
(parallel pass composition) and H-005 (branching lineage), both of
which extend the category with new operators.

---

### H-004 — Certificate as a first-class type wrapper

**Status:** proposed

**Gap.** The manifest is produced at the end of the pipeline as a
side-output. The DDL surface and the proof of its correctness are
separate values. A `Certificate<'a>` type would couple them:

```fsharp
type Certificate<'a> = {
    Value    : 'a
    Manifest : Manifest
    Trail    : LineageEvent list
}
```

**Location.** New type in `src/Projection.Core/Lineage.fs` or a
dedicated `src/Projection.Core/Certificate.fs`.

**Unlocks.** Downstream consumers receive `Certificate<DDL>` and can
inspect the proof without touching the DDL. Composing two
certificates produces a certificate whose trail is the concatenation
of both. The manifest is no longer terminal — it is a carried value.
Multi-target fanout (H-009) becomes `Certificate<SSDT> *
Certificate<Json> * Certificate<Distribution>` with a shared trail
prefix.

---

### H-005 — Branching lineage (speculative execution)

**Status:** proposed

**Gap.** The current lineage trail is linear and append-only. There is
no way to run a pass speculatively, inspect the resulting trail, and
discard it if the result is undesirable. Policy simulation and the
`v2 diff-policy` verb (H-036) require this.

**Location.** `src/Projection.Core/Lineage.fs`. The existing linear
writer monad becomes a special case of a `LineageTree<'a>` that
supports `branch: Lineage<'a> -> LineageTree<'a>` and
`commit: LineageTree<'a> -> Lineage<'a>`.

**Unlocks.** `Compose.runSkeleton` (already functionally complete) and
the full-policy pipeline can be run against the same `Catalog`; the
resulting trees are diffed on `SsKey × Classification` to produce the
policy delta. Every "what would happen if..." query over policy space
becomes a branch operation.

---

### H-006 — Parallel pass composition operator (monoidal product)

**Status:** proposed

**Gap.** `PassChainAdapter.compose` is strictly sequential Kleisli
composition. Many passes operate on disjoint `SsKey` sets and are
therefore mathematically independent. There is no parallel composition
operator.

**Implementation sketch.**
```fsharp
/// Run two passes concurrently on disjoint SsKey partitions,
/// merge their lineage trails at the join point.
val par : Pass<Catalog, Catalog> -> Pass<Catalog, Catalog>
       -> Pass<Catalog, Catalog>
```

The `SsKey` partition is derived from `TopologicalOrder.levels` (which
already computes independent batches). Passes within the same level are
disjoint by construction and can be composed with `par` rather than
`>=>`.

**Location.** `src/Projection.Pipeline/PassChainAdapter.fs`.

**Unlocks.** The deployment batch computation that `TopologicalOrder.levels`
already performs also becomes the pass scheduling surface. Passes that
don't share `SsKey` state run concurrently; composition time drops
from O(n_passes × n_kinds) to O(max_level_depth × n_kinds).

---

### H-007 — SchemaDelta type and a second category of delta passes

**Status:** proposed

**Gap.** Every pass today operates on a snapshot `Catalog`. There is
no type for *the change between two Catalogs*, so migration generation,
breaking-change detection, and safe-deploy ordering all require ad-hoc
diffing code.

**Implementation sketch.**
```fsharp
type SchemaDelta = {
    Added    : Kind list
    Removed  : Kind list
    Modified : (Kind * Kind) list   // (before, after)
    Renamed  : (SsKey * SsKey) list
}

val diff : Catalog -> Catalog -> SchemaDelta
```

**Location.** New module `src/Projection.Core/SchemaDelta.fs`.

**Unlocks.** A second pass category: `SchemaDelta → Lineage<Diagnostics<SchemaDelta>>`.
Delta passes can detect backward-incompatible changes (column type
narrowing, NOT NULL addition to a non-empty table, PK column removal),
compute safe-deploy order across the delta, and emit migration scripts
that are shaped to the delta rather than to the full schema. The
`CatalogDiff` smart constructor from chapter 3.5 is the precursor;
`SchemaDelta` is its generalization.

---

### H-008 — DiagnosticLattice: a partial order over diagnostic entries

**Status:** proposed

**Gap.** `Diagnostics<'a>` is currently an unordered collection of
`DiagnosticEntry` values. There is no formal notion of subsumption
(a table-level error makes its column-level errors redundant), or of
ordering (a nullability conflict must be resolved before a tightening
intervention targeting the same column is actionable).

**Implementation sketch.**
```fsharp
type DiagnosticRelation =
    | Subsumes of DiagnosticEntry * DiagnosticEntry
    | Precedes of DiagnosticEntry * DiagnosticEntry

val lattice : Diagnostics<'a> -> DiagnosticRelation list
val minimal  : Diagnostics<'a> -> Diagnostics<'a>
```

**Location.** `src/Projection.Core/Diagnostics.fs`.

**Unlocks.** The operator-facing diagnostic output becomes a minimal,
ordered set rather than a flat list. Triage is deterministic. The
`v2 diagnose` verb emits a structured report where each entry is
positioned in the partial order; resolving a parent entry propagates
its resolution downward.

---

### H-009 — Multi-target fanout with shared lineage trail

**Status:** proposed

**Gap.** A single pipeline run today produces one surface. The SSDT,
JSON, and Distributions emitters run independently; their lineage
trails diverge from the first step because they are separate pipeline
invocations.

**Implementation sketch.**
```fsharp
val fanOut :
    (Catalog -> 'a) ->     // SSDT emitter
    (Catalog -> 'b) ->     // JSON emitter
    (Catalog -> 'c) ->     // Distribution emitter
    Catalog ->
    Lineage<'a * 'b * 'c>
```

The shared lineage prefix is the pass chain; each target appends its
own emission events after the join.

**Location.** `src/Projection.Pipeline/Compose.fs`.

**Unlocks.** Multi-target consistency is provable: if the same pass ran
for all three targets, their lineage trails share the same prefix hash.
Divergences are precisely located to the emission layer, not the pass
chain. H-004 (Certificate) composes naturally: the result is
`Certificate<SSDT> * Certificate<Json> * Certificate<Distribution>`,
all sharing a trail prefix.

---

### H-010 — Bidirectional specs as Prisms (Catalog ↔ DDL)

**Status:** proposed

**Gap.** The emitter (`Catalog → DDL`) and reader (`DDL → Catalog`) are
separate code paths with no formal coupling. Roundtrip laws exist as
ad-hoc canary property tests but are not typed as laws.

**Implementation sketch.**
```fsharp
type Prism<'a, 'b> = {
    Get        : 'a -> 'b
    ReverseGet : 'b -> 'a option
}

val catalogDdlPrism : Prism<Catalog, DDL>
// Law: reverseGet (get c) = Some c'  where c' ≡ c modulo known lossy fields
// Violations ≡ manifest.Unsupported list
```

**Location.** New module `src/Projection.Core/Prism.fs`.

**Unlocks.** Coverage tracking in the manifest becomes a law checker
rather than a bookkeeping exercise. Every field in `Unsupported` is a
named violation of the losslessness law. The roundtrip canary becomes
a typed law check: `Prism.check catalogDdlPrism` on the fixture catalog
asserts the law and reports violations as typed `PrismViolation` values.

---

### H-011 — Incremental computation: change propagation through the pass graph

**Status:** proposed

**Gap.** Every pipeline run re-executes all passes over the full
`Catalog`. When only a small subset of `Kind` values change, most pass
work is redundant.

**Implementation sketch.** A dependency graph over passes (which
`SsKey` sets each pass reads and writes) derived from the lineage
trails of a prior run. On incremental re-run, only passes whose input
`SsKey` sets intersect the changed set re-execute; downstream passes
re-execute transitively.

**Location.** `src/Projection.Pipeline/PassChainAdapter.fs`.

**Unlocks.** Sub-second re-runs on incremental schema changes at 300+
table scale. The dependency graph is itself a lineage-derived artifact:
it is computable from `TrailEvent.SsKey` sets across a completed run.
No new IR is required; the lineage trail already carries the
information needed to compute the dependency graph.

---

## Group II — F# language features and idioms

These items adopt specific F# features that the codebase is already
aligned toward but hasn't yet activated. Each has an explicit trigger
condition per the F# feature surface section of `CLAUDE.md`.

---

### H-012 — Active patterns for SsKey structural dispatch

**Status:** proposed

**Gap.** Multi-step matches on `SsKey` recur across `NullabilityPass`,
`UniqueIndexPass`, `ForeignKeyPass`, and the adapter translation layer.
The nested match pattern:

```fsharp
match key with
| OssysOriginal _ -> ...
| Synthesized  _ -> ...
| DerivedFrom  _ -> ...
| V1Mapped     _ -> ...
```

is open-coded at each site. Active patterns would absorb the structural
traversal into a named pattern usable in a single `match`.

**Location.** `src/Projection.Core/Catalog.fs` (where `SsKey` is
defined).

**Trigger per CLAUDE.md.** When the same nested-match pattern appears
at three or more call sites. Currently at two known sites in passes;
the adapter layer likely adds a third. Count and activate at N=3.

---

### H-013 — Units of measure on Profile numeric fields

**Status:** proposed

**Gap.** `ColumnProfile.RowCount : int64`, `NullCount : int64`,
`NumericDistribution.P50 : decimal`, `Mean : decimal` are all bare
numeric types. Nothing prevents passing a row count where a percentile
is expected. The smart constructors enforce monotonicity; they do not
enforce dimensionality.

**Location.** `src/Projection.Core/Profile.fs`.

**Implementation.**
```fsharp
[<Measure>] type rows
[<Measure>] type pct
[<Measure>] type σ

type ColumnProfile = {
    RowCount  : int64<rows>
    NullCount : int64<rows>
    ...
}
```

**Trigger per CLAUDE.md.** When a numeric-mix-up bug surfaces in real
fixture data, OR when a strategy mixes percentile and count values in
the same expression. This is a safety net with zero runtime cost; the
trigger is the first confused-units bug.

---

### H-014 — Phantom types for pipeline stage safety

**Status:** proposed

**Gap.** A `Catalog` value at the output of `TopologicalOrderPass` and
a raw `Catalog` at the input of `NullabilityPass` are structurally
identical types. Nothing prevents passing a pre-ordered catalog to a
pass that expects a post-ordered one, or vice versa.

**Implementation.**
```fsharp
type [<Phantom>] Raw
type [<Phantom>] Ordered
type [<Phantom>] Tightened

type StagedCatalog<'stage> = StagedCatalog of Catalog

val topologicalOrder : StagedCatalog<Raw>      -> Lineage<StagedCatalog<Ordered>>
val nullability      : StagedCatalog<Ordered>  -> Lineage<StagedCatalog<Tightened>>
```

**Location.** `src/Projection.Core/Catalog.fs` (phantom type
declarations) + `src/Projection.Pipeline/Compose.fs` (pass signatures
updated).

**Unlocks.** Illegal stage-ordering becomes a compile-time error. The
pass chain in `Compose.fs` is not just a sequence; it is a typed
pipeline where each step's output type is the next step's input type.

---

### H-015 — Lens / optic library for Catalog navigation

**Status:** proposed

**Gap.** Deep updates in `Catalog` — modifying a specific
`Attribute.NullabilityDecision` inside a specific `Kind` inside a
specific `Module` — require three nested record-update expressions.
The path `catalog.Modules.[i].Kinds.[j].Attributes.[k]` is not
addressable as a value.

**Implementation.** A minimal optic layer (no external dependency):

```fsharp
type Lens<'s, 'a> = {
    Get : 's -> 'a
    Set : 'a -> 's -> 's
}

val kindAtKey    : SsKey -> Lens<Catalog, Kind option>
val attrAtKey    : SsKey -> Lens<Kind, Attribute option>
val nullability  : Lens<Attribute, NullabilityDecision>
```

Composed as `kindAtKey key >=> attrAtKey attrKey >=> nullability`.

**Location.** New module `src/Projection.Core/Optics.fs`.

**Trigger.** When pass implementations write three or more nested
record-update expressions targeting the same deep path. The Tightening
pass family is the most likely trigger point.

---

### H-016 — Policy as a typed combinator language

**Status:** proposed

**Gap.** Policy is currently a `Policy` record produced by parsing a
config file (TOML / JSON). The parsing is a 700+ line nested match
chain in `Policy.fs`. Illegal combinations are detectable only at
runtime. There is no way to compose two policies algebraically.

**Implementation sketch.**

```fsharp
type PolicyExpr =
    | Atom    of PolicyAtom
    | And     of PolicyExpr * PolicyExpr
    | Or      of PolicyExpr * PolicyExpr
    | Seq     of PolicyExpr * PolicyExpr
    | Override of OverlayAxis * PolicyExpr

val eval     : PolicyExpr -> Policy
val simplify : PolicyExpr -> PolicyExpr
val diff     : PolicyExpr -> PolicyExpr -> PolicyExpr
```

**Location.** New module `src/Projection.Core/PolicyExpr.fs`.

**Unlocks.** Two policies diff as `PolicyExpr.diff p1 p2`; the result
is the minimal expression describing the change. Illegal combinations
(`Emission.empty &&& Tightening.MaximalIntervention`) can be detected
at construction time. The config parser becomes `PolicyExpr.parse >>=
PolicyExpr.eval` — one pipeline rather than a 700-line flat match tree.

---

### H-017 — Profile inference passes (structural inference without live data)

**Status:** proposed

**Gap.** The full operator pass pipeline requires a populated `Profile`
— nullability interventions, FK tightening, and categorical uniqueness
rules all gate on profile evidence. `Profile.empty` returns an empty
profile, causing every strategy to fall back to its "evidence missing"
arm. A `Catalog` with no associated live profile cannot benefit from
any operator pass.

**Implementation.** A `ProfileInferencePass` that derives best-effort
profile evidence from structural catalog relationships:
- NOT NULL attributes → `NullCount = 0`
- PRIMARY KEY attributes → `UniqueCount ≈ RowCount`
- FK references → cardinality upper-bound from referent PK
- CHECK constraints → `CategoricalDistribution` bounded by constraint domain

**Location.** New pass `src/Projection.Core/Passes/ProfileInferencePass.fs`.

**Unlocks.** The full operator pipeline runs on any `Catalog`, even one
that has never touched a database. Structural invariants (PK
uniqueness, FK cardinality bounds) become baseline evidence when live
profiling is unavailable. The inferred profile is tagged
`ProfileSource.Structural` so downstream passes know the confidence
level.

---

## Group III — IR surface completion

The five DDL construct types added at chapter A.0' (`ColumnCheck`,
`Trigger`, `Sequence`, `ExtendedProperty`, `TemporalConfig`) are fully
populated in the IR and never emitted. These items close the loop.

---

### H-018 — CHECK constraint emission

**Status:** implemented (SsdtDdlEmitter.fs `columnCheckDef` + `ALTER TABLE ADD CONSTRAINT CHECK`; slice 5.13.column-features-emit)

**Gap.** `Kind.ColumnChecks : ColumnCheck list` is populated by the
OSSYS and SQL adapters. `SsdtDdlEmitter` does not emit CHECK
constraints. `ScriptDomBuild.tryParseFilterWithDiagnostics` (chapter
4.7) already provides the Diagnostics-aware parse helper.

**Location.** `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs`.

**Implementation.** Add `ColumnCheckDef` to the typed statement stream;
emit `ALTER TABLE ... ADD CONSTRAINT ... CHECK (...)` per
`ColumnCheck.Condition` via `ScriptDomBuild.tryParseFilterWithDiagnostics`.
Parse failures surface as `DiagnosticEntry` values via the
`WithDiagnostics` emitter signature discipline (chapter 4.7).

**Coverage impact.** `PredicateName.HasCheckConstraint` in
`ManifestEmitter` flips from always-false to live evaluation.

---

### H-019 — Trigger emission

**Status:** implemented (Statement.CreateTrigger; ScriptDomBuild.tryParseTriggerBody; SsdtDdlEmitter.triggerStatements; kindToSsdtFile + statements wired; DacpacEmitter + Deploy.fs exhaustiveness; Render.fs + ScriptDomGenerate.fs comment updates)

---

### H-020 — Sequence emission

**Status:** implemented (Statement.CreateSequence; ScriptDomBuild.buildCreateSequence + sequenceDataType; SsdtDdlEmitter.sequenceStatements; statements yields sequences before table loop; DacpacEmitter + Deploy.fs exhaustiveness)

---

### H-021 — ExtendedProperty emission across all four IR levels

**Status:** implemented (SsdtDdlEmitter.fs `extendedPropertyStatements` lines 443–471; all four levels: Kind/Module/Attribute/Index)

**Gap.** `Module.ExtendedProperties`, `Kind.ExtendedProperties`,
`Attribute.ExtendedProperties`, and `Index.ExtendedProperties` are all
populated at chapter A.0'. `buildSetExtendedProperty` for `Module` was
emitted at chapter 4.9 (slice ε). The remaining three levels are
unimplemented.

**Location.** `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs`.

**Implementation.** Reuse the `ExtendedPropertyOwner` DU (chapter 4.9);
add `Kind`, `Attribute`, and `Index` arms and their corresponding
`buildSetExtendedProperty` overloads.

---

### H-022 — TemporalConfig emission

**Status:** implemented (Statement.CreateTable 6th arg `TemporalConfig option`; ScriptDomBuild.buildCreateTable emits SystemTimePeriodDefinition + SystemVersioningTableOption; SsdtDdlEmitter.createTableStatement extracts ModalityMark.Temporal)

**Gap.** `ModalityMark.Temporal` carries temporal table configuration
(`SystemTimeColumn`, `HistoryTableName`, `RetentionPolicy`). Temporal
tables require `WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = ...))` in
the `CREATE TABLE` statement. The SSDT emitter does not produce this
clause.

**Location.** `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs`.

**Implementation.** Detect `ModalityMark.Temporal` on a `Kind`;
emit `PERIOD FOR SYSTEM_TIME` column pair + `WITH (SYSTEM_VERSIONING =
ON)` clause via ScriptDom's `TemporalClause`. Deactivation is a
`SET (SYSTEM_VERSIONING = OFF)` before table modification.

---

### H-023 — Coverage gap tracking for unsupported IR constructs

**Status:** proposed

**Gap.** When an IR field is populated but the target emitter does not
support it (e.g., SQL Server does not support a particular temporal
retention mode), the gap is currently silent. The manifest
`Unsupported` field lists named tolerances; it does not list per-kind,
per-field emission gaps.

**Location.** `src/Projection.Targets.SSDT/ManifestEmitter.fs`.

**Implementation.** Add an `EmissionGap` record to the manifest:
```fsharp
type EmissionGap = {
    KindKey     : SsKey
    FieldName   : string
    Reason      : string
}
```
Each emitter that silently skips a populated field emits a gap entry
rather than discarding. The manifest's `Unsupported` section widens to
include per-kind structured gaps alongside the existing per-divergence
strings.

---

## Group IV — Statistical evidence layer

Five profile constructs are fully computed in `LiveProfiler.fs` and
have zero downstream consumers. These items close the loop from
evidence to decision.

---

### H-024 — FK cardinality consumers

**Status:** implemented (ForeignKeyPass.fs — `cardinalityMetadata` enriches Metadata with `meanChildCount`; `cardinalitySuggestedConfig` emits SuggestedConfig on PolicyDisabled when density evidence is present; Profile.fs — `tryFindForeignKeyCardinality` + `tryFindForeignKeySelectivity` lookup helpers)

---

### H-025 — FK selectivity consumers

**Status:** implemented (FkSelectivityDiagnostics.fs — `emit : Profile -> DiagnosticEntry list`; Info entry per high-selectivity FK reference; meanMatchCount < 2.0 threshold with minDistinctCount ≥ 10 guard; wired in Pipeline.fs diagnostics list)

**Gap.** `ForeignKeySelectivity` (estimated FK lookup cost, derived
from index statistics) is computed and carried on `Profile` with no
consumer.

**Consumer.** An index advisor pass that recommends covering indexes for
high-selectivity FK columns. When `ForeignKeySelectivity.EstimatedCost`
exceeds a threshold, the pass emits a `DiagnosticEntry` with
`SuggestedConfig` pointing at an index addition in the emission policy.

**Location.** New pass `src/Projection.Core/Passes/IndexAdvisorPass.fs`.

---

### H-026 — JointDistribution consumers

**Status:** implemented (JointDependencyDiagnostics.fs — `emit : Profile -> DiagnosticEntry list`; Info entry per kind with near-unique FK tuple co-occurrence; uniquenessRatio ≥ 0.95 threshold with minDistinctCount ≥ 5 guard; wired in Pipeline.fs diagnostics list)

**Gap.** `JointDistribution` (co-occurrence counts for column pairs,
used to detect functional dependencies) is fully computed and carried
on `Profile` with no consumer.

**Consumer.** A functional dependency detection pass: when two columns
exhibit near-perfect co-occurrence (`JointDistribution.MutualInformation`
above a threshold), the pass emits a `DiagnosticEntry` suggesting a
unique constraint on the pair.

**Location.** New pass `src/Projection.Core/Passes/FunctionalDependencyPass.fs`.

---

### H-027 — Statistical moments emission (Mean and StdDev to manifest and diagnostics)

**Status:** implemented (ManifestEmitter.fs — `ColumnProfileSummary` type + `ColumnProfiles` field; `buildWith` accepts `Profile`; `toNode` emits `columnProfiles` JSON array sorted Schema→Table→Column)

**Gap.** `StatisticalMoments` (`Mean : decimal`, `StdDev : decimal`) is
computed inside `NumericDistribution` and never surfaced. It is the
most compact statistical summary of a numeric column's distribution.

**Location.** `src/Projection.Core/Profile.fs` (the moments are
computed here). Consumer: `ManifestEmitter.fs` (add to per-column
profile section) and `NullabilityPass.fs` (use `StdDev` to assess
whether a column has meaningful variance before recommending
intervention).

**Unlocks.** The manifest gains a per-column statistical summary.
Operators can assess distribution shape from the manifest alone,
without re-running the profiler.

---

### H-028 — IsPresentButInactive surfacing in diagnostics

**Status:** implemented (Projection.Pipeline/InactiveAttributeDiagnostics.fs; wired in Pipeline.fs `runWithConfigCore`; Source `selectionScan`, Code `selection.inactive-attribute`, Severity Warning)

**Gap.** `AttributeReality.IsPresentButInactive` is detected in the
OSSYS adapter (the attribute exists in the schema but is inactive in
the OutSystems model) and carried on `Attribute`. No pass or emitter
surfaces it.

**Location.** No existing pass handles this. A new check in the
selection policy or a dedicated `InactiveAttributePass`.

**Implementation.** Emit a `DiagnosticEntry` at `Source = "selection"`,
`Code = "selection.inactive-attribute"`, `Severity = Warning` for
every `IsPresentButInactive` attribute. The operator can then decide
whether to exclude it via `SelectionPolicy` or emit it with a
diagnostic flag.

---

### H-029 — coefficientOfVariation usage

**Status:** implemented (NullabilityPass.fs — `cvMetadata` helper; `opportunityEntry` takes `Profile`; CV surfaced in Metadata as `"cv"` key on `RelaxedUnderEvidence` and `MandatoryButHasNullsBeyondBudget` arms; G4 invariant-culture formatting)

**Gap.** `Profile.coefficientOfVariation` is a helper function
(`StdDev / Mean`, normalized dispersion) that is written and never
called. It is the standard measure for distinguishing "is the spread
large relative to the center?" from "is the spread large in absolute
terms?"

**Consumer.** `NullabilityRules.evaluate` — when assessing whether a
high null-count column should be flagged for tightening, the CV
distinguishes between "column has many nulls because it is sparse data"
(high CV → real signal) and "column has many nulls uniformly"
(low CV → structural intent).

**Location.** `src/Projection.Core/Strategies/NullabilityRules.fs`.

---

### H-030 — Faker: synthetic data emitter

**Status:** proposed

**Gap.** All statistical trigger conditions are met: `ColumnProfile`,
`NumericDistribution`, `CategoricalDistribution`, `StatisticalMoments`,
and `ForeignKeyCardinality` are all populated in the live profiler.
There is no pass that inverts these distributions to generate synthetic
INSERT statements.

**Implementation sketch.**

```fsharp
val fake :
    Kind -> ColumnProfile -> Policy -> int ->
    Lineage<Diagnostics<DataInsertScript>>
```

For each column:
- `CategoricalDistribution` → sample from the empirical PMF
- `NumericDistribution` → sample from a fitted distribution (Normal
  if `|CV| < 0.5`, LogNormal otherwise, using Mean + StdDev from
  `StatisticalMoments`)
- `ForeignKeyCardinality` → sample FK values from the referent PK
  pool in proportion to observed cardinality

**CLI verb.** `osm fake-data --profile <profile.json> --count 10000`
emits INSERT statements whose statistical signature matches the
original schema.

**Formal property.** The Faker is the right inverse of the profiler:
`profile (fake catalog profile policy n)` should yield a profile
statistically indistinguishable (within `n`'s sampling variance) from
the input `profile`. This property is the dual of the roundtrip canary;
it tests the profiler's invertibility.

---

## Group V — Policy intelligence shell

---

### H-031 — SuggestedConfig population in NullabilityPass

**Status:** implemented (NullabilityPass.fs — `MandatoryButHasNullsBeyondBudget` arm; JSONPath `$.tightening.interventions[?(@.id=="<id>")].nullBudget`; ceiling-4dp null fraction as suggested value)

**Gap.** `DiagnosticEntry.SuggestedConfig : SuggestedConfig option` is
the designed-in socket for policy suggestion. It was set to `None` when
the type was introduced. `NullabilityPass.opportunityEntry` is the most
natural first consumer.

**Location.** `src/Projection.Core/Passes/NullabilityPass.fs`.

**Implementation.** When `NullabilityRules.evaluate` returns an
`Opportunity`, populate `SuggestedConfig` with the TOML stanza that
would suppress the diagnostic:

```fsharp
SuggestedConfig = Some {
    Path  = $"tightening.interventions[{interventionId}].mode"
    Value = "enforce"
    Note  = Some "Statistical evidence: null rate below 0.5%"
}
```

**Unlocks.** The `v2 suggest-config` verb (H-032) has its first real
content. Every `RequireOperatorApproval` diagnostic becomes
self-documenting: the operator sees not just the finding but the config
change that would resolve it.

---

### H-032 — v2 suggest-config CLI verb

**Status:** implemented (Projection.Targets.OperationalDiagnostics/SuggestConfigEmitter.fs; `collect` deduplicates by Path (max Value); `emit` returns JsonNode; wired into `Compose.Outputs.SuggestConfigJson` + written as `suggest-config.json`)

**Gap.** The `SuggestedConfig` infrastructure in `DiagnosticEntry` was
designed for a `v2 suggest-config` verb that emits a policy document
containing all the config changes suggested by the current pipeline
run. The verb is not wired.

**Location.** `src/Projection.Cli/Program.fs` (verb registration) +
new `src/Projection.Targets.OperationalDiagnostics/SuggestConfigEmitter.fs`.

**Implementation.** Collect all `DiagnosticEntry` values where
`SuggestedConfig = Some _`; group by `SuggestedConfig.Path`; emit a
config document that, if applied, would silence all opportunity
diagnostics that have statistical evidence. The emitter is the mirror
of the config parser in `Policy.fs`.

**Unlocks.** A new operator workflow: run `osm diagnose`, then run
`osm suggest-config > policy-additions.toml`, then review and apply.
The pipeline becomes a policy advisor.

---

### H-033 — Policy diff: compare two pipeline runs under different policies

**Status:** proposed

**Gap.** There is no way to compare what the pipeline would produce
under two different policies. The operator cannot see "what changes if
I switch from `TighteningPolicy.minimal` to `TighteningPolicy.strict`?"
without running the pipeline twice and diffing the outputs manually.

**Implementation.** Using H-005 (branching lineage):

```fsharp
val diffPolicy :
    Catalog -> Profile ->
    Policy -> Policy ->
    Lineage<PolicyDiff>
```

`PolicyDiff` is the set of `SsKey` values whose `Classification` or
`DiagnosticEntry` set differs between the two runs. The diff is derived
from the two lineage trails, not from the two DDL outputs.

**Location.** `src/Projection.Pipeline/Compose.fs`.

**CLI verb.** `osm diff-policy --from minimal.toml --to strict.toml`

---

### H-034 — Cross-pass conflict detection

**Status:** proposed

**Gap.** Multiple passes may emit competing `DiagnosticEntry` values for
the same `SsKey`. `NullabilityPass` and a hypothetical `TighteningPass`
may both flag the same column with incompatible recommendations. This
is currently invisible.

**Implementation.** After all passes complete, join the lineage trails
on `SsKey`:

```fsharp
val detectConflicts :
    LineageEvent list -> DiagnosticEntry list ->
    PolicyConflict list

type PolicyConflict = {
    Key   : SsKey
    Left  : DiagnosticEntry
    Right : DiagnosticEntry
    Axes  : OverlayAxis list
}
```

Conflicts surface as a dedicated section in the manifest.

**Location.** `src/Projection.Pipeline/Compose.fs` or a new
`src/Projection.Core/ConflictDetector.fs`.

---

### H-035 — Policy regression testing framework

**Status:** proposed

**Gap.** There is no way to assert that a policy change does not
inadvertently change the DDL surface for `SsKey` values unrelated to
the change. The canary tests determinism under a fixed policy; it does
not test isolation of policy changes.

**Implementation.** A property test:

```fsharp
[<Property>]
let ``policy change on axis X does not affect SsKey outside axis X's scope``
    (policy1 : Policy) (axis : OverlayAxis) (delta : PolicyDelta) =
    let policy2 = Policy.applyDelta policy1 axis delta
    let ssdtBefore = Compose.project catalog policy1 profile
    let ssdtAfter  = Compose.project catalog policy2 profile
    diffDdl ssdtBefore ssdtAfter
    |> List.forall (fun change -> change.AffectedAxis = Some axis)
```

**Location.** `tests/Projection.Tests/PolicyIsolationTests.fs`.

---

### H-036 — v2 skeleton-only CLI verb (osm skeleton)

**Status:** proposed

**Gap.** `Compose.runSkeleton` is functionally complete — it runs the
DataIntent-only pipeline (4 passes, `Policy.empty`, no operator
overlays) and produces a DDL skeleton. It is not exposed as a CLI verb.

**Location.** `src/Projection.Cli/Program.fs`.

**Implementation.** `osm skeleton` calls `Compose.runSkeleton` and
emits the result as SSDT DDL. The manifest for the skeleton run
contains only `DataIntent` lineage events; the `AppliedOverlays`
section is empty. This is the reference output: the DDL that the schema
mandates without any operator opinion.

**Unlocks.** The skeleton is the baseline for H-033 (policy diff):
`osm diff-policy --from skeleton --to policy.toml`.

---

## Group VI — Graph topology

`TopologicalOrderPass` computes significantly more structure than it
surfaces. These items expose the latent graph intelligence.

---

### H-037 — Schema island detection

**Status:** proposed

**Gap.** `TopologicalOrderPass.tarjanScc` runs the full Tarjan
strongly-connected-component algorithm. Single-node SCCs (isolated
tables with no FK relationships) are discarded. Schema islands —
subgraphs disconnected from the main FK graph — are detectable by
finding SCCs whose `SsKey` sets share no FK edges with any other SCC.

**Location.** `src/Projection.Core/Passes/TopologicalOrderPass.fs`.

**Implementation.** One-line change: after computing SCCs, group them
into connected components of the SCC DAG. Components with no edges to
any other component are islands. Emit a `DiagnosticEntry` per island
naming its constituent `SsKey` values.

**Unlocks.** Island detection identifies orphaned table clusters — a
schema evolution smell that frequently indicates a module boundary
should be formalized or a FK relationship is missing.

---

### H-038 — Parallel deployment batch surface

**Status:** proposed

**Gap.** `TopologicalOrder.levels` already computes the set of
`SsKey` values at each topological level (tables that can be deployed
in parallel without FK constraint violations). This is not surfaced.

**Location.** `src/Projection.Core/Passes/TopologicalOrderPass.fs` and
`src/Projection.Targets.SSDT/ManifestEmitter.fs`.

**Implementation.** Add a `DeploymentBatches : SsKey list list` field
to the manifest. Each inner list is a set of tables safe to deploy in
the same transaction batch. The outer list is the deployment order.

**Unlocks.** Zero-downtime schema deployments can use the batch list to
parallelize DDL execution. For a 300-table schema, the maximum-parallel
batch depth is typically 8–12 levels; deploying in batch order reduces
deploy time by the number of parallelisable levels.

---

### H-039 — Cascade shock zone detection

**Status:** proposed

**Gap.** `CycleResolution.classify` tags FK edges as `Cascade`,
`Weak`, or `Other`. A cascade shock zone is a subgraph where a DELETE
on one table cascades to N tables through a chain of `ON DELETE CASCADE`
edges. These are not detected or reported.

**Location.** `src/Projection.Core/Passes/TopologicalOrderPass.fs`.

**Implementation.** A traversal from each `Cascade`-tagged FK edge,
following the cascade graph depth-first. The shock zone is the set of
reachable tables; its size is the fan-out. Emit a `DiagnosticEntry` per
shock zone exceeding a threshold (default: fan-out ≥ 3).

---

### H-040 — JunctionDeferred mode surface

**Status:** proposed

**Gap.** `JunctionDeferred` is a topological sort policy for junction
tables (many-to-many tables with two FK references). In `JunctionDeferred`
mode, these tables are scheduled in a second pass after their referents.
This mode exists in the sort algorithm but is not exposed as an
`EmissionPolicy` option.

**Location.** `src/Projection.Core/Policy.fs` (add `JunctionDeferred`
to `EmissionPolicy`) and `src/Projection.Core/Passes/TopologicalOrderPass.fs`
(propagate the mode into the sort).

---

### H-041 — Topological level emission in manifest

**Status:** proposed

**Gap.** The deployment batch structure from H-038 is useful to tools
beyond the SSDT emitter. It should appear in the JSON manifest as a
first-class section so external tools can consume it without re-parsing
the DDL.

**Location.** `src/Projection.Targets.Json/JsonEmitter.fs` and
`src/Projection.Targets.SSDT/ManifestEmitter.fs`.

**Implementation.** Add `topologicalLevels : string[][] ` to the JSON
manifest output. Each inner array is the `SsKey.toString()` values for
one topological level.

---

## Group VII — Schema algebra and delta types

---

### H-042 — Schema algebra: Catalog.union / intersect / subtract

**Status:** proposed

**Gap.** Two `Catalog` values can be combined conceptually (a merged
schema, a common subset, or a difference) but there are no formal
operations for this. Multi-tenant schema management, schema
composition, and module boundary extraction all require schema algebra.

**Implementation sketch.**

```fsharp
/// Merge two Catalogs; conflict resolution is specified by policy.
val union     : ConflictPolicy -> Catalog -> Catalog -> Result<Catalog>

/// Retain only Kinds present in both Catalogs.
val intersect : Catalog -> Catalog -> Catalog

/// Remove from left all Kinds present in right.
val subtract  : Catalog -> Catalog -> Catalog
```

**Location.** New module `src/Projection.Core/CatalogAlgebra.fs`.

**Unlocks.** `Catalog.subtract base current` is `SchemaDelta.added`
(H-007). `Catalog.intersect v1 v2` produces the common schema between
V1 and V2 for canary comparison. `Catalog.union` enables composing
module-level schemas into a deployment catalog.

---

### H-043 — Catalog diff as first-class type (completing SchemaDelta)

**Status:** proposed

**Gap.** `CatalogDiff` (chapter 3.5) compares two Catalogs for
roundtrip fidelity. `SchemaDelta` (H-007) describes the semantic
difference. These are related but distinct: `CatalogDiff` is a
fidelity check; `SchemaDelta` is a migration descriptor.

**Location.** `src/Projection.Core/SchemaDelta.fs` (new, per H-007),
referencing the existing `CatalogDiff` type in
`src/Projection.Core/Catalog.fs`.

**Implementation.** `SchemaDelta.fromDiff : CatalogDiff -> SchemaDelta`
converts the roundtrip-fidelity diff into the migration-oriented delta.
The reverse is partial: `SchemaDelta.toDiff : SchemaDelta -> CatalogDiff`
is defined only for diffs that don't involve schema renames.

---

### H-044 — Schema versioning and history

**Status:** proposed

**Gap.** The pipeline is stateless with respect to time. There is no
model of "this `Catalog` at version N" or "this `SsKey` was renamed
between version N-1 and N".

**Implementation sketch.**

```fsharp
type VersionedCatalog = {
    Version : SemVer
    At      : DateTimeOffset
    Schema  : Catalog
}

type SchemaHistory = VersionedCatalog list

val deltasBetween : SchemaHistory -> SchemaDelta list
val changelogFor  : SsKey -> SchemaHistory -> SchemaDelta list
```

**Location.** New module `src/Projection.Core/SchemaHistory.fs`.

**Unlocks.** A per-`SsKey` changelog: every structural change to a
column or table over time, with a timestamp and the policy that was in
effect. The lineage trail for a single `Kind` across all pipeline runs
becomes its complete history.

---

## Group VIII — Certification and observability

---

### H-045 — Registry pass dependency graph

**Status:** proposed

**Gap.** The `TransformRegistry` records every registered pass with its
`StageBinding` and `Classification`. It does not record which passes
depend on the output of which other passes. The dependency structure is
implicit in `Compose.fs`.

**Location.** `src/Projection.Core/TransformRegistry.fs`.

**Implementation.** Add a `DependsOn : TransformName list` field to
`RegisteredTransformMetadata`. `TransformRegistry.dependencyGraph`
computes a DAG from these declarations. The DAG is the static
specification of the pass graph; H-011 (incremental computation)
derives the dynamic dependency graph from lineage trails at runtime.

**Unlocks.** The `osm explain` verb can show the dependency chain for
any given `SsKey`: which passes ran, in what order, and which passes
depended on which predecessors' outputs.

---

### H-046 — Pass idempotence verification

**Status:** proposed

**Gap.** Each pass is expected to be idempotent: running it twice on
the same `Catalog` should produce the same output. This is not tested
structurally.

**Location.** `tests/Projection.Tests/PassIdempotenceTests.fs`.

**Implementation.**

```fsharp
[<Property>]
let ``every pass is idempotent`` (catalog : Catalog) (policy : Policy) =
    let run1 = NullabilityPass.run catalog policy Profile.empty
    let run2 = NullabilityPass.run run1.Value.Value policy Profile.empty
    run1.Value.Value = run2.Value.Value
```

Applied to every registered pass. `PassChainAdapter.compose` inherits
idempotence from the passes; the property test for the composed pipeline
follows from composition.

---

### H-047 — Automated v1↔v2 registry audit

**Status:** proposed

**Gap.** The V1 Parity Audit Wave (chapter 5.1–5.8) was a manual
cross-reference exercise. There is no automated check that every V1
transformation present in the V1 codebase is either registered in the
V2 `TransformRegistry` (as `Status = Implemented`) or has an explicit
`Status = NotImplementedInV2 of rationale` entry.

**Location.** New audit tool `tools/v1-registry-audit.fsx` or a test
in `tests/Projection.Tests/RegistryAuditTests.fs`.

**Implementation.** Parse V1's transformation entry points (statically,
from the V1 source via the `ADMIRE.md` carbon-copy index); compare
against the V2 registry. Any V1 transformation not present in either
category is a gap. The audit produces a row per gap, which becomes a
`BACKLOG.md` parity item.

---

### H-048 — Manifest as an externally verifiable artifact

**Status:** proposed

**Gap.** The manifest's `RegistryDigest` (SHA256 over sorted transform
metadata) can be re-derived from the registry and compared against the
manifest's recorded digest. This comparison is not exposed as a
standalone verification tool.

**Location.** New verb `src/Projection.Cli/Program.fs` and
`src/Projection.Pipeline/ManifestVerifier.fs`.

**Implementation.** `osm verify <manifest.json> <registry-snapshot.json>`
recomputes the `RegistryDigest` from the registry snapshot and asserts
it matches the manifest's recorded value. Mismatches indicate that the
pipeline that produced the manifest is not the same pipeline as the one
currently installed.

**Unlocks.** Audit trails: a manifest can be verified against the
registry at the time of emission, months or years later. This is the
proof-carrying property made operational.

---

### H-049 — RegistryDigest stability tests across refactors

**Status:** proposed

**Gap.** The `RegistryDigest` is supposed to be a stable fingerprint of
the pipeline. But it is not tested for stability: renaming a pass,
reordering fields in `RegisteredTransformMetadata`, or changing a
`TransformSite.Rationale` string will silently change the digest.

**Location.** `tests/Projection.Tests/RegistryDigestTests.fs`.

**Implementation.** A snapshot test: record the current digest as a
string constant; assert it matches on every CI run. Any change to the
digest must be acknowledged explicitly by updating the snapshot.
Pairs with a DECISIONS entry naming the rationale for the digest
change.

---

## Group IX — Testing and verification laws

---

### H-050 — Property tests for adjunction laws (emitter / reader roundtrip)

**Status:** proposed

**Gap.** The emitter (`Catalog → DDL`) and reader (`DDL → Catalog`) form
an adjunction: `reader ∘ emitter = id` (up to named lossy fields). The
canary tests this on specific fixtures. Property-based testing would
sweep the space of generated `Catalog` values.

**Location.** `tests/Projection.Tests/AdjunctionLawTests.fs`.

**Implementation.**

```fsharp
[<Property>]
let ``emitter-reader roundtrip preserves structural equality``
    (catalog : Catalog) =
    let ddl       = SsdtDdlEmitter.emit catalog Policy.empty Profile.empty
    let recovered = ReadSide.parseDdl ddl
    CatalogDiff.compute catalog recovered = CatalogDiff.empty
```

The `Catalog` generator for FsCheck must be constrained to well-formed
catalogs (smart constructor invariants). `CatalogDiff.empty` is the
witness that the roundtrip is lossless.

---

### H-051 — Kleisli law tests (identity and associativity)

**Status:** proposed

**Gap.** `PassChainAdapter.compose` is Kleisli composition. The Kleisli
category laws — left identity, right identity, and associativity — are
not tested.

**Location.** `tests/Projection.Tests/KleisliLawTests.fs`.

**Implementation.**

```fsharp
[<Property>]
let ``left identity: compose (return ∘ f) g = g`` ...
[<Property>]
let ``right identity: compose f (return ∘ id) = f`` ...
[<Property>]
let ``associativity: compose (compose f g) h = compose f (compose g h)`` ...
```

The `return` here is `Lineage.ofValue`; the composition is
`PassChainAdapter.compose`. All three laws follow from `Lineage.bind`
satisfying monad laws; the tests make the guarantee explicit.

---

### H-052 — Skeleton purity and overlay exercise property tests

**Status:** proposed

**Gap.** Per pillar 9 (`DECISIONS 2026-05-15 (late)`), the
bidirectional dichotomy contract requires two property tests:
(1) skeleton-purity: `Compose.runSkeleton` emits zero `OperatorIntent`
events; (2) overlay-exercise: every registered `OperatorIntent` fires
in the canary. These are named as requirements but not yet tests.

**Location.** `tests/Projection.Tests/PillarNineTests.fs`.

---

### H-053 — Lineage monad law tests (return and bind)

**Status:** proposed

**Gap.** `Lineage` is a monad. The monad laws — left identity, right
identity, and associativity of `bind` — are not tested explicitly.

**Location.** `tests/Projection.Tests/LineageLawTests.fs`.

**Implementation.**

```fsharp
[<Property>]
let ``left identity: bind (return a) f = f a`` ...
[<Property>]
let ``right identity: bind m return = m`` ...
[<Property>]
let ``associativity: bind (bind m f) g = bind m (fun x -> bind (f x) g)`` ...
```

These tests are the structural underwriting for H-001 (CE builder):
a computation expression built on a law-verified monad is formally
correct.

---

### H-054 — Policy simulation property tests

**Status:** proposed

**Gap.** H-033 (policy diff) computes the delta between two policy
runs. The delta should satisfy: (1) if `policy1 = policy2`, the delta
is empty; (2) if `policy2 = Policy.empty`, the delta is the full
overlay set; (3) `applyDelta (applyDelta base p1) p2 = applyDelta base
(p1 ∪ p2)` for independent axes.

**Location.** `tests/Projection.Tests/PolicyDiffTests.fs`.

---

## Group X — Targets and emission

---

### H-055 — JSON target: per-column statistical profile section

**Status:** proposed

**Gap.** The JSON emitter produces a structural representation of the
`Catalog`. It does not include statistical evidence from the `Profile`.
A per-column profile section — null rate, P50/P95/P99, mean, StdDev,
unique count ratio — would make the JSON output a complete data
dictionary.

**Location.** `src/Projection.Targets.Json/JsonEmitter.fs`.

**Implementation.** Add a `"profile"` section to each column object in
the JSON output, populated from `ColumnProfile` and
`NumericDistribution` when available. When `Profile.empty` is provided,
the section is omitted. The `A18 amended` commitment (`Π` never
consumes `Policy`) holds: `Profile` is evidence, not intent.

---

### H-056 — Distributions target: manifest-aware bundle

**Status:** proposed

**Gap.** The Distributions emitter produces a standalone bundle. It
does not include a manifest. A `DistributionManifest` analogous to
`SsdtManifest` would record the same `RegistryDigest`, `Coverage`, and
`AppliedOverlays` that the SSDT manifest records.

**Location.** `src/Projection.Targets.Distributions/DistributionsEmitter.fs`.

**Implementation.** Reuse `ManifestEmitter.build` with a
`DistributionManifest` output shape. H-009 (multi-target fanout) is the
prerequisite: the shared lineage trail makes the manifest's
`AppliedOverlays` identical across all three targets.

---

### H-057 — DACPAC target: DacFx typed-AST adoption

**Status:** proposed

**Gap.** Per `DECISIONS 2026-05-10` (text-builder-as-first-instinct
discipline), the chapter 3.x DacpacEmitter has a Tier-3 hard-
requirement deferral: it MUST adopt `Microsoft.SqlServer.Dac` (DacFx)
typed-AST construction. The current implementation uses
`ScriptDomBuild` + text-based `.dacpac` assembly.

**Location.** `src/Projection.Targets.SSDT/` (current DACPAC stub) →
`src/Projection.Adapters.OssysSql/` (C# adapter project, home for
DacFx-idiomatic code per V2 self-containment discipline).

**Implementation.** A C# adapter project wrapping `Microsoft.SqlServer.Dac`
API surfaces; an F# bridge module in `src/Projection.Targets.SSDT/`
consuming the bridge. The typed `.dacpac` construction replaces the
ScriptDom + text-assembly approach. Chapter 3.x pre-scope DACPAC
emitter document (`CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`) carries the
full scope.

---

### H-058 — ReadSide adapter: full-fidelity catalog reconstruction

**Status:** proposed

**Gap.** `ReadSide.readCatalog` reconstructs a `Catalog` from a live
SQL Server database (the DDL round-trip path). The reconstruction is
not full-fidelity for all IR fields added at chapter A.0' —
specifically `ColumnCheck`, `Trigger`, `Sequence`, and temporal
metadata are not reconstructed from the SQL Server system catalog.

**Location.** `src/Projection.Adapters.Sql/ReadSide.fs`.

**Implementation.** Add system-catalog queries for each missing IR
field:
- `sys.check_constraints` → `ColumnCheck`
- `sys.triggers` → `Trigger`
- `sys.sequences` → `Sequence`
- `sys.periods` + `temporal_type` → `TemporalConfig`

Each is a new `EvidenceCache` derivation (per the
discovery-then-derive pattern from `DECISIONS 2026-05-19`). No new SQL
round-trips per attribute; new derivation functions over the existing
cache substrate where possible.

---

### H-059 — Operator-facing schema report (osm report verb)

**Status:** proposed

**Gap.** The three diagnostic artifact targets (decision log,
opportunities, validations) are machine-readable JSON. There is no
single operator-facing human-readable report that integrates topology,
statistics, coverage, and diagnostics into a structured document.

**Location.** New `src/Projection.Targets.OperationalDiagnostics/SchemaReportEmitter.fs`
+ `src/Projection.Cli/Program.fs` (`osm report` verb).

**Implementation.** A Markdown or structured-text emitter that produces:
1. Schema summary (module / kind / attribute counts)
2. Topology section (deployment batches from H-038, islands from H-037,
   cascade zones from H-039)
3. Statistical evidence section (per-kind null rates, FK cardinalities,
   distribution summaries from H-027)
4. Coverage section (from manifest)
5. Diagnostics section (opportunities, sorted by the DiagnosticLattice
   partial order from H-008)
6. Registry section (pipeline fingerprint, applied overlays)

The report is the proof-carrying document in a form an operator can
read, review, and attach to a deployment sign-off.

---

## Cross-cutting notes

**Dependency order.** Several items here are prerequisite to others:

- H-003 (Kleisli explicit) → H-006 (parallel composition)
- H-005 (branching lineage) → H-033 (policy diff), H-034 (cross-pass
  conflict detection)
- H-007 (SchemaDelta) → H-042 (schema algebra), H-043 (delta passes)
- H-004 (Certificate) → H-009 (multi-target fanout), H-048 (external
  verification)
- H-012 (active patterns) → simplification of passes that today have
  nested SsKey matches
- H-031 (SuggestedConfig population) → H-032 (suggest-config verb)
- H-037 (schema islands) → H-041 (deployment batches surface) → H-038

**Priority tiers.**

- **Tier 1 — Immediate craft dividend:** H-001 (Lineage CE), H-002
  (LineageDiagnostics CE), H-003 (Kleisli explicit), H-012 (active
  patterns), H-031 (SuggestedConfig), H-036 (skeleton verb), H-037
  (island detection), H-038 (deployment batches). Each is small, has
  no prerequisites, and makes the existing structure more visible or
  more usable.

- **Tier 2 — Statistical evidence closure:** H-024 through H-030 and
  H-033 (Faker). The LiveProfiler already computes everything needed;
  these items are consumers.

- **Tier 3 — Kernel growth:** H-005 (branching lineage), H-006
  (parallel passes), H-007 (SchemaDelta), H-008 (DiagnosticLattice),
  H-010 (Prism), H-016 (Policy DSL). Each extends the formal
  machinery with a new type or composition operator.

- **Tier 4 — Schema algebra and full system realization:** H-042
  through H-044, H-055 through H-059. These complete the
  vision of the system as a formal schema intelligence platform.

---

---

## Group XI — Deeper categorical structure

The first ten kernel items name the existing categorical structure and
add new composition operators. This group goes one layer further: the
structures between structures — natural transformations, profunctors,
comonads, and the limits/colimits that govern how schemas combine.

---

### H-060 — Natural transformation between PolicyExpr and Policy record

**Status:** proposed

**Gap.** H-016 proposes a `PolicyExpr` combinator language whose
`eval : PolicyExpr -> Policy` produces the familiar `Policy` record.
This is a natural transformation between two functors over the policy
domain — the free combinator functor and the record functor. The
natural transformation laws (composition preservation, identity
preservation) are not stated and not tested.

**Location.** `src/Projection.Core/PolicyExpr.fs` (H-016).

**Implementation.** Add a `PolicyExpr.naturalTransform` module with
the two laws as property tests:

```fsharp
// naturality square: eval (fmap f expr) = fmap' f (eval expr)
// identity:          eval PolicyExpr.identity = Policy.empty
```

**Unlocks.** The `eval` function is not just a convenient converter; it
is a structure-preserving map. The laws make it impossible to write a
combinator that evaluates to a different policy depending on which
`eval` path is taken. Round-trip tests for the config parser follow
from the natural transformation laws.

---

### H-061 — Profunctor for bidirectional pass transformation

**Status:** proposed

**Gap.** A pass `Catalog → Lineage<Diagnostics<Catalog>>` can be
composed forward (map the output) or backward (contramap the input).
There is no `Profunctor` abstraction expressing both directions
simultaneously, so adapters that need to reshape inputs before a pass
and outputs after it must do so in two separate expressions.

**Implementation sketch.**

```fsharp
type Profunctor<'a, 'b> = {
    DiMap : ('c -> 'a) -> ('b -> 'd) -> Profunctor<'c, 'd>
}

val passAsProfunctor : Pass<'a, 'b> -> Profunctor<'a, 'b>
```

**Location.** New module `src/Projection.Core/Profunctor.fs`.

**Unlocks.** Pass adaptation — reshaping a pass to fit a different
input/output type without losing the lineage — becomes a single
`diMap` call. The adapter layer in `Projection.Adapters.*` has several
sites where input reshaping and output reshaping are currently split
across two `|> map` calls; `diMap` unifies them.

---

### H-062 — Reader comonad for Policy + Profile context propagation

**Status:** proposed

**Gap.** Every pass takes `Policy` and `Profile` as parameters. This
is the reader monad pattern applied at the parameter level. The dual
of a reader monad is a reader comonad — a value `'a` paired with the
context `Policy * Profile` that produced it, with `extract` projecting
out the value and `extend` threading context through a chain of
computations.

**Implementation sketch.**

```fsharp
type PassContext<'a> = {
    Value   : 'a
    Policy  : Policy
    Profile : Profile
}

val extend  : (PassContext<'a> -> 'b) -> PassContext<'a> -> PassContext<'b>
val extract : PassContext<'a> -> 'a
```

**Location.** New module `src/Projection.Core/PassContext.fs`.

**Unlocks.** A pass chain written with `extend` carries `Policy` and
`Profile` through the chain implicitly; each pass sees the full context
without taking it as an explicit parameter. The comonad laws (left
identity, right identity, associativity of `extend`) are
property-testable. This is the context-propagation dual of the
Lineage writer monad; together they characterize the full algebraic
structure of the pipeline.

---

### H-063 — Free monad for pass scheduling as a first-class DSL

**Status:** proposed

**Gap.** The pass chain in `Compose.fs` is a fixed sequence of
`PassChainAdapter.compose` calls. A free monad over the pass DSL
would allow the pass chain to be described as a data structure (an
AST) and interpreted in multiple ways: sequentially (the current
default), in parallel (H-006), or as a dependency-ordered graph
(H-011).

**Implementation sketch.**

```fsharp
type PassF<'a> =
    | RunPass  of Pass<Catalog, Catalog> * 'a
    | Parallel of PassF<'a> list * 'a
    | Done     of 'a

type PassProgram<'a> = Free<PassF, 'a>

val sequential : PassProgram<Catalog> -> Lineage<Diagnostics<Catalog>>
val parallel   : PassProgram<Catalog> -> Lineage<Diagnostics<Catalog>>
val dryRun     : PassProgram<Catalog> -> string list  // pass names in order
```

**Location.** New module `src/Projection.Pipeline/PassProgram.fs`.

**Unlocks.** The three interpreters — sequential, parallel, dry-run —
are the same `PassProgram<Catalog>` value evaluated differently. `dryRun`
becomes the implementation of `osm explain`: it lists the pass chain
without executing it. H-006 (parallel composition) becomes a derived
interpreter rather than a new composition operator.

---

### H-064 — Colimits in the schema category (coproduct and pushout)

**Status:** proposed

**Gap.** H-042 proposes `Catalog.union / intersect / subtract` as
schema algebra. The categorical underpinning is that `Catalog.union`
is a pushout (the amalgamation of two schemas over a shared sub-schema)
and `Catalog.intersect` is a pullback (the common structure). These
operations are currently proposed as ad-hoc functions; naming the
categorical structure they instantiate makes composition laws provable.

**Implementation.** `CatalogAlgebra.pushout` takes two Catalogs and a
shared sub-Catalog (their overlap) and produces the amalgamated result.
`CatalogAlgebra.pullback` computes the intersection. Both satisfy
universal-property laws testable as property tests.

**Location.** `src/Projection.Core/CatalogAlgebra.fs` (H-042).

**Unlocks.** Multi-module schema composition becomes a sequence of
pushouts. Schema decomposition (extract a sub-module) becomes a
pullback. The laws guarantee that composed schemas are consistent and
that decomposition is the left inverse of composition.

---

### H-065 — Yoneda embedding applied to the TransformRegistry

**Status:** proposed

**Gap.** The Yoneda lemma states that a functor `F` is completely
determined by its set of natural transformations from the representable
functor `Hom(A, -)`. Applied to the registry: every registered
transform is completely determined by how it maps every possible
`Catalog` to a lineage-annotated result. The registry's SHA256 digest
is a Yoneda-style proof: it identifies the transform uniquely by its
metadata, not by its function pointer.

**Implementation.** A `TransformRegistry.yonedaProbe` function that,
given a probe `Catalog`, runs every registered transform and records
the lineage events produced. The resulting table is a Yoneda embedding
of the registry: two registries are equivalent iff their probe results
are identical on every possible probe catalog.

**Location.** `src/Projection.Core/TransformRegistry.fs`.

**Unlocks.** The probe table is a stronger equivalence certificate than
the SHA256 digest alone (which is sensitive to metadata string changes
but not to behavioral equivalence). Two differently-named transforms
that produce identical lineage traces on every probe catalog are
behaviorally identical — a fact the digest cannot express but the
Yoneda probe can detect.

---

## Group XII — Advanced F# type system

---

### H-066 — Recursive descent parser CE for Policy config

**Status:** proposed

**Gap.** `Policy.fs`'s config parser is approximately 700 lines of
nested `match` / `Result.bind` chains. It is the largest single
function in the codebase and the hardest to extend: adding a new
policy axis requires threading through N layers of `match`.

**Implementation.** A recursive descent parser computation expression:

```fsharp
type Parser<'a> = JsonNode -> Result<'a, ParseError list>

let parser = ParserBuilder()

let parsePolicy : Parser<Policy> = parser {
    let! selection  = parseSelectionPolicy
    let! emission   = parseEmissionPolicy
    let! tightening = parseTighteningPolicy
    let! insertion  = parseInsertionPolicy
    return Policy.create selection emission tightening insertion
}
```

The builder supports `let!` (bind), `return` (lift), and `yield!`
(collect errors applicatively rather than failing fast).

**Location.** `src/Projection.Core/Policy.fs`.

**Unlocks.** Adding a new policy axis is a new `let!` binding in
`parsePolicy` plus a new `parseFooAxis` leaf function. The blast
radius drops from N match-arm sites to 1. Error collection becomes
applicative: all parse errors surface together rather than stopping at
the first failure.

---

### H-067 — Statically-resolved type parameters for zero-overhead pass composition

**Status:** proposed

**Gap.** `PassChainAdapter.compose` takes pass functions as first-class
values. Each composition step introduces an indirect function call
through a closure. On the operator-reality canary (300 tables, 12
passes), this is approximately 3600 indirect calls per pipeline run —
small but measurable in the bench output.

**Implementation.** Inline-able composition via SRTP constraints:

```fsharp
let inline compose
    (^P1 : (member Run : Catalog -> Lineage<Diagnostics<Catalog>>))
    (^P2 : (member Run : Catalog -> Lineage<Diagnostics<Catalog>>))
    = ...
```

F# specializes the inline at each call site; the composition becomes
a direct call chain with no closure allocations.

**Location.** `src/Projection.Pipeline/PassChainAdapter.fs`.

**Trigger.** When bench data shows function-call overhead in the pass
composition hot path. Currently not a bottleneck; this is a
bench-driven optimization item per the protocol.

---

### H-068 — Measure-polymorphic statistical aggregation helpers

**Status:** proposed

**Gap.** H-013 proposes units of measure on Profile numerics. Once
adopted, statistical aggregation helpers (`mean`, `variance`,
`percentile`) become measure-polymorphic: `mean : 'a<[<Measure>]'u>
list -> 'a<[<Measure>]'u>`. The aggregation preserves the unit;
mixing units is a compile error.

**Implementation.**

```fsharp
let inline mean (xs : decimal<'u> list) : decimal<'u> =
    List.sum xs / decimal<'u> xs.Length

let inline variance (xs : decimal<'u> list) : decimal<'u ^ 2> = ...
```

**Location.** `src/Projection.Core/Profile.fs`.

**Unlocks.** Every statistical computation in `NullabilityRules`,
`CategoricalUniquenessRules`, and future rules is dimensionally safe.
The helpers are a thin wrapper over the standard `List` functions; the
cost is zero at runtime; the benefit is type-checked dimensional
consistency.

---

### H-069 — SqlIdentifier value object (constrained string VO)

**Status:** proposed

**Gap.** SQL identifiers (table names, column names, schema names) are
represented as `string` throughout the codebase. Nothing prevents
passing a column name where a schema name is expected. `Name` is a
presentation-only VO; `SsKey` is an identity VO. There is no VO
specifically for the SQL-level identifier that must satisfy SQL Server
identifier rules (max 128 chars, no embedded NUL, bracket-escapable).

**Implementation.**

```fsharp
type SqlIdentifier = private SqlIdentifier of string

module SqlIdentifier =
    val create    : string -> Result<SqlIdentifier, IdentifierError>
    val bracket   : SqlIdentifier -> string   // [identifier]
    val unescaped : SqlIdentifier -> string
```

**Location.** New module `src/Projection.Core/SqlIdentifier.fs`.

**Unlocks.** Every site that today passes a bare `string` to
`buildCreateTable` or `buildCreateIndex` becomes a `SqlIdentifier`
parameter. SQL injection via schema names at the boundary becomes a
compile-time impossibility (the adapter must create a `SqlIdentifier`
via `create`, which validates the string). Bracket-escaping is
centralized in one place.

---

### H-070 — Refinement-type lite: constrained IR field invariants

**Status:** proposed

**Gap.** Several IR field values have domain constraints that smart
constructors currently enforce at construction time but cannot be
expressed in the type signature:
- `ColumnProfile.RowCount >= 0`
- `NumericDistribution.P50 <= P95 <= P99`
- `CategoricalDistribution.Categories` has at least 1 entry

These are checked at construction time (smart constructors return
`Result`) but there is no type-level notation that makes the
constraint visible at the call site.

**Implementation.** A `Refined<'a, 'constraint>` newtype:

```fsharp
type NonNegative
type Monotone

type Refined<'a, 'c> = private Refined of 'a

module Refined =
    val create : ('a -> bool) -> 'a -> Result<Refined<'a, 'c>, string>
    val value  : Refined<'a, 'c> -> 'a
```

Field types become `Refined<int64, NonNegative>` and
`Refined<decimal list, Monotone>`. The constraint is visible in the
field type; consumers know at a glance that the value has been
validated.

**Location.** New module `src/Projection.Core/Refined.fs`.

---

## Group XIII — Schema intelligence features

These items extend the pipeline's analytical depth — from graph
metrics to profile anomaly detection to structural complexity scoring.

---

### H-071 — Schema centrality metrics (PageRank over the FK graph)

**Status:** proposed

**Gap.** The FK graph is computed by `TopologicalOrderPass` but no
centrality measure is derived from it. A table's centrality in the FK
graph is the strongest single predictor of: migration risk (high
centrality = many dependents), query performance (high centrality =
likely join hub), and schema evolution cost (high centrality = many
cascading changes).

**Implementation.** Personalized PageRank over the FK adjacency matrix
(`kind_i → kind_j` edge for each FK in `kind_i.References` pointing to
`kind_j`). The stationary distribution assigns a centrality score to
each `SsKey`. Convergence is guaranteed (the FK graph is finite and
typically sparse).

**Location.** New pass `src/Projection.Core/Passes/CentralityPass.fs`.

**Unlocks.** The manifest gains a `CentralityRanking` section — the
top-N most central tables by schema. The `osm report` verb (H-059)
includes centrality in the topology section. Policy suggestions for
high-centrality tables are flagged with elevated urgency.

---

### H-072 — Subgraph extraction for bounded context discovery

**Status:** proposed

**Gap.** A "bounded context" in Domain-Driven Design terms is a
self-consistent subset of the schema — a set of tables that are more
connected to each other than to the rest of the schema. The FK graph
encodes this structure; the community detection algorithm over it would
identify natural module boundaries.

**Implementation.** Louvain community detection (or Girvan-Newman edge
betweenness) over the FK graph. Each community is a candidate bounded
context. `Catalog.extractSubgraph(ssk set)` (a prerequisite: H-042
schema algebra) extracts the sub-catalog containing exactly those
`SsKey` values and the FK references between them.

**Location.** New pass `src/Projection.Core/Passes/BoundedContextPass.fs`.

**Unlocks.** `osm report` includes a "Suggested module boundaries"
section. The pipeline can be run on a sub-catalog extracted by bounded
context — enabling module-by-module migration strategies rather than
all-or-nothing deployments.

---

### H-073 — Anomaly detection in Profile (outlier column identification)

**Status:** proposed

**Gap.** The `Profile` carries per-column statistical evidence.
Columns whose statistical profile is anomalous relative to their peers
(unexpectedly high null rate, extreme cardinality, unusual value
distribution shape) are currently invisible. The statistical evidence
to detect them is present; no pass compares columns within the same
table or module.

**Implementation.** A `ProfileAnomalyPass` that, for each `Kind`:
1. Computes the inter-column null rate distribution
2. Flags columns whose null rate is more than 2σ above the table mean
3. Flags columns whose `coefficientOfVariation` is anomalously high
   relative to peer columns of the same data type
4. Emits `DiagnosticEntry` at `Severity = Info` per anomaly

**Location.** New pass `src/Projection.Core/Passes/ProfileAnomalyPass.fs`.

---

### H-074 — Multi-column functional dependency detection

**Status:** proposed

**Gap.** H-026 proposes consuming `JointDistribution` for pairwise
dependency detection. Multi-column functional dependencies (A, B → C)
require hypergraph analysis: a subset of columns determines another
column. These are the preconditions for database normalization
recommendations.

**Implementation.** A `FunctionalDependencyPass` that tests Armstrong's
axioms (reflexivity, augmentation, transitivity) empirically against
the profile data. When a candidate FD `X → Y` is detected with
statistical confidence above a threshold, the pass emits a
`DiagnosticEntry` suggesting normalization.

**Location.** New pass `src/Projection.Core/Passes/FunctionalDependencyPass.fs`.

**Unlocks.** Automated normalization advice. The pass is the
statistical dual of the static normalization checks in
`CategoricalUniquenessRules`; together they form a two-layer
normalization advisor (static structure + empirical evidence).

---

### H-075 — Schema complexity scoring

**Status:** proposed

**Gap.** There is no single metric that characterizes schema
complexity. Operators making migration decisions and schema evolution
trade-offs have no quantitative baseline.

**Implementation.** A composite `SchemaComplexity` record:

```fsharp
type SchemaComplexity = {
    CyclomaticComplexity  : decimal   // FK cycle count weighted by SCC size
    CouplingIndex         : decimal   // average FK fan-in per table
    CohesionIndex         : decimal   // FK density within each community (H-072)
    DepthOfInheritance    : decimal   // longest FK chain from leaf to root
    NullabilityRatio      : decimal   // fraction of columns that are nullable
    OverallScore          : decimal   // weighted composite
}
```

Computed by a `SchemaComplexityPass` from the topological order and
FK graph already available after `TopologicalOrderPass`.

**Location.** New pass `src/Projection.Core/Passes/SchemaComplexityPass.fs`.

**Unlocks.** Complexity appears in the manifest and in `osm report`.
Operators can track complexity over time (H-077) and correlate
complexity spikes with schema evolution events.

---

### H-076 — Query plan hint annotation emission

**Status:** proposed

**Gap.** The statistical evidence in `Profile` — FK selectivity,
cardinality, join distribution — is exactly the evidence SQL Server's
query optimizer uses to select query plans. V2 can emit `WITH
(INDEX(...))`, `NOLOCK`, and query store hints as extended properties
on high-traffic tables when evidence justifies them.

**Location.** New pass `src/Projection.Core/Passes/QueryHintPass.fs`
+ `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs` (emit extended
properties).

**Implementation.** When `ForeignKeySelectivity.EstimatedCost` exceeds
a threshold AND `Index.FillFactor` is default (80), the pass suggests
a lower fill factor via `SuggestedConfig` and emits a query-store hint
extended property.

---

### H-077 — Time-series profiling (schema evolution over multiple runs)

**Status:** proposed

**Gap.** Every pipeline run produces a fresh `Profile`. There is no
model of how the profile changes over time. Trending null rates, growing
cardinality, shifting distributions — these are the signals that
indicate schema health deterioration or data quality drift.

**Implementation.**

```fsharp
type ProfileTimeSeries = {
    At      : DateTimeOffset
    Profile : Profile
}

val trend : ProfileTimeSeries list -> Map<SsKey, TrendSummary>

type TrendSummary = {
    NullRateTrend    : Trend   // Rising | Stable | Falling
    CardinalityTrend : Trend
    DistributionDrift: decimal // KL divergence between first and last profile
}
```

**Location.** New module `src/Projection.Core/ProfileTimeSeries.fs`.

**Unlocks.** The `osm report` verb gains a "Schema health trends"
section. Alerts when `DistributionDrift` exceeds a threshold (data
quality regression). Pairs with H-091 (pipeline audit log) to build a
complete historical record.

---

## Group XIV — Multi-format emission targets

These items add new output formats. Each is a new sibling Π — a
target that consumes `Catalog × Profile` and emits a structured
artifact. The existing T11 (sibling-Π commutativity) and A18 amended
(no Policy in emitters) apply to all of them.

---

### H-078 — Entity Framework model generation

**Status:** proposed

**Gap.** The `Catalog` IR is a complete description of a relational
schema. Entity Framework's code-first model is a C# representation of
the same schema. The mapping is mechanical: `Kind` → `DbSet<TEntity>`,
`Attribute` → property, `Reference` → navigation property, `Index` →
`HasIndex` fluent call.

**Location.** New project `Projection.Targets.EntityFramework/`.

**Implementation.** A Roslyn `SyntaxFactory`-based emitter (C# AST,
not text) producing `DbContext` + per-entity class files. EF Core's
`HasColumnType`, `IsRequired`, `HasForeignKey` fluent API maps
directly from V2's IR fields. The emitter is a sibling Π; it never
touches `Policy`; all decisions about which entities to include were
already made by the selection passes.

**Unlocks.** From a V2 pipeline run, generate the Entity Framework
scaffolding for the schema. An OutSystems schema becomes a typed C#
model in one command. Pairs with H-080 (dbt) and H-081 (OpenAPI) for
multi-layer schema documentation.

---

### H-079 — dbt model generation

**Status:** proposed

**Gap.** dbt (data build tool) models are YAML `sources` + SQL `SELECT`
stubs. The `Catalog` IR carries everything needed: table names, column
names, types, descriptions, FK relationships.

**Location.** New project `Projection.Targets.Dbt/`.

**Implementation.** A YAML emitter (using `YamlDotNet` or
`Utf8JsonWriter` with YAML output) producing:
- `sources.yml`: one entry per `Module` with one table per `Kind`
- `schema.yml`: per-column descriptions from `Attribute.Description`
- `stg_<table>.sql`: passthrough SELECT stubs referencing source table

**Unlocks.** From a V2 pipeline run, generate the dbt staging layer
for a data warehouse that consumes the OutSystems transactional schema.
No manual scaffolding; schema evolution in V2 propagates to dbt via
a pipeline re-run.

---

### H-080 — Liquibase changelog generation

**Status:** proposed

**Gap.** `SchemaDelta` (H-007) describes the change between two
Catalogs. Liquibase's XML changelog format is a sequence of
`<changeSet>` elements that express the same delta. The mapping is
direct: `Added Kind` → `<createTable>`, `Removed Kind` → `<dropTable>`,
`Modified Attribute` → `<addColumn>` / `<modifyDataType>`.

**Location.** New project `Projection.Targets.Liquibase/`.

**Implementation.** A delta emitter (consumes `SchemaDelta`, not
`Catalog`; a delta pass, not a snapshot pass). The XML is produced via
`XDocument` / `XElement` (typed XML AST, per text-builder-as-first-
instinct discipline). Each `<changeSet>` carries an `id` derived from
the `SsKey` of the changed kind + the `RegistryDigest` as the `author`.

---

### H-081 — OpenAPI / JSON Schema emission

**Status:** proposed

**Gap.** REST APIs built on OutSystems schemas expose resources whose
shape matches the underlying tables. The `Catalog` IR describes these
shapes. An OpenAPI 3.1 spec with JSON Schema components is generatable
from `Kind` + `Attribute` + `Reference`.

**Location.** New project `Projection.Targets.OpenApi/`.

**Implementation.** `Kind` → `components/schemas/<Kind.Name>` with
properties derived from `Attribute` types. `Reference` → `$ref`
links between schemas. `Index.Filter` → `readOnly: true` hint for
filtered-index columns. The emitter uses `Utf8JsonWriter` (per A18:
no Policy consumed). The JSON Schema type mapping follows V2's
`SqlTypeCorrespondence` in reverse.

---

### H-082 — GraphQL schema emission

**Status:** proposed

**Gap.** GraphQL SDL (Schema Definition Language) is an alternative
type system for the same relational structure. `Kind` → `type`, `Attribute`
→ field, `Reference` → field of related type, nullable `Attribute` →
`FieldType` vs `FieldType!`.

**Location.** New project `Projection.Targets.GraphQL/`.

**Implementation.** A SDL text emitter. Unlike SQL DDL, GraphQL SDL
has no established typed-AST library for .NET; raw string construction
via `StringBuilder` is acceptable here (LINT-ALLOW substantive
rationale: no typed-AST library exists for SDL in the .NET ecosystem).
All `SsKey`-sorted, deterministic (T1).

---

### H-083 — Data Vault 2.0 Hub / Satellite / Link decomposition

**Status:** proposed

**Gap.** Data Vault 2.0 is a modeling methodology that decomposes
relational schemas into Hubs (business keys), Satellites (descriptive
attributes), and Links (relationships). The decomposition is
algorithmically derivable from the Catalog: each `Kind` with a primary
key becomes a Hub; its non-key attributes become a Satellite; each FK
`Reference` becomes a Link. The FK graph and `CentralityPass` (H-071)
inform which Hubs are load-bearing.

**Location.** New project `Projection.Targets.DataVault/`.

**Implementation.** A transformation pass (DataVault decomposition) +
a SSDT emitter for the Hub / Satellite / Link DDL. The pass is
`OperatorIntent Emission` (it restructures the schema; it is not in the
DataIntent skeleton). The resulting DDL is a sibling Π alongside the
regular SSDT emission.

---

### H-084 — Flyway versioned migration scripts

**Status:** proposed

**Gap.** Flyway uses versioned SQL scripts (`V1__description.sql`,
`V2__description.sql`) to manage schema evolution. `SchemaDelta`
(H-007) is the natural input: each delta produces one versioned script.
The script content is ScriptDom-generated DDL (per text-builder-as-
first-instinct discipline); the version number is derived from the
`RegistryDigest` prefix.

**Location.** New project `Projection.Targets.Flyway/`.

**Implementation.** A delta emitter: `SchemaDelta → string * DDLScript`
where the string is the Flyway filename and the DDLScript is the
ScriptDom-generated SQL. The emitter produces one script per
`SchemaDelta.Modified` + `Added` + `Removed` group, ordered by
topological dependency (H-038).

---

## Group XV — Operator experience

---

### H-085 — Policy versioning with SemVer

**Status:** proposed

**Gap.** A policy file is a static document with no version. When a
policy is updated, there is no record of what changed or when. The
pipeline can produce different DDL outputs from the same `Catalog`
under two policy versions, but this is invisible to the operator.

**Implementation.**

```fsharp
type VersionedPolicy = {
    Version : SemVer
    At      : DateTimeOffset
    Policy  : Policy
    ChangeLog : string option
}
```

The `SemVer` is computed from the hash of the serialized policy: a
changed policy produces a new version automatically. Major version bump
= removal or restriction of an existing axis; minor = addition; patch
= clarification (rationale string change only).

**Location.** New module `src/Projection.Core/VersionedPolicy.fs`.

**Unlocks.** The manifest records `PolicyVersion` alongside
`RegistryDigest`. Two manifests can be compared by policy version to
understand whether DDL differences are from schema changes or policy
changes.

---

### H-086 — Operator approval workflow for SuggestedConfigs

**Status:** proposed

**Gap.** `SuggestedConfig` (H-031 / H-032) emits a policy suggestion.
There is no workflow for an operator to accept, reject, or annotate the
suggestion. Accepted suggestions silently change the policy; rejected
suggestions are forgotten.

**Implementation.** An `ApprovalRecord` type:

```fsharp
type ApprovalDecision = Accept | Reject | Defer of DateTimeOffset
type ApprovalRecord = {
    DiagnosticCode : string
    SsKey          : SsKey
    Decision       : ApprovalDecision
    Note           : string option
    At             : DateTimeOffset
}
```

`osm approve <approval-record.json>` reads the approval record and
produces an updated policy file. Rejected suggestions are preserved as
`Skip`-equivalent entries in the policy: the diagnostic fires but
`SuggestedConfig` is suppressed for this key.

**Location.** New module `src/Projection.Core/ApprovalWorkflow.fs`
+ CLI verb in `src/Projection.Cli/Program.fs`.

---

### H-087 — Interactive policy tuning REPL

**Status:** proposed

**Gap.** Policy configuration today requires editing a TOML/JSON file,
re-running the pipeline, and inspecting the diagnostics. For a complex
policy, this loop may require dozens of iterations. An interactive REPL
would let the operator try a policy change, see the effect on
diagnostics, and commit or revert without leaving the terminal.

**Implementation.** A `Spectre.Console`-driven TUI:
- Display the current diagnostic summary (grouped by SsKey)
- Let the operator select a diagnostic and apply the SuggestedConfig
- Re-run the relevant pass (H-011 incremental computation) and update
  the display
- Output the final policy diff as a TOML stanza on exit

**Location.** New project `Projection.Cli.Tui/` (or extension of
`Projection.Cli`).

**Trigger.** When the operator workflow demands faster iteration than
the current edit-run-inspect cycle.

---

### H-088 — Schema diff viewer (terminal and HTML)

**Status:** proposed

**Gap.** `SchemaDelta` (H-007) produces a structured description of
changes between two Catalogs. There is no human-readable rendering of
this delta.

**Implementation.** Two renderers:

1. **Terminal:** `Spectre.Console` table with color coding (green =
   added, red = removed, yellow = modified). Per-column diff shows
   which fields changed.

2. **HTML:** A self-contained HTML file using inline CSS. Suitable for
   embedding in PR descriptions or deployment documentation.

**Location.** New module `src/Projection.Targets.SchemaDiff/`.

**CLI verb.** `osm diff --before before.json --after after.json
--format [terminal|html]`

---

### H-089 — Migration preview (dry-run with human-readable change list)

**Status:** proposed

**Gap.** Before running a migration, operators need to understand what
DDL will execute and in what order. The current pipeline produces DDL
without a preview mode.

**Implementation.** A `--dry-run` flag on `osm emit` that:
1. Runs the full pipeline
2. Produces the DDL as a statement stream (A35)
3. Renders each statement as a one-line summary: `CREATE TABLE [dbo].[Kind]`,
   `ALTER TABLE [dbo].[Kind] ADD [attr] INT NOT NULL`, etc.
4. Groups by topological batch (H-038) with batch numbers
5. Reports estimated risk per statement (DROP = High; ALTER = Medium;
   CREATE = Low) based on SsKey centrality from H-071

**Location.** `src/Projection.Cli/Program.fs` + new
`src/Projection.Targets.MigrationPreview/PreviewEmitter.fs`.

---

### H-090 — Pipeline audit log (full reproducibility record)

**Status:** proposed

**Gap.** There is no persistent record of pipeline runs. An operator
who ran `osm emit` last Tuesday cannot reconstruct which `Catalog`,
`Policy`, `Profile`, and registry were in effect without manually
saving each artifact.

**Implementation.** An `AuditLog` record written alongside the DDL
bundle:

```fsharp
type AuditRecord = {
    RunAt         : DateTimeOffset
    CatalogDigest : string         // SHA256 of serialized Catalog
    PolicyVersion : SemVer         // H-085
    ProfileDigest : string option  // SHA256 of Profile (None if Profile.empty)
    RegistryDigest: string         // from manifest
    OutputDigest  : string         // SHA256 of emitted DDL bundle
}
```

Given an `AuditRecord`, any operator with the same inputs can
reproduce the exact DDL output.

**Location.** `src/Projection.Pipeline/AuditLog.fs` +
`src/Projection.Cli/Program.fs` (write audit record on each emit).

---

## Group XVI — Infrastructure, extensibility, and scale

---

### H-091 — Plugin architecture for custom passes

**Status:** proposed

**Gap.** All passes are statically linked. An operator who needs a
custom pass (e.g., an org-specific naming convention enforcer, a
compliance check for a particular regulatory standard) must modify and
recompile the core codebase.

**Implementation.** A plugin contract:

```fsharp
type PassPlugin = {
    Name        : string
    Version     : string
    Stage       : StageBinding
    Run         : Catalog -> Policy -> Profile -> Lineage<Diagnostics<Catalog>>
}
```

The CLI discovers plugins from a configured directory
(`~/.osm/plugins/*.dll`). Each DLL exposes one or more `PassPlugin`
values. The registry loads them via reflection (out of scope for Core;
the plugin host lives in the CLI layer). Plugin passes are registered
in the `TransformRegistry` at startup; their `RegistryDigest` entry
changes when a plugin is added or removed.

**Location.** New module `src/Projection.Cli/PluginHost.fs`.

---

### H-092 — Streaming catalog loading for large schemas

**Status:** proposed

**Gap.** `Catalog.create` loads the full schema into memory. At 300
tables (the operator-reality canary), this is fine. At 10,000 tables
(enterprise-scale OutSystems installations), the full in-memory Catalog
may exhaust available memory before the pipeline begins.

**Implementation.** A streaming `CatalogReader` that emits
`CatalogChunk` values — bounded subsets of the Catalog (e.g., one
Module at a time) — via `AsyncStream`. Passes that operate on the full
Catalog (e.g., topological sort) buffer the full graph; passes that
operate per-Kind (e.g., NullabilityPass) process chunks without
materializing the full Catalog.

**Location.** `src/Projection.Adapters.Sql/StreamingCatalogReader.fs`.

**Trigger.** When a real schema exceeds available memory at 300-table
canary scale. Currently not a bottleneck; this is a capacity planning
item.

---

### H-093 — Manifest signing (Ed25519 signature over manifest digest)

**Status:** proposed

**Gap.** The manifest records a `RegistryDigest` (SHA256) and
`OutputDigest` (SHA256 of DDL bundle). These are integrity checks, not
authenticity checks. An adversary who can modify the manifest can
update the digests to match tampered content.

**Implementation.** An Ed25519 signature over the canonical JSON
serialization of the manifest. The signing key is operator-managed
(stored in a hardware token or secrets manager). `osm verify
<manifest.json> --key <public-key.pem>` checks the signature.

**Location.** `src/Projection.Cli/ManifestSigner.fs`.

**Unlocks.** Deployment pipelines can verify that the DDL bundle they
are about to execute was produced by an authorized pipeline run with
an unmodified registry. Compliance requirements that demand an audit
trail of DDL changes are satisfied structurally.

---

### H-094 — Schema registry integration

**Status:** proposed

**Gap.** The `Catalog` IR is V2's internal schema representation. Many
organizations maintain a schema registry (Confluent Schema Registry,
AWS Glue Data Catalog, Azure Purview) as the enterprise-wide schema
source of truth. V2 currently has no adapter for these registries.

**Implementation.** A new adapter family:

```fsharp
// src/Projection.Adapters.SchemaRegistry/
module ConfluentAdapter =
    val readCatalog : ConfluentConfig -> Task<Result<Catalog, AdapterError>>

module GlueAdapter =
    val readCatalog : GlueConfig -> Task<Result<Catalog, AdapterError>>
```

Each adapter maps the registry's schema representation to the V2
`Catalog` IR, following the same translation disciplines as the OSSYS
adapter (trace-before-fixture, three-class typology, ADMIRE entry per
adapter).

---

### H-095 — Deterministic build artifacts (bit-for-bit reproducible DDL)

**Status:** proposed

**Gap.** T1 (byte-determinism) asserts that `Project(catalog, policy,
profile)` is deterministic: the same inputs always produce the same
outputs. This is tested via the canary's `PhysicalSchema` roundtrip.
It is not tested in isolation as a property: "given the same inputs
on two different machines, with two different .NET runtimes, at two
different times, the output is byte-identical."

**Implementation.** A reproducibility test suite that:
1. Records the output of a pipeline run to a fixture file
2. Runs the pipeline again (different process, possibly different
   machine via CI) on the same inputs
3. Asserts byte-for-byte equality of the two outputs

The test is the structural proof of T1; the canary tests correctness;
the reproducibility test tests determinism.

**Location.** `tests/Projection.Tests/ReproducibilityTests.fs`.

**Known risks.** `DateTimeOffset` sources in `AuditRecord` (H-090)
must be injected, not sourced from `DateTimeOffset.UtcNow`. The
`Clock` abstraction already exists in the boundary layer; this test
will surface any remaining `Now` calls in the pipeline.

---

### H-096 — Mutation testing for strategy rules

**Status:** proposed

**Gap.** Property tests and example tests verify that strategies
produce correct outputs for given inputs. They do not verify that the
test suite would catch a subtle strategy bug — a reversed comparison
operator, an off-by-one in a threshold. Mutation testing inserts these
bugs and verifies the test suite kills the mutant.

**Implementation.** Stryker.NET (`dotnet-stryker`) over the
`src/Projection.Core/Strategies/` directory. Mutation operators: value
replacement (`>` → `>=`), branch negation, operator swap. A mutation
score of ≥80% killed mutants is the target.

**Location.** `bench/stryker-config.json` + CI step.

**Unlocks.** Confidence that the strategy test suite is not just green
but detecting. The strategy layer is the most semantically dense code
in the codebase; mutation testing is the appropriate verification
instrument for it.

---

### H-097 — Fuzz testing for the Policy config parser

**Status:** proposed

**Gap.** The Policy config parser (H-066 proposes replacing it;
regardless of implementation, it is a boundary function). Fuzz testing
exercises the parser with arbitrary byte sequences to find crashes,
hangs, and unexpected exceptions before adversarial inputs do.

**Implementation.** SharpFuzz + libFuzzer over
`Policy.parseFromString : string -> Result<Policy, ParseError list>`.
The fuzzer is seeded with valid policy fixtures; mutations explore the
boundary between valid and invalid inputs.

**Location.** `tests/Projection.FuzzTests/PolicyParserFuzz.fsx`.

**Unlocks.** Parser robustness against malformed operator-supplied
config files. The parser is the largest function in the codebase and
the most likely attack surface for a config-injection bug.

---

### H-098 — Model-based testing for the policy system

**Status:** proposed

**Gap.** H-054 proposes property tests for specific policy simulation
laws. Model-based testing is the generalization: define a simple model
of the policy system (a state machine over `PolicyExpr` transitions),
generate random sequences of policy operations, run both the model and
the real implementation, and assert they agree.

**Implementation.** `FsCheck.StateMachine` over the policy state
machine:
- States: `Policy` record values
- Transitions: `applyAxis`, `removeAxis`, `compose`, `reset`
- Model: a `Map<OverlayAxis, bool>` (axis present or absent)
- Agreement: `model.ActiveAxes = Set.ofList (Policy.activeAxes real)`

**Location.** `tests/Projection.Tests/PolicyStateMachineTests.fs`.

---

### H-099 — Remote pass execution (offload compute-intensive passes to a worker)

**Status:** proposed

**Gap.** The pipeline runs entirely in-process. For very large schemas
or compute-intensive passes (topological sort over 10k-table graphs,
multi-column functional dependency detection over large profiles),
offloading passes to a remote worker or a serverless function would
reduce local execution time.

**Implementation.** A `RemotePass<'In, 'Out>` adapter that serializes
the input, calls a remote endpoint, and deserializes the result back
into the Kleisli pipeline:

```fsharp
val remotePass :
    Uri -> Pass<Catalog, Catalog> -> Pass<Catalog, Catalog>
```

The remote endpoint is an `osm-worker` process that exposes a simple
HTTP API accepting serialized `Catalog` and returning serialized
`Lineage<Diagnostics<Catalog>>`.

**Location.** New project `Projection.Adapters.Remote/`.

**Trigger.** When bench data shows that a specific pass takes more than
50% of pipeline wall time at operator-reality canary scale and the pass
is embarrassingly parallelizable across independent `SsKey` sets.

---

### H-100 — AXIOMS.md executable type-checker (L3 axioms as runnable specs)

**Status:** proposed

**Gap.** `AXIOMS.md` is the formal system — A1–A41+ and T1–T11.
`PRODUCT_AXIOMS.md` is the product axiom layer. The
verifiability-triangle audit classifies each axiom into Bucket A
(fully underwritten) through Bucket D (named but unverified). Bucket D
axioms are aspirational statements with no executable witness.

**Implementation.** A `tests/Projection.Tests/AxiomTests.fs` suite
that maps every named axiom to a test:
- Bucket A axioms: test already exists, cross-referenced by name
- Bucket B axioms: property test generated from axiom statement
- Bucket C/D axioms: `Skip = "Axiom A<N>: pending — <axiom statement>"`

The test file is the executable form of AXIOMS.md. When a Bucket D
axiom is promoted to Bucket A, the `Skip` becomes a `[<Fact>]` or
`[<Property>]`. The audit cadence (verifiability-triangle audit) is
what keeps the two documents in sync.

**Location.** `tests/Projection.Tests/AxiomTests.fs`.

**Unlocks.** The formal system is no longer just documentation; it is
a live test suite. A contributor who reads `AXIOMS.md` can run the
axiom tests to see which claims are verified, which are aspirational,
and which are pending. The gap between documentation and behavior
becomes structurally visible.

---

## Cross-cutting notes (updated)

**Dependency order** (additions to the original list):

- H-016 (Policy DSL) → H-060 (natural transformation), H-085
  (policy versioning)
- H-007 (SchemaDelta) → H-080 (Liquibase), H-084 (Flyway), H-088
  (diff viewer)
- H-042 (schema algebra) → H-064 (colimits), H-072 (subgraph
  extraction)
- H-071 (centrality) → H-072 (bounded contexts), H-075 (complexity
  scoring), H-089 (migration preview risk)
- H-066 (parser CE) → H-097 (fuzz testing)
- H-093 (manifest signing) → H-090 (audit log, provides the
  artifact being signed)
- H-031 (SuggestedConfig) → H-086 (approval workflow)
- H-005 (branching lineage) → H-063 (free monad scheduling)

**Priority tiers** (extended):

- **Tier 1 — Immediate craft dividend:** H-001 through H-003,
  H-012, H-031, H-036, H-037, H-038 (original list). From the
  extension: H-066 (parser CE — highest single-function leverage
  in the codebase), H-069 (SqlIdentifier VO — closes a security
  surface), H-100 (axiom tests — makes the formal system live).

- **Tier 2 — Statistical evidence closure + intelligence features:**
  H-024 through H-030, H-033, H-071, H-073, H-074, H-075. The
  LiveProfiler computes the evidence; these items are the consumers
  and analysts.

- **Tier 3 — Kernel growth:** H-005 through H-011, H-016, H-060
  through H-065. Each adds new formal machinery — new types, new
  composition operators, new categorical structures.

- **Tier 4 — Multi-format targets:** H-078 through H-084. Each is
  a new sibling Π; they share the Catalog/Profile evidence and
  produce output for a different consumer ecosystem.

- **Tier 5 — Operator experience and infrastructure:** H-085
  through H-099. These complete the system as a production-grade
  platform: reproducibility, audit trails, plugins, signing, scale.

---

## Group XVII — Deferred IR completions and semantic gaps

Items in this group correspond to features that are explicitly named
in `BACKLOG.md`'s Active deferrals index — work that the V2 cutover
deliberately deferred with a named trigger. They land here as
HORIZON items because they are not cutover-blocking but represent
genuine schema fidelity gaps.

---

### H-101 — Column default value emission

**Status:** proposed

**Gap.** `Attribute.DefaultValue` (chapter A.0' slice γ/δ/ε) is
populated in the IR. `DEFAULT` constraint emission is deferred per
the Active deferrals index (`DECISIONS 2026-05-11`). The ScriptDom
`DefaultConstraintDefinition` is already used at the read side
(via `tryParseFilterWithDiagnostics`'s precedent); the emit side is
a symmetric gap.

**Location.** `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs`.

**Trigger.** Operator surfacing a table whose DEFAULT constraints are
silently dropped from the SSDT bundle. Currently the manifest's
`Unsupported` list would catch this, but only if the tolerance is
registered — which it is not yet.

---

### H-102 — Cross-module FK IR refinement

**Status:** proposed

**Gap.** Chapter 4.1.A slice 6 is deferred per the Active deferrals
index (`DECISIONS 2026-05-19`). Cross-module FKs (a table in
`Module A` references a table in `Module B`) require a
`Reference.TargetModule : SsKey option` field to unambiguously locate
the referent. Today the OSSYS adapter derives the target by
`SsKey` lookup within the same `Module`; cross-module references
silently resolve only if the `Kind.SsKey` is globally unique.

**Location.** `src/Projection.Core/Catalog.fs` (IR field addition) +
`src/Projection.Adapters.Osm/CatalogReader.fs` (populate) +
`src/Projection.Adapters.Sql/ReadSide.fs` (read back).

**Unlocks.** Cross-module FK roundtrip in the canary without relying
on SsKey global uniqueness. Also required for H-072 (bounded context
discovery) — the algorithm needs to know which FK edges cross module
boundaries.

---

### H-103 — Logical FK (without DB constraint) as first-class IR concept

**Status:** proposed

**Gap.** `HasLogicalForeignKeyWithoutDbConstraint` is one of the four
always-false `PredicateName` variants from chapter 4.4 (the other
three were resolved in chapters 4.5–4.7). A logical FK is a
relationship that exists semantically (by OutSystems platform
convention) but has no corresponding `FOREIGN KEY` constraint in the
database. The IR has no field for this distinction; the manifest
cannot evaluate the predicate.

**Location.** `src/Projection.Core/Catalog.fs` — add
`Reference.IsLogical : bool` field. Adapter: derive from OutSystems
model metadata (logical FKs appear in `osm_model.json` but not in
`INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS`).

**Unlocks.** The last always-false `PredicateName` variant resolves.
Logical FKs are also the precondition for selective constraint
enforcement in the Tightening axis: an operator may want to enforce
a logical FK as a physical constraint, which is precisely the
purpose of `TighteningPolicy`.

---

### H-104 — Description consumers beyond IR storage

**Status:** proposed

**Gap.** `Kind.Description` and `Attribute.Description` were added at
chapter A.0' slice α and are populated by both adapters. Their only
consumer today is the `ExtendedProperty` emitter (module-level
descriptions emit as SQL Server extended properties). Column-level
and table-level descriptions do not yet appear in the JSON emitter
or in any diagnostic output.

**Location.** `src/Projection.Targets.Json/JsonEmitter.fs` (add
`"description"` field to column and table objects) +
`src/Projection.Targets.OperationalDiagnostics/SchemaReportEmitter.fs`
(H-059, use descriptions in the human-readable schema report).

**Unlocks.** The JSON target becomes a self-documenting data
dictionary — every column carries its description alongside its type.
The schema report gains prose descriptions without requiring the
operator to supply them separately.

---

### H-105 — CDC awareness in delta passes

**Status:** proposed

**Gap.** Chapter 4.1.B established CDC-silence-on-idempotent-redeploy
as the highest-leverage property. `Profile.CdcAwareness` carries
per-table CDC enrollment status. The `SchemaDelta` type (H-007) has
no CDC-aware ordering layer: when a CDC-tracked table changes
structure, the delta must include a CDC disable / re-enable cycle.
Without this, applying a `SchemaDelta` to a CDC-tracked table breaks
CDC enrollment.

**Location.** New `SchemaDelta.withCdcOrdering : CdcAwareness ->
SchemaDelta -> SchemaDelta` function. The CDC ordering is a
`DataIntent` enrichment (it preserves database behavior, not operator
intent).

**Unlocks.** The Liquibase (H-080) and Flyway (H-084) emitters produce
CDC-correct migration scripts without requiring operator knowledge
of CDC implications.

---

### H-106 — Schema normalization advisor

**Status:** proposed

**Gap.** The functional dependency pass (H-074) detects FDs from
evidence. The normalization advisor combines FD detection with static
structural analysis (PK shape, attribute dependencies) to classify
each `Kind` by normal form (1NF through BCNF) and emit recommendations
for each violation.

**Implementation.** A `NormalizationPass` that:
1. Detects repeating groups (1NF violations) from `CategoricalDistribution`
   showing array-encoded data
2. Detects partial key dependencies (2NF violations) from FKs where
   non-key attributes depend on a PK subset
3. Detects transitive dependencies (3NF violations) from the FD pass
4. Emits `DiagnosticEntry` per violation with `SuggestedConfig` naming
   the split that would resolve it

**Location.** New pass `src/Projection.Core/Passes/NormalizationPass.fs`.

---

### H-107 — Schema semantic versioning (breaking vs non-breaking classification)

**Status:** proposed

**Gap.** `SchemaDelta` (H-007) describes changes. It does not classify
them. Some changes are backward-compatible (adding a nullable column,
adding an index); others are breaking (dropping a column, narrowing a
type, adding a NOT NULL without a default). The classification is
deterministic from the delta.

**Implementation.**

```fsharp
type ChangeKind =
    | Breaking        of reason: string
    | NonBreaking
    | Conditional     of condition: string  // breaking only if consumers exist

val classify : SchemaDelta -> Map<SsKey, ChangeKind>
```

The classification produces a `SemVer` bump recommendation:
`Breaking` → major; new attributes → minor; pure metadata → patch.
Pairs with H-085 (policy versioning) and H-093 (manifest signing).

**Location.** `src/Projection.Core/SchemaDelta.fs` (H-007).

---

### H-108 — CI/CD integration templates

**Status:** proposed

**Gap.** The pipeline is a CLI tool. There are no canonical examples
of using it in a CI/CD pipeline. An operator setting up `osm emit`
in GitHub Actions, Azure DevOps, or GitLab CI must build the
integration from scratch.

**Implementation.** A `templates/` directory containing:
- `github-actions/schema-emit.yml` — run `osm emit`, commit the DDL
  bundle, fail if the manifest digest changed unexpectedly
- `azure-devops/schema-emit.yml` — equivalent for Azure Pipelines
- `gitlab-ci/schema-emit.yml` — equivalent for GitLab
- `Makefile` targets — `make emit`, `make verify`, `make diff`

Each template embeds the three key gates: verify the manifest digest
matches the registry, check for breaking changes (H-107), assert the
canary is green.

**Location.** New `templates/` directory at the `sidecar/projection`
root.

---

### H-109 — Schema lint rules (named structural conventions)

**Status:** proposed

**Gap.** The pipeline detects violations of structural invariants
(referential integrity, nullability, FK consistency). It does not
detect violations of naming or structural conventions (e.g., "all
PKs should be named `Id`", "all audit columns should be present on
every table", "no table should exceed 100 columns"). These are
operator-supplied conventions, not structural axioms.

**Implementation.** A `LintRule` type and a `LintPass`:

```fsharp
type LintRule = {
    Name    : string
    Check   : Kind -> Attribute list -> LintViolation option
}

type LintViolation = {
    Rule    : string
    Message : string
    Severity: DiagnosticSeverity
}
```

Rules are registered via the Policy system (OperatorIntent) and run
in `LintPass`. The operator supplies rules via config; the pass emits
`DiagnosticEntry` values for each violation.

**Location.** New pass `src/Projection.Core/Passes/LintPass.fs` +
`src/Projection.Core/Policy.fs` (LintPolicy axis).

---

### H-110 — Cross-environment schema consistency verification

**Status:** proposed

**Gap.** The canary asserts V1 ≈ V2 within one environment. The
operator runs V2 across four environments (Dev / Test / UAT / Prod).
There is no tool for asserting that the schema in Dev matches the
schema in Test modulo known environment-specific differences.

**Implementation.** `osm cross-env-verify` reads two manifests
(one per environment), computes the symmetric difference of their
`Coverage` and `RegistryDigest` sections, and classifies each
difference as either a known `ToleratedDivergence` or an unexpected
gap.

**Location.** New verb in `src/Projection.Cli/Program.fs` +
`src/Projection.Pipeline/CrossEnvVerifier.fs`.

---

## Grand synthesis: what this system is becoming

*This section is not a list of items. It is the argument for why the
items above form a coherent whole — what the ceiling looks like from
the inside.*

---

### I. The thesis

This system is not a schema exporter. A schema exporter takes a
database and produces DDL text. The defining property of an exporter
is that the output is correct if and only if it looks right. There is
no proof, no certificate, no trail. The operator checks the output by
eye and ships it.

This system occupies a fundamentally different position. It takes a
schema (`Catalog`), a behavioral contract (`Policy`), and statistical
evidence (`Profile`), and produces a **certified artifact**: DDL
plus a manifest that proves — at the level of SHA256 digests,
typed lineage events, and coverage measurements — that the emitted
DDL is the correct, complete, and justified translation of the
inputs. The manifest is the proof. The lineage trail is the
derivation. The pass chain is the formal system.

The closest analogy is a type checker. A type checker does not run
your program; it reasons about it. It tells you what is impossible,
what is required, and what can be safely omitted. Its output is a
certificate of type-correctness. This system does the same for
database schemas. The proof structure is there. Most of the items
in this document are about making that structure visible, tested,
and extended — not about building it from scratch.

---

### II. What is at stake

**At the craft level**, the question is whether F# and functional
programming's genuine advantages — algebraic types, monadic
composition, equational reasoning, law-driven testing — are being
fully exploited or merely gestured at. The answer today is: the
structure is correct, the exploitation is partial. The Kleisli
category exists but isn't named. The monad laws hold but aren't
tested. The writer monad is load-bearing but has no CE builder.
The policy system is well-typed but parsed by a 700-line match tree.
Every item in Groups I and II is a step toward a codebase that not
only uses functional programming but makes its functional nature
legible. When the CE builders exist, when the Kleisli structure is
named, when the policy DSL is typed, a reader can see the algebra
directly in the code. That is the craft dividend.

**At the system level**, the question is whether the pipeline
reasons about schemas or merely transforms them. The distinction is
this: a transformation takes an input and produces an output. A
reasoning system takes an input, makes decisions with evidence, and
produces an output together with an explanation of how the decisions
were made. The evidence layer — FK cardinality, selectivity,
statistical moments, joint distributions — is already computed.
None of it reaches the decision layer. Five IR types are populated
and never emitted. Five statistical constructs are computed and
never consumed. The policy simulation infrastructure is wired to
within one function of being usable. The items in Groups III–VI
close these open loops. This is not new capability; it is completing
circuits that are already drawn.

**At the organizational level**, the question is whether DDL can be
treated with the same discipline that test-driven development brought
to application code. TDD's key insight was not "write tests before
code" — it was "make the correctness proof visible and executable."
Every passing test is evidence that the system behaves as claimed.
Every schema change processed by this pipeline produces a manifest
that is evidence of the same kind: the DDL is what it is because the
pipeline ran with these inputs, this policy, this registry, and
produced this coverage. The manifest is DDL's test suite. The items
in Groups VIII, IX, and XVI — signing, audit logs, reproducibility
tests, CI/CD templates — are what make the manifest trustworthy as
an organizational artifact, not just as a technical one.

---

### III. The three movements

**Movement One — Foundation.** Groups I and II. Make the existing
algebraic structure visible and tested. Name the Kleisli category.
Add the CE builders. Test the monad laws. Replace the 700-line
parse tree with a typed combinator language. Add units of measure
to Profile numerics. Make phantom types enforce pipeline stage
ordering at compile time. None of this adds behavior; all of it
makes the existing behavior legible and verifiable. The cost is
low. The dividend compounds: every item built on the foundation
thereafter is built on ground that has been stated clearly and
tested formally.

**Movement Two — Intelligence.** Groups III–VII plus Group XIII.
Close the open loops. Emit the five IR types. Consume the five
statistical constructs. Populate `SuggestedConfig`. Wire the
skeleton verb. Detect schema islands. Surface deployment batches.
Compute centrality. Detect anomalies. Identify bounded contexts.
The thesis of this movement is: the evidence is already here.
The codebase computes it, carries it, and discards it at the
boundary. The intelligence is latent; what's missing is the final
mile of wiring that connects evidence to decision, decision to
suggestion, suggestion to operator action. The items in this
movement have unusually high ROI precisely because the infrastructure
cost is already paid — they are consumers of work that is already
done.

**Movement Three — Platform.** Groups XIV–XVI plus Group XVII.
Build on the intelligence layer to create something operators
would recognize as a platform, not a tool. Multiple output formats
(EF, dbt, Liquibase, OpenAPI, GraphQL, Data Vault). A signed,
reproducible, auditable artifact trail. A plugin system so
organizations can extend the pipeline without forking it. An
interactive REPL for policy exploration. CI/CD templates so
adoption doesn't require re-inventing the integration. Schema
registries so the catalog can be sourced from enterprise-wide
infrastructure. This movement is where the system becomes something
you'd architect a company's schema practice around.

---

### IV. The unlock graph

The items in this document are not independent. Some unlock
clusters of other items; some are unlocked by a single predecessor.
Understanding the dependency structure makes sequencing tractable.

**The five most-unlocking items:**

1. **H-007 (SchemaDelta)** is the most structurally unlocking item
   in the document. It opens: H-043 (delta passes), H-064 (pushout/
   pullback), H-080 (Liquibase), H-084 (Flyway), H-088 (diff
   viewer), H-089 (migration preview), H-107 (semantic versioning),
   H-105 (CDC-aware deltas). Every feature that reasons about
   *change* rather than *state* requires `SchemaDelta`. It is also
   the most underspecified major feature — the implementation is
   clear (a record with `Added / Removed / Modified`), the scope is
   narrow (one new module, `SchemaDelta.fs`), and the return is
   enormous.

2. **H-031 (SuggestedConfig population)** is the item with the
   highest immediate operator-facing ROI. The LiveProfiler computes
   the evidence; the diagnostics carry the socket; the only missing
   piece is the function that fills it. Once populated, H-032
   (suggest-config verb), H-086 (approval workflow), and the full
   policy-advisor loop become usable. The operator experience
   transforms from "the pipeline tells me what is wrong" to "the
   pipeline tells me what to do about it."

3. **H-005 (branching lineage)** unlocks the entire speculative-
   execution cluster: H-033 (policy diff), H-034 (cross-pass
   conflict detection), H-063 (free monad scheduling), and any
   "what-if" query over policy space. The Lineage monad today is
   linear; making it a tree opens a qualitatively different class
   of pipeline use.

4. **H-016 (Policy DSL)** unlocks H-060 (natural transformation
   laws), H-085 (policy versioning), H-086 (approval workflow), and
   H-066 (parser CE). More importantly, it transforms the policy
   system from a parsed configuration into a composable algebra.
   Two policies can be `&&&`'d; a policy can be diffed against
   `Policy.empty`; illegal combinations fail at construction time
   rather than at runtime. The policy system today is correct; with
   H-016 it becomes composable.

5. **H-071 (schema centrality)** unlocks the schema intelligence
   cluster: H-072 (bounded contexts), H-075 (complexity scoring),
   H-089 (migration preview risk). The FK graph is already
   computed; centrality is a pure-F# derivation over it. Once the
   centrality scores exist, every operator-facing feature that needs
   to rank tables by importance has a principled metric to use.

**The five tightest dependency chains:**

```
H-007 → H-043 → H-080 / H-084 / H-088
H-031 → H-032 → H-086 → H-087
H-005 → H-033 → H-036
H-071 → H-072 → bounded-context-aware targets (H-083 Data Vault)
H-001 → H-002 → H-003 (CE builders make the Kleisli category legible)
```

**The items that share no significant dependencies (can be done in
any order):**

H-018/H-019/H-020/H-021/H-022 (IR surface completion) are all
independent of each other. H-024 through H-029 (statistical
evidence consumers) are independent of each other. H-037 (island
detection) and H-038 (deployment batches) are independent of each
other and of the statistical evidence cluster. H-066 (parser CE) is
independent of all other items.

---

### V. Natural clusters

The 110 items fall into seven natural clusters that correspond to
coherent work packages — sequences of items that share a substrate
and pay off together.

**Cluster A — "Close the loops"** (H-018 through H-022, H-024
through H-029, H-031, H-032). These items share a single property:
the infrastructure is already built, the evidence is already
computed, and the only missing piece is the wire between source and
sink. Estimated total effort: 3–4 weeks at session cadence. Return:
the pipeline becomes self-recommending and the IR emits its full
schema. This is the highest-ROI cluster in the document.

**Cluster B — "Make the structure legible"** (H-001, H-002, H-003,
H-012, H-013, H-053, H-100). CE builders, Kleisli naming, active
patterns, monad law tests, axiom test suite. These items make the
existing algebraic structure visible in code and in tests. No new
behavior; pure craft dividend. Estimated total effort: 1–2 weeks.
Prerequisite for everything in Groups XI–XII.

**Cluster C — "Policy intelligence"** (H-005, H-016, H-033, H-034,
H-035, H-036, H-060, H-085, H-086, H-087). The policy simulation
cluster. Requires H-005 (branching lineage) and H-016 (Policy DSL)
as prerequisites. Once those exist, the rest of the cluster flows
naturally. Estimated total effort: 5–7 weeks. Return: the pipeline
becomes a policy advisor — operators can explore policy space and
get concrete, evidence-backed configuration recommendations.

**Cluster D — "Schema graph intelligence"** (H-037, H-038, H-039,
H-040, H-041, H-071, H-072, H-073, H-075, H-076). The graph
analysis cluster. `TopologicalOrderPass` already computes most of
the needed graph structure; these items derive higher-level metrics
from it. H-071 (centrality) is the keystone; the others build on
it or on the topological structure directly. Estimated total effort:
3–4 weeks. Return: the operator report gains a topology section that
actually characterizes the schema's risk and complexity profile.

**Cluster E — "Multi-format output"** (H-057, H-078 through H-084).
The sibling-Π cluster. Each is a new target consuming the same
`Catalog × Profile` inputs. They share no internal dependencies and
can be built in any order. H-057 (DacFx DACPAC) is the highest
priority because it closes a Tier-3 hard-requirement deferral.
H-079 (dbt) and H-081 (OpenAPI) likely have the broadest operator
demand. Estimated total effort: 6–10 weeks for the full cluster.
Return: the pipeline becomes the source of truth for all schema-
derived artifacts in an organization's stack.

**Cluster F — "Formal verification"** (H-050 through H-054, H-096
through H-100). Property tests for adjunction laws, Kleisli laws,
monad laws, policy simulation laws, reproducibility, mutation
testing, fuzz testing, model-based testing, and the AXIOMS
executable checker. These items collectively make the formal claims
in `AXIOMS.md` runnable rather than aspirational. Estimated total
effort: 4–6 weeks. Return: the distinction between "we believe the
pipeline is correct" and "the pipeline is provably correct" becomes
a matter of test output rather than argument.

**Cluster G — "Platform hardening"** (H-085 through H-095,
H-108 through H-110). Policy versioning, approval workflow, audit
logs, manifest signing, CI/CD templates, schema lint, cross-env
verification. These items make the system trustworthy as an
organizational artifact — not just technically correct but
operationally reliable in a production engineering environment.
Estimated total effort: 6–8 weeks. Return: adoption by teams
beyond the original operator; the pipeline becomes an
organizational practice, not an individual tool.

---

### VI. What unlocks what: the ROI ladder

From lowest effort and highest immediate return to highest effort
and highest long-term leverage:

**Rung 1 — The quick close** (Cluster A + Cluster B together):
Approximately 4–6 weeks of work that transforms the pipeline from
"structurally correct but partially emitting" into "structurally
visible and fully emitting." The CE builders make the algebra
legible. The IR emission items close the five open loops. The
statistical consumers surface the evidence that's been computed
since chapter B.3. The `SuggestedConfig` population makes every
diagnostic self-documenting. The skeleton verb makes the DataIntent
baseline runnable from the CLI. At the end of Rung 1, the pipeline
is qualitatively different to use: it tells you what it found,
what it recommends, and why.

**Rung 2 — The intelligence layer** (Clusters D + C prerequisites):
H-007 (SchemaDelta) is the keystone. Once it exists, the graph
intelligence cluster (H-037 through H-041, H-071 through H-076)
follows naturally. The topology features require only the existing
graph computation; the schema intelligence features require only the
existing statistical evidence. H-031/H-032 (SuggestedConfig + CLI
verb) is the other keystone: it activates the policy advisor. At
the end of Rung 2, the pipeline is a schema intelligence system:
it reasons about schemas structurally, statistically, and
topologically, and it surfaces recommendations with evidence.

**Rung 3 — The composition layer** (Cluster C in full + Cluster F):
H-005 (branching lineage) and H-016 (Policy DSL) open the policy
simulation cluster. The formal verification cluster makes the
claims in `AXIOMS.md` executable. At the end of Rung 3, the
pipeline is a formally verified, policy-composable schema reasoner.
The distinction from today: every law that `AXIOMS.md` states is
now a test that passes, and the policy system composes algebraically
rather than parses imperatively.

**Rung 4 — The platform layer** (Clusters E + G):
The multi-format targets and platform hardening items. At the end
of Rung 4, the pipeline is an organizational schema practice
platform: it sources schemas from registries, signs and audits its
output, integrates with CI/CD, supports extension via plugins, and
emits artifacts for every schema-consuming tool in the engineering
stack. This is where individual technical excellence becomes
organizational infrastructure.

---

### VII. The mission

*What is this codebase ultimately trying to be?*

The mission is to make schema evolution as disciplined as code
evolution. Code evolution has: version control, typed interfaces,
compile-time checking, automated tests, code review, and
deployment pipelines. Schema evolution has, at most, migration
scripts that are checked by eye and executed by hand.

This pipeline closes that gap. It introduces a formal reasoning
layer between the schema and its DDL representation — a layer that
checks types (via smart constructors), enforces axioms (via
AXIOMS.md), carries evidence (via Profile), records every decision
(via Lineage), and certifies the output (via Manifest). When fully
realized, every schema change passes through this layer. The operator
never touches DDL directly; they express intent via Policy and the
pipeline derives the correct DDL from evidence.

The mission does not require any item in this document to be
implemented. The foundation is already built. What this document
names is the distance between the current system and its full
expression — the remaining distance between a well-engineered
exporter and a proof-carrying schema intelligence platform.

Most of that distance is short. Most of the items in Cluster A
are one function away from existing. The statistical evidence is
computed. The IR types are populated. The policy simulation socket
is wired. The graph structure is computed. The lineage trail is
correct.

The opportunity is to complete what's already been started —
to close the circuits that are drawn, test the laws that are
claimed, and surface the intelligence that is already latent in
the data the pipeline carries.

---

*Maintained alongside `BACKLOG.md`. Items progress through the same
status vocabulary. New items arrive via `proposed` entries; scheduled
items cite the chapter that claims them. This document is the ceiling;
`BACKLOG.md` is the path.*
