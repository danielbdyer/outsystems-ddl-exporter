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

**Status:** proposed

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

**Status:** proposed

**Gap.** `Kind.Triggers : Trigger list` is populated at chapter A.0'.
No emitter target surfaces triggers. SQL Server triggers are
`CREATE TRIGGER ... ON <table> AFTER INSERT, UPDATE AS ...`.

**Location.** `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs`.

**Implementation.** `Trigger.Body` is a `string` (the raw T-SQL body).
Emit via `ScriptDomBuild.tryParseTriggerWithDiagnostics` (new helper
following the chapter 4.7 pattern). Parse failures → `DiagnosticEntry`.

**Coverage impact.** Triggers surface in the SSDT bundle; roundtrip
canary gains trigger fixture coverage.

---

### H-020 — Sequence emission

**Status:** proposed

**Gap.** `Catalog.Sequences : Sequence list` is populated at chapter
A.0'. Sequences are standalone schema objects (`CREATE SEQUENCE ...`);
they are not kind-level constructs. The SSDT emitter processes
`Catalog` and currently ignores `Sequences`.

**Location.** `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs`.

**Implementation.** `Sequence.StartValue`, `Increment`, `MinValue`,
`MaxValue`, `Cycle`, `Cache` map directly to ScriptDom's
`CreateSequenceStatement`. Emit before table creation in the statement
stream (sequences are referenced by DEFAULT constraints).

---

### H-021 — ExtendedProperty emission across all four IR levels

**Status:** proposed

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

**Status:** proposed

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

**Status:** proposed

**Gap.** `ForeignKeyCardinality` is computed at `LiveProfiler.fs`
slice B.3.5 and carried on `Profile`. No pass consumes it.

**Consumer.** `ForeignKeyPass` currently makes FK decisions based on
structural analysis. `ForeignKeyCardinality.Ratio` (number of distinct
FK values / total rows) is the empirical measure of whether a FK
relationship is sparse or dense. Dense FK → enforce constraint;
sparse FK → opportunity diagnostic.

**Location.** `src/Projection.Core/Passes/ForeignKeyPass.fs` and
`src/Projection.Core/Strategies/ForeignKeyRules.fs`.

**Unlocks.** FK tightening decisions become evidence-driven rather than
policy-driven-only. The `SuggestedConfig` field on `DiagnosticEntry`
can suggest `TighteningPolicy.add (FkEnforce fkKey)` when cardinality
evidence supports enforcement.

---

### H-025 — FK selectivity consumers

**Status:** proposed

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

**Status:** proposed

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

**Status:** proposed

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

**Status:** proposed

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

**Status:** proposed

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

**Status:** proposed

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

**Status:** proposed

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

*Maintained alongside `BACKLOG.md`. Items progress through the same
status vocabulary. New items arrive via `proposed` entries; scheduled
items cite the chapter that claims them. This document is the ceiling;
`BACKLOG.md` is the path.*
