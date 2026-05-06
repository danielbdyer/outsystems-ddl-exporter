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

