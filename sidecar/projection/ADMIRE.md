# ADMIRE

Append-only log of V1 admirations and their V2 placements. The bridge
between V1's working knowledge and V2's pure architecture.

## What this is

For each meaningful V1 component the V2 effort is preparing to migrate,
this document records:

- **What it does** — in algebraic terms.
- **V2 placement** — pure pass in `Projection.Core.Passes`, adapter at a
  port in `Projection.Adapters.*`, or a split with the boundary explicitly
  named.
- **Existing test coverage** — the V1 tests that protect this component,
  what they assert, and how each translates into V2 testing
  (property-based, differential, behavioral re-expression, or skip with
  logged exception). The migration is complete when V2 satisfies V1's
  tests (or the exceptions are logged in `DECISIONS.md`). See the
  contract-testing entry in `DECISIONS.md` for the discipline.
- **Migration path** — how V1's behavior gets carried into V2. Where the
  C# logic lives. What test fixtures it needs. What compatibility
  considerations apply.
- **Edges / risks** — non-obvious assumptions, lurking impurities, hidden
  invariants the future migrator should know about.

Entries are short — paragraphs, not essays. The corpus accumulates value
over time. Read top-to-bottom for chronological order.

## Format

    ## YYYY-MM-DD — V1 component (file path)
    **Status:** admired (placement decided) | extracted (V2 in place)

    ### What it does (algebraic terms)
    one or two paragraphs.

    ### V2 placement
    pure pass / adapter / split — with rationale.

    ### Inputs and outputs (V2 IR)
    one paragraph naming the V2 IR fields consumed and produced.

    ### Existing test coverage
    table form, one row per V1 test method. Columns: test name, file:line,
    category (FK ordering / cycle detection / determinism / ...), what it
    asserts in plain English, V2 translation (Property / Differential /
    Behavioral / Skip with rationale).

    ### Migration path
    paragraph or two on the carry-across. Test fixtures, compatibility,
    sequencing.

    ### Edges / risks
    bullets, terse.

---

## 2026-05-06 — `EntitySeedDeterminizer` (`src/Osm.Emission/Seeds/EntitySeedDeterminizer.cs`)

**Status:** admired (placement decided); pure-core sort half **extracted**
in V2 as `Projection.Core.Passes.NormalizeStaticPopulations` (commit 5,
session 3). Boundary cell-coercion still pending the Catalog Reader.

### What it does (algebraic terms)

Takes an unordered collection of static-entity tables — each a table
definition plus an `ImmutableArray<object[]>` of rows — and returns the
same tables with each row collection deterministically sorted. Sort key
is "primary-key columns first (in PK order), then all columns
left-to-right, then array length"; per-cell comparison dispatches on
runtime type (numeric coerced to `decimal`, then `DateTime` /
`DateTimeOffset` / `DateOnly` / `TimeOnly` / `TimeSpan` / `Guid` /
`bool` / `byte[]`, then `IComparable` fallback, then ordinal string).
Pure: no I/O, no mutation. Stable across runs because every comparison
uses `CultureInfo.InvariantCulture`.

The reason the determinizer exists is reproducibility: deterministic seed
scripts diff cleanly under git, survive refactors as bytewise-identical
artifacts, and let CI assert byte-equality on emitted SQL. It is the
guarantee that the unordered input from a SQL fetch produces a totally
ordered emission.

### V2 placement

**Split.** The type-aware cell comparison lives at the boundary; the
ordering pass lives in the pure core.

  - **Boundary (C# adapter, `Projection.Adapters.Sql` or similar).** The
    Catalog Reader coerces V1's `object[]` cells to canonical string
    representations using invariant-culture rules. The runtime-type
    dispatch (numeric coercion, `DateTime` formatting, `byte[]`
    hex-encoding, etc.) is impurity in disguise: it depends on the .NET
    runtime's type system and culture machinery. By the time a
    `StaticRow` reaches the IR its `Values : Map<Name, string>` carries
    canonical, comparable strings.
  - **Pure core (`Projection.Core.Passes.NormalizeStaticPopulations`).**
    A pass that, for every kind carrying the `Static` modality, sorts
    its populations by `Identifier` and, as a tiebreaker, by the `Values`
    map walked in alphabetical attribute-name order with string-ordinal
    cell comparison. Pass category: structural normalization, runs after
    `canonicalizeIdentity` and before any emitter that consumes static
    populations.

The split honors purity: cell-level type semantics are a property of the
*source*, not of the algebra. Two adapters (V1 `OsmModel` reader; some
future DACPAC reader) may coerce differently; the pure pass treats
canonical strings uniformly.

### Inputs and outputs (V2 IR)

Consumes: `Catalog` — specifically every `Kind` whose `Modality` list
contains `Static populations` with non-empty rows.

Produces: same `Catalog` shape, with each `Static`'s `populations` list
reordered. Identity is preserved: `StaticRow.Identifier` (an `SsKey`)
is untouched; only list order changes.

The pass emits one `Touched` lineage event per `Static` modality
processed (per A25), naming the kind whose populations were normalized.
A future pass that *invents* canonical row identifiers (where the source
provided none) would emit `Created` events with derived `SsKey`s; the
determinizer itself only reorders.

### Existing test coverage

V1 tests EntitySeedDeterminizer **indirectly** through integration and
golden-file tests; no direct unit tests on the comparer logic exist.
The eight observable invocations and their V2 translations:

| V1 test | File:line | Category | Asserts | V2 translation |
|---|---|---|---|---|
| `BuildSsdtPipeline_MatchesEdgeCaseFixtures` | `tests/Osm.Etl.Integration.Tests/EmissionPipelineTests.cs:25` | golden-file (integration) | end-to-end pipeline emits seed SQL matching `Fixtures/emission/edge-case/Seeds/AppCore/StaticEntities.seed.sql` after normalization | **Differential** — runs once Catalog Reader can ingest V1 fixture; meanwhile the F# `contract: a perturbed catalog normalizes to the canonical form` carries the invariant |
| `BuildSsdtPipeline_WithRenamesMatchesFixtures` | same:132 | golden-file | as above with naming overrides | **Differential** — same gating |
| `StaticSeedStep_generates_seed_scripts` | `tests/Osm.Pipeline.Tests/BuildSsdtPipelineStepTests.cs:175` | step-level | normalized seeds reach the script generator | **Behavioral** — covered by V2 integration with `Π_SSDT.RawTextEmitter` (later) |
| `StaticSeedStep_OrdersTablesByForeignKeyDependencies` | same:214 | step-level | row ordering survives downstream FK ordering | **Differential** — when `EntityDependencySorter` lands in V2 |
| `StaticSeedStep_emits_master_seed_when_enabled` | same:321 | step-level | normalized seeds feed both per-module and master files | **Behavioral** — emission-config concern, not normalizer |
| `StaticSeedStep_disambiguates_colliding_sanitized_module_names` | same:389 | step-level | normalizer is independent of module naming | **Behavioral** — covered by `non-Static kinds pass through structurally unchanged` |
| `Generate_ProducesMergeBlocksForEachRow` | `tests/Osm.Emission.Tests/StaticEntitySeedScriptGeneratorTests.cs:16` | unit (downstream) | generator assumes normalized input; no row-order assertion of its own | **Skip** — V2 covers row ordering directly via the property tests in `NormalizeStaticPopulationsTests.fs` |
| `Generate_OrdersTablesUsingForeignKeyDependencies` | same:141 | unit (downstream) | FK ordering of tables, not row ordering | **Skip** — concern of the future ordering pass, not this normalizer |

V1 invariants now defended in V2 by `NormalizeStaticPopulationsTests.fs`:

- **Idempotence** — `contract: idempotent on the synthetic fixture` and
  `contract: idempotent on a perturbed catalog`.
- **Determinism (T1)** — `T1: NormalizeStaticPopulations is deterministic`
  (output and trail both byte-stable across repeat runs).
- **PK-ordered totality** — `contract: rows are sorted by Identifier`.
- **Identity preservation** — `A4: pass neither invents nor drops kind
  SsKeys` and `A4: pass neither invents nor drops static-row Identifiers`.
- **Static-only effect** — `non-Static kinds pass through structurally
  unchanged` plus `A25: only Static-bearing kinds emit Touched events`.
- **Edge cases V1 missed** — empty-population, single-row, already-canonical
  (none of these are tested in V1; FsCheck-amplified property test
  `property: row order in input does not affect output order` covers
  the combinatorial space).
- **Cardinality** — `cardinality preserved: same modules / kinds /
  attributes / references / modality marks`.

Differential testing (V1 vs V2 on shared golden fixtures
`tests/Fixtures/emission/edge-case/Seeds/AppCore/StaticEntities.seed.sql`
and the matrix-temporal variant) lands when the Catalog Reader exists
to coerce V1 fixture inputs into V2 form. Until then the contract is
defended by property-based and behavioral tests; the V1 fixtures are
the gold standard and the differential check is a follow-on commit
once the boundary adapter is in place.

### Migration path

1. **Catalog Reader, static-data branch.** A new C# function in the
   adapter takes V1's `StaticEntityTableData` and produces V2's
   `Static populations`: each row's `Identifier` becomes an `SsKey`
   built from the row's PK column values (canonicalized through invariant
   culture); each cell becomes a string keyed by the V2 attribute `Name`.
   The PK column set was added in commit 4 of session 3 (`IsPrimaryKey`
   on `Attribute`).
2. **F# pass.** `NormalizeStaticPopulations.run : Catalog -> Lineage<Catalog>`,
   one of the standard endofunctors. Idempotent on canonical input,
   normalizing on perturbed input — the same pattern as
   `canonicalizeIdentity`. Property tests: golden output is byte-stable
   across runs (T1); reordering input rows yields the same output;
   running twice equals running once.
3. **Test fixtures.** The existing V1 fixtures under
   `tests/Osm.Etl.Integration.Tests/EmissionPipelineTests.cs` lines 65,
   180 are gold for compatibility: V2 should produce equivalent ordering
   for the same logical inputs (after coercion).
4. **Sequencing.** Lands after the IR gains `IsPrimaryKey` (small IR
   commit), after the Catalog Reader's static-data branch (medium
   adapter commit), then the pass itself (small pass commit).

### Edges / risks

- **Invariant culture must remain explicit.** V1 uses
  `CultureInfo.InvariantCulture` everywhere; lose this and seed scripts
  vary by locale. The boundary adapter must take an explicit dependency
  and reject `CultureInfo.CurrentCulture` on principle.
- **Silent type-coercion fallback.** V1 catches `FormatException` and
  `InvalidCastException` and falls through to string comparison without
  logging. V2's boundary should warn through the (forthcoming)
  `Diagnostics` writer's operator channel when coercion falls back —
  silent degradation hides data corruption.
- **PK detection assumes accurate metadata.** V1 trusts
  `definition.Columns[i].IsPrimaryKey`; if the metadata is stale, row
  order becomes semantically meaningless. V2's `IsPrimaryKey` field
  should be set by the Catalog Reader from the same authoritative
  metadata source, with a startup assertion that every kind has at
  least one PK attribute (or carries an explicit "no PK" annotation).
- **Null tables silently dropped.** V1 skips null entries in the input
  list (lines 24–28). V2 boundary should refuse null and surface a
  validation error — `Result<Catalog>` is the right return type for the
  reader.
- **Tertiary sort by array length.** V1 falls back to
  `row.Values.Length` after value comparison. With V2's fixed-shape
  `StaticRow` (every row has the same `Values` map keys), this fallback
  is unreachable; document and drop in the V2 pass.

---

## 2026-05-07 — `EntityDependencySorter` (`src/Osm.Emission/Seeds/EntityDependencySorter.cs`)

**Status:** admired (placement decided)

### What it does (algebraic terms)

Takes a collection of static entity tables plus an OSM model and
produces a topologically sorted ordering of the tables — children after
parents, with explicit cycle handling. Implements Kahn's algorithm for
the topological sort and Tarjan's algorithm for strongly connected
components (cycle detection). When cycles are detected, attempts to
auto-resolve by classifying edges as Weak (nullable + NoAction/SetNull)
vs Cascade vs Other; if the graph contains exactly one weak edge in a
2-cycle ("asymmetric audit cycle"), removes it; if not, falls back to
alphabetical ordering. Optional manual cycle-resolution overrides via
`CircularDependencyOptions`. Optional junction-table deferral when bridge
tables (2+ FKs) need to be emitted last. Returns an immutable result
record with the ordering, mode flag, edge counts, and SCC diagnostics.

Pure with one explicit side-effect channel: callers may pass a mutable
`ICollection<string>` to receive diagnostic messages.

### V2 placement

**Pure pass in `Projection.Core.Passes`** — but **producing an A32
emitter-consumable value, not a structural change to the catalog.**

The masterwork's §16 (Topological Order Law) makes ordering a property
of the data emission, not of the schema. Schema emission uses
deterministic (alphabetical) ordering per A33; data emission consumes
the topological ordering. So:

- The pass `Projection.Core.Passes.TopologicalOrder.run` takes the full
  `Catalog` and produces a `TopologicalOrder` value carrying the sorted
  kind list, the edge classification, the cycle diagnostics, and the
  ordering mode.
- The `Catalog` itself is **not** restructured — kinds keep their
  declaration order. The ordering is metadata, consumed by emitters
  that need it.
- This is a textbook A32 instance: one pass produces a value
  (`TopologicalOrder`) that multiple Π's consume — the (eventual) data-
  emission Π reads it for INSERT order; the diagnostics Π reads it for
  cycle reporting; the schema-emission Π ignores it (per A33).

The pass operates on the **full** selected catalog (per masterwork §16:
sort always sees all selected kinds; emission filters afterward), not
on a per-modality subset. This eliminates the V1 "scoped sort" pathology
(decomposition Vector 3, lines 1727–1803).

### Inputs and outputs (V2 IR)

Consumes:

- `Catalog` — kinds + their references (the FK edges).
- `Policy.Selection` — to know which kinds participate (sort runs on
  the selected subset, but **all** selected kinds, never sub-scoped to
  static-only).
- `Policy.Insertion` — informs the emission consumer; not consumed by
  the sort itself.
- A future `OrderingPolicy` axis (probably under `EmissionPolicy`)
  carries the manual cycle overrides equivalent to V1's
  `CircularDependencyOptions`.

Produces (as an emitter-consumable value, A32):

```fsharp
type TopologicalOrder = {
    Mode             : OrderingMode
    Order            : Kind list                     // sorted, FK-safe
    Edges            : (SsKey * SsKey) list          // (source, target)
    MissingEdges     : (SsKey * SsKey) list          // FKs to absent kinds
    Cycles           : CycleDiagnostic list          // SCCs that survived
    Diagnostics      : string list                   // human-readable trace
}

and OrderingMode = Topological | Alphabetical | JunctionDeferred

and CycleDiagnostic = {
    Members         : SsKey list
    BreakableEdges  : (SsKey * SsKey) list
    Reason          : string                         // e.g. "no weak edge in 3-cycle"
}
```

The pass also emits one `Touched` lineage event per kind it considered
(per A25), and `Annotated` events for cycle-resolution actions.

### Existing test coverage

`tests/Osm.Emission.Tests/EntityDependencySorterTests.cs` has eight
example-based tests. Translated:

| V1 test | Lines | Category | Asserts | V2 translation |
|---|---|---|---|---|
| `SortByForeignKeys_ParentsPrecedeChildren` | 14–96 | basic FK ordering | parent before child; EdgeCount=1, NodeCount=2 | **Property** — `forall acyclic graph: every (parent, child) edge has parentIndex < childIndex` |
| `SortByForeignKeys_ReportsEdgesAfterMetadataEnrichment` | 98–204 | edge-detection | edges only counted when ActualConstraints carries column metadata | **Behavioral** — V2 derives edges from `Reference.SourceAttribute` / `TargetKind` directly; the V1 metadata-enrichment dance disappears (boundary cleans it up) |
| `SortByForeignKeys_ReportsMissingEdgesWhenReferencedTableAbsent` | 206–263 | missing-edge | absent parent ⇒ MissingEdgeCount=1, sort proceeds | **Property** — `forall graph with N missing edges: TopologicalOrder.MissingEdges.Length = N` |
| `SortByForeignKeys_DetectsCyclesAndAppliesFallback` | 265–367 | symmetric cycle | bidirectional FKs ⇒ Mode=Alphabetical, CycleDetected=true | **Behavioral** — re-expressed in F#; verifies symmetric-cycle fallback to alphabetical |
| `SortByForeignKeys_AutoDetectsAsymmetricAuditCycle` | 369–483 | asymmetric cycle | one weak edge in 2-cycle ⇒ auto-broken, Mode=Topological | **Property** — `forall 2-cycle with exactly one weak edge: cycle resolved, weak edge appears in Cycles[].BreakableEdges` |
| `SortByForeignKeys_SkipsAutoDetectionWhenManualCyclesExist` | 485–624 | manual cycle config | manual ordering overrides auto-detection; diagnostics confirm "skipping automatic" | **Behavioral** — V2 OrderingPolicy explicitly opts into manual mode |
| `SortByForeignKeys_ResolvesSanitizedEffectiveNames` | 626–712 | naming overrides | physical-name FKs resolve via NamingOverrideOptions | **Skip / Boundary** — V2 keeps logical-name resolution in the IR via SsKey; this V1 concern is handled by the Catalog Reader before the sort sees the input |
| `SortByForeignKeys_DefersJunctionTablesWhenEdgesMissing` | 714–843 | junction deferral | bridge table with 2 FKs ⇒ deferred to end when option set | **Behavioral** — V2 OrderingPolicy.DeferJunctions flag; same observable behavior |

V2 invariants the property tests will defend (some lifted from V1's
example tests, some new):

- `every emitted kind appears exactly once in the output order` (V1
  implicit; V2 explicit FsCheck).
- `acyclic graphs always produce Mode = Topological` (V1 not asserted
  as a property; V2 explicit).
- `asymmetric cycles with exactly one weak edge always resolve via the
  weak edge` (V1 tested with one example; V2 sweeps the combinatorial
  space).
- `manual cycle ordering takes precedence over auto-detection`.
- `MissingEdges count is exact and round-trips through serialization`.
- `dictionary iteration order does not perturb the output` — see
  Edges/risks below; this is the most important V2 property because
  V1's correctness depends on it implicitly.

Differential testing against the V1 fixtures (the small "Parent /
Child / Audit / Bridge" graphs constructed inline in the V1 test file)
lands when the C# Catalog Reader can ingest the equivalent V2 form;
until then property + behavioral tests carry the contract.

### Migration path

1. **Define `TopologicalOrder` and `OrderingPolicy` value types** in
   `Projection.Core` (the value Π consumes is part of the IR's surface
   even though it's not part of the catalog itself).
2. **Port Kahn's algorithm** as a pure F# function over the
   `(Kind list, (SsKey * SsKey) list)` graph extracted from the catalog.
3. **Port Tarjan's SCC** as a pure F# function — no global state, no
   recursion-stack reliance; an explicit stack-based implementation is
   easier to reason about.
4. **Edge classification** (Weak / Cascade / Other) — derive from the
   `Reference.OnDelete` discriminant plus the source attribute's
   `Column.IsNullable`. Strictly local; pure.
5. **Cycle resolver** — start with the V1 algorithm (asymmetric-2-cycle
   detection only); add the heuristic feedback-arc-set search later if
   real fixtures need it. Defer the V1 `MaxCombinationsToTry` complexity
   until evidence demands it.
6. **Junction-table heuristic** — port last; flagged below as a real
   risk because V1's heuristic has false positives.
7. **Diagnostics** — return as a list of strings on `TopologicalOrder`,
   never as a side effect (see Edges/risks).
8. **Sequencing.** This pass lands after `Policy` gains an `Ordering`
   axis (which carries the manual cycle config); the value type
   `TopologicalOrder` lands first so it can be round-tripped through
   tests before the algorithm is wired.

### Edges / risks

- **Dictionary iteration order is load-bearing in V1.** V1 relies on
  C# `Dictionary<K,V>`'s insertion-order iteration. `OsmModel.Modules`
  → `Entities` → `Relationships` is iterated without an explicit sort,
  and Kahn's `InsertSorted` uses a `ReadyQueueComparer` that breaks
  ties by name only as a fallback. V2 must enforce stable iteration
  (sort kinds and references by `SsKey` before graph construction) and
  add a property test sweeping shuffled inputs. **This is the highest-
  leverage V2 improvement on V1.**
- **Single-constraint assumption.** V1's `ActualConstraints[0]` ignores
  any subsequent constraints on the same relationship (line 889). V2
  classifies all constraints; if they disagree on edge strength the
  pass emits a diagnostic naming the disagreement.
- **Missing edges do not block sorting.** V1 tolerates FKs to absent
  tables (alphabetical fallback). V2 should tolerate them too —
  partial exports rely on this — but the `MissingEdges` field on
  `TopologicalOrder` makes the tolerance explicit and auditable.
- **Junction-table heuristic has false positives.** V1 flags a table
  as a junction if it has 2+ non-PK FK columns. A 3-column table with
  ID + 2 FKs + a `CreatedDate` matches but isn't a junction. V2
  should let `OrderingPolicy` carry an explicit junction-table SsKey
  set; the heuristic becomes a fallback.
- **Diagnostics as side-effect channel.** V1 mutates a caller-supplied
  `ICollection<string>`. V2 returns diagnostics as a value — purity
  preserved; observability unchanged.
- **Case-insensitive name comparison hard-coded.** V1's `TableKeyComparer`
  is case-insensitive (SQL Server convention). V2 keys by `SsKey`
  which is case-sensitive at the type level — the right call, since
  identity should not depend on case.
- **`SCCs` returned as table-name strings.** V1 returns SCC members as
  strings, requiring callers to look up entities by name. V2 returns
  `SsKey list list` directly — strongly typed, unambiguous, no lookup.
- **Cycle-resolution combinatorial blowup.** V1's
  `FindMinimumFeedbackArcSet` caps at 50,000 combinations and bails to
  "remove all weak edges." For V2's synthetic milestone the simple
  asymmetric-2-cycle resolver is enough; the heuristic search arrives
  when a real fixture has a 3+ node cycle.

