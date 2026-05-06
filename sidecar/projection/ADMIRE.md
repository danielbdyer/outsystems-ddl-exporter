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

    ### Migration path
    paragraph or two on the carry-across. Test fixtures, compatibility,
    sequencing.

    ### Edges / risks
    bullets, terse.

---

## 2026-05-06 — `EntitySeedDeterminizer` (`src/Osm.Emission/Seeds/EntitySeedDeterminizer.cs`)

**Status:** admired (placement decided)

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

### Migration path

1. **Catalog Reader, static-data branch.** A new C# function in the
   adapter takes V1's `StaticEntityTableData` and produces V2's
   `Static populations`: each row's `Identifier` becomes an `SsKey`
   built from the row's PK column values (canonicalized through invariant
   culture); each cell becomes a string keyed by the V2 attribute `Name`.
   The PK column set must be marked on `Attribute` in the IR — currently
   absent, so this introduces a small IR refinement: an
   `IsPrimaryKey : bool` field on `Attribute`. Synthetic fixture and
   tests update accordingly.
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

