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
    pure pass / adapter / split / carbon-copied — with rationale.

    ### V2 placement — carbon-copy log (entries 2026-05-16 onward)
    For entries with placement "carbon-copied" (per the V2
    self-containment + editorial-inheritance discipline), each
    inheritance event records:

    - **V1 source path** at the V1 head V2 inherited from.
    - **V2 location** — the file path inside the sidecar where
      the carbon-copy landed.
    - **Date inherited** (ISO).
    - **Refactor status** — verbatim / partially refactored /
      fully refactored.
    - **Citation comment** — the file-header note linking V2's
      file back to the V1 source for tethering.

    See `BACKLOG.md` § "V1 inheritance log" for the cross-component
    operational ledger.

## Format amendment (2026-05-16 — V2 self-containment discipline)

Per `DECISIONS 2026-05-16 (later) — V2 self-containment + carbon-copy
editorial inheritance`, V2 has zero runtime dependency on V1's trunk.
V1 components V2 wants to inherit from are **carbon-copied** into V2's
domain-structured locations; the original V1 trunk source is unchanged;
V2's copy is V2's responsibility from the moment it lands. Each
component's ADMIRE entry records the carbon-copy events as they happen,
forming an editorial inheritance ledger sibling to `BACKLOG.md` § "V1
inheritance log".

**Re-classification corrections (codified at the 2026-05-16 audible):**

- `NullabilityEvaluator`: PURE PASS in F#. V1 evaluator is mode-bound
  policy front-to-back; the "signals" are policy-applying tree nodes
  built per-mode. V2's `Projection.Core.Strategies.NullabilityRules.fs:223-277`
  already covers the rule space in 55 lines. No carbon-copy candidate.
- `ForeignKeyEvaluator`: PURE PASS in F#. Splittable in principle, but
  post-split evidence set duplicates V2 IR fields. No carbon-copy
  candidate.
- `UniqueIndexDecisionOrchestrator`: SPLIT — V1's
  `UniqueIndexEvidenceAggregator` (~150 LOC) is a carbon-copy candidate
  for evidence aggregation; rule application stays in F#
  `UniqueIndexRules.fs:144`. Carbon-copy lands at the chapter that
  consumes the lifted evidence (not pre-emptively).
- `EntityDependencySorter`: harmonized via existing
  `TopologicalOrderPass.SelfLoopPolicy` (A40). No carbon-copy
  candidate.
- OSSYS catalog producer: CARBON-COPY CANDIDATE. V1's metadata
  extraction chain (`MetadataSnapshotRunner.cs`, `SnapshotJsonBuilder.cs`,
  the result-set processor chain; ~1,880 LOC) is the highest-value
  inheritance target. Carbon-copies land in a dedicated C# adapter
  project (`Projection.Adapters.OssysSql`, museum-polish) when the
  chapter that consumes it opens.

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

**Status:** **extracted (differential confirmed)** — V2's `Projection.
Core.Passes.NormalizeStaticPopulations` plus `Projection.Adapters.Sql.
Static` jointly satisfy V1's behavioral contract on the
`static-entities.edge-case.json` fixture (session 5 commit 3). The
embedded V1 fixture content is the V2 contract; the V1 file remains the
source of truth for V1 itself, and any V1-fixture change requires a
deliberate V2 expectation update.

Pure-core sort half extracted as `NormalizeStaticPopulations` in
session 3 (commit 5). Boundary cell-coercion shipped as
`Projection.Adapters.Sql.Static` in session 5 (commit 3) — F# adapter
(see DECISIONS 2026-05-09 on adapter language choice). Type-aware
comparison V1 did at the C# level collapses to canonical
invariant-culture strings at the boundary; the pure pass operates on
those strings.

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
and the matrix-temporal variant) **landed at session 5 commit 3** as
`StaticAdapterDifferentialTests.fs`. Three `V1 contract:`-prefixed
tests assert the V1 fixture round-trip directly through V2's adapter
boundary. The V1 fixtures remain the gold standard and the V2
expectation embedded in the test file is the V2 contract; any V1
fixture change requires a deliberate V2 expectation update. The
property-based and behavioral tests in `NormalizeStaticPopulationsTests.
fs` continue to defend the invariants alongside the differential.
(`CHAPTER_1_CLOSE.md §2.11` flagged this acknowledgement as missing;
session 13 added it.)

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

## 2026-05-09 — `NullabilityEvaluator` (`src/Osm.Validation/Tightening/NullabilityEvaluator.cs`)

**Status:** **extracted (differential confirmed)** — V2's
`Projection.Core.NullabilityRules` + `Projection.Core.Passes.NullabilityPass`
+ `Projection.Adapters.Sql.ProfileSnapshot` jointly carry V1's
`NullabilityEvaluator` semantics into V2.

**Audible update (2026-05-16; V2 self-containment).** V2 placement
remains **PURE PASS in F#**. No carbon-copy candidate: V1's evaluator
is mode-bound policy front-to-back; the "signals" (`NullEvidenceSignal`,
`MandatorySignal`, `ForeignKeySupportSignal`) read `Policy.NullBudget`
and the `ForeignKeys` axis directly, and the signal tree is built per-
mode via `NullabilitySignalFactory.Create(_options.Policy.Mode)`. V2's
55-line F# implementation at `NullabilityRules.fs:223-277` is the
published form. V1's policy-tree-construction machinery is left behind
as a mental-model artifact V2 does not inherit. Five of V1's eight test
scenarios translate as Behavioral parity assertions in
`V1NullabilityParityTests.fs` (session 7 commit 3); three are explicit
Skip cases naming intentional V2 divergences (Aggressive mode collapsed
per DECISIONS 2026-05-09; opportunity-stream wire-up pending the
Diagnostics writer). The end-to-end differential test in
`EndToEndDifferentialTests.fs` (session 6 commit 6) validates the
three-input projection through both adapters.

The `IsMandatory` IR refinement (session 7 commit 2) closed the gap
surfaced empirically by the milestone test; the V1 mandatory-driven
branches now fire and are covered by tests.

Third use of the canonical "extracted (differential confirmed)" status
string. The first was `EntitySeedDeterminizer` (sort half, session 5);
the second is implicit in this same status; this is the first
*decision-producing* V1 transform fully migrated. Future ADMIRE
entries reaching this state use the same phrase.

**Significance:** This is the first V1 transform that consumes `Profile`
(empirical evidence). The admire entry does two things at once —
documents the V1 component, and validates that V2's `Profile` aggregate
is workable in practice. The V2 form will be the first real exercise of
the three-input projection `(Catalog, Policy, Profile)`.

### What it does (algebraic terms)

For each attribute on each kind, decides whether the surface should
emit `NOT NULL` and whether downstream emission should be blocked
pending data remediation. The decision composes five evidence signals
(primary key, physical-not-null, foreign-key support, unique-clean,
logical-mandatory) under a tightening **mode** (Cautious /
EvidenceGated / Aggressive) that determines which signals participate
and which require profile evidence to fire. A null-budget threshold
allows a configurable percentage of nulls without disqualifying
tightening. Operator overrides bypass the entire decision tree.

The decision is pure — same `(catalog, policy, profile)` triple
produces the same `NullabilityDecision` for every column, with a full
`SignalEvaluation` trace showing which signals fired and why.

### V2 placement

**Pure pass in `Projection.Core.Passes`, producing an emitter-consumable
`NullabilityDecisionSet` value per A32** (paralleling `TopologicalOrder`).
The catalog itself is **not** modified — nullability decisions are
metadata that emitters consume; the catalog's structural truth (logical
`IsMandatory`, physical `IsNullable`) remains the source.

Following the algebra/domain split (DECISIONS 2026-05-09):

  - **Algebra in the pass.** Walk every kind × attribute, look up
    profile evidence, look up policy overrides, apply the decision
    function, accumulate `NullabilityDecision` values into a
    `NullabilityDecisionSet`, emit lineage events.
  - **Domain in `NullabilityRules` (new module, alongside
    `CycleResolution`).** The signal hierarchy, threshold formula
    (`allowed = rowCount * nullBudget`), mode-specific composition
    rules (which signals participate in which mode), and rationale
    taxonomy. V1's `TighteningPolicyMatrix` is the source for the
    mode/signal matrix.
  - **Typed seam between them.** A `Decider` type
    (`Kind -> Attribute -> Catalog -> Policy -> Profile -> NullabilityDecision`)
    is the seam. The pass is parameterized over decider; the default
    decider implements V1's signal-hierarchy rules.

### Inputs and outputs (V2 IR)

Consumes:

  - **Catalog** — `Kind.Attributes[].IsMandatory`, `IsPrimaryKey`,
    `Reference` (FK metadata + `OnDelete`), `Column.IsNullable` (the
    physical-NOT-NULL signal), plus the kind's `PhysicalRealization`
    for coordinate resolution.
  - **Policy** — `Tightening.Mode` (`Cautious | EvidenceGated |
    Aggressive`), `Tightening.NullBudget` (decimal 0.0–1.0),
    `Tightening.AllowCautiousNullabilityRelaxation` (bool),
    `Tightening.NullabilityOverrides` (list of `(SsKey * Outcome)`
    entries — V2 keys by SsKey, not by name+coordinate, per A4).
  - **Profile** — `ColumnProfile.NullCount`, `RowCount`,
    `NullCountProbeStatus.Outcome`; `UniqueCandidateProfile.HasDuplicate`,
    `ProbeStatus.Outcome`; `ForeignKeyReality.HasOrphan`, `OrphanCount`,
    `ProbeStatus.Outcome`. Profile lookups are by `SsKey` (the V2
    boundary already resolves physical coordinates to identities).

Produces (emitter-consumable, A32):

```fsharp
type NullabilityDecisionSet = {
    Decisions       : NullabilityDecision list
    SynthesizedAtUtc: DateTimeOffset  // for audit; not part of equality
}

and NullabilityDecision = {
    AttributeKey      : SsKey
    MakeNotNull       : bool
    RequiresRemediation : bool
    Rationales        : Rationale list
    Trace             : SignalEvaluation option
}
```

### The Profile-consumption pattern in detail

This section earns its weight: the algebra's first three-input exercise
has to honor V1's subtleties without smoothing them over.

**Signal hierarchy (the algebra):**

1. **PrimaryKey** — true iff `Attribute.IsPrimaryKey`. No profile
   needed.
2. **PhysicalNotNull** — true iff `Column.IsNullable = false`. No
   profile needed.
3. **ForeignKeySupport** — true iff the FK is enforced or can be safely
   created (no orphans, target present, delete rule acceptable).
   Reads `ForeignKeyReality`.
4. **UniqueClean** — true iff a unique constraint covers the column
   with no observed duplicates. Reads `UniqueCandidateProfile`.
5. **LogicalMandatory** — true iff `IsMandatory` AND
   `(profile absent OR NullCount within budget)`. Reads `ColumnProfile`.

The mode determines which signals participate:

  - **Cautious** — only signals 1, 2, 5 (no profile-driven tightening).
  - **EvidenceGated** — signals 1, 2, 5 always; 3, 4 only when their
    probe succeeded (`RequiresEvidence: true`).
  - **Aggressive** — all five always; signals 3, 4 contribute even
    without evidence, but mark `RequiresRemediation` when evidence is
    missing (`AddsRemediationWhenEvidenceMissing: true`).

**Null-budget formula:**

`allowed = RowCount × NullBudget`. If `NullCount ≤ allowed`, the
column passes the null-budget gate. Configurable; lives in
`Policy.Tightening.NullBudget`. Default is conservative (e.g., `0.05`
allows up to 5% nulls).

**Probe-outcome gating:**

V1 trusts only `ProbeOutcome.Succeeded` and `TrustedConstraint`
(line 17, `NullEvidenceSignal.cs`). Any other outcome
(`FallbackTimeout`, `Cancelled`, `AmbiguousMapping`) is treated as
"no evidence" — the signal does not fire. This is **conservative by
design**: probe failure ⇒ no support for tightening. V2 must preserve
this.

**Override precedence:**

Lines 139–170 (`NullabilityEvaluator.cs`): if an override applies, the
entire decision is replaced with `MakeNotNull = false`,
`RequiresRemediation = false`, and rationales are scrubbed and
replaced with `NullabilityOverride`. **Overrides are absolute** —
they bypass signal evaluation entirely. V2 must preserve this; it is
the operator-approved escape hatch and emitter consumers depend on
the override never silently re-enabling NOT NULL.

**Missing-evidence default:**

A column whose profile is absent from the lookup is treated as
"profile missing." The Mandatory signal still fires on
`IsMandatory = true` (logical schema is trustworthy without
empirical confirmation), but the rationale set includes
`ProfileMissing` rather than `DataNoNulls`. Downstream consumers can
distinguish the two.

**Subtle: cross-column non-dependency.**

Each column's decision is independent. V1 does **not** cross-reference
profile evidence between columns (e.g., "if column A has nulls, column
B becomes less aggressive"). Composite-unique evidence is pre-aggregated
into the four ISet collections before NullabilityEvaluator sees it.

### The masterwork's PolicyDecisionSet shape question

The masterwork (constitution §3, lines 320–384) prescribes a
**ternary** outcome:

```fsharp
type NullabilityOutcome =
    | EnforceNotNull              // model + profile agree
    | KeepNullable                // profile shows nulls
    | RequireOperatorApproval     // conflict — operator must decide
```

V1 actually uses a **binary + remediation flag**:

```csharp
record NullabilityDecision(
    ColumnCoordinate Column,
    bool MakeNotNull,
    bool RequiresRemediation,
    ImmutableArray<string> Rationales,
    SignalEvaluation? Trace);
```

V1's `RequiresRemediation = true` carries the semantics of
`RequireOperatorApproval` but is more flexible: a decision can be
`MakeNotNull = true` with `RequiresRemediation = true` (V1 says
"tighten, but block emission until data is fixed"), which the
masterwork's three-way DU cannot express directly.

**V2 design choice (don't pre-decide; surface for the V2 builder):**

Three plausible V2 shapes:

1. **Inherit V1's binary + remediation** — closest to the working code,
   preserves the "tighten but require remediation" combination. Loses
   the ubiquitous-language clarity of `EnforceNotNull / KeepNullable /
   RequireOperatorApproval`.
2. **Adopt the masterwork's ternary** — aligns with the bounded
   contexts; loses the "tighten + remediate" combination unless that
   becomes a fourth variant or an additional flag on `EnforceNotNull`.
3. **Hybrid with structured rationale**:
   ```fsharp
   type NullabilityDecision = {
       Outcome          : NullabilityOutcome  // ternary
       RequiresRemediation : bool             // additional flag
       Rationales       : Rationale list      // structured DU, not strings
       Trace            : SignalEvaluation option
   }
   ```
   The `Rationale` becomes a DU (`DataNoNulls | DataHasNulls of float
   | NullBudgetEpsilon | ProfileMissing | ForeignKeyEnforced |
   DataHasOrphans of int64 | NullabilityOverride | ...`) rather than a
   string array. Type-safe; self-documenting; tests assert on
   structured rationales rather than substring-matching strings.

I lean (3) for V2 — gain ubiquitous-language alignment, gain
type-safety on rationales, keep the "tighten + remediate" expressivity
V1 needs. **Surface for Danny's call.**

**Threshold configuration:**

Lives in `Policy.Tightening.NullBudget` per V1. V2 should keep it
global (per-policy, not per-column). When a real fixture surfaces a
need for per-attribute thresholds, refine.

**Evidence-supports / evidence-missing / evidence-contradicts:**

V1 distinguishes via rationale codes: `DataNoNulls` (supports),
`ProfileMissing` (missing), `DataHasNulls` (contradicts). V2's
`Rationale` DU should preserve this trichotomy explicitly — emitter
consumers may want to behave differently in each case.

### Existing test coverage

V1's tests are at `tests/Osm.Validation.Tests/NullabilityEvaluatorTests.cs`.
8 example-based tests, no Theory tests. V2 should add Theory tests for
the threshold boundaries V1 misses.

| V1 test | Lines | Category | Asserts | V2 translation |
|---|---|---|---|---|
| `EvidenceGated_Should_Tighten_MandatoryColumn_When_NullBudgetNotExceeded` | 16 | null-budget threshold | 4/100 nulls + budget 5% ⇒ MakeNotNull=true; rationales include `DataNoNulls`, `NullBudgetEpsilon` | **Behavioral** — same shape in F#; assert structural rationale list |
| `EvidenceGated_Should_StayNullable_When_MandatoryColumn_Has_Nulls` | 54 | null-budget threshold | 12/100 nulls + budget 5% ⇒ MakeNotNull=false; rationale includes `DataHasNulls` | **Behavioral** — V2 mirrors |
| `Cautious_Should_Block_MandatoryRelaxation_When_FlagDisabled` | 92 | mode-specific override | Cautious + AllowRelax=false + mandatory + nulls ⇒ MakeNotNull=true, RequiresRemediation=true; rationale `CautiousRelaxationDisabled` | **Behavioral** — V2 mirrors mode logic |
| `Cautious_Should_Allow_MandatoryRelaxation_When_FlagEnabled` | 133 | mode toggle | Cautious + AllowRelax=true ⇒ MakeNotNull=false; no `CautiousRelaxationDisabled` | **Behavioral** |
| `Aggressive_Should_Flag_Remediation_When_UniqueSignal_Exceeds_NullBudget` | 172 | aggressive + remediation | Aggressive + unique + 20/100 nulls ⇒ MakeNotNull=true, RequiresRemediation=true | **Behavioral** — exercises the "tighten + remediate" combination |
| `Analyze_Should_Create_Remediation_Opportunity_When_Data_Has_Nulls` | 209 | opportunity generation | `Analyze()` populates builder with remediation opportunity | **Skip / out-of-scope** — opportunities are V1's reporting concern; V2 separates Diagnostics from NullabilityDecisionSet |
| `Analyze_Should_Skip_Opportunity_For_Intentional_Nullability` | 260 | opportunity filtering | Non-mandatory column ⇒ no opportunity | **Skip** — same reason |
| `NullabilityOverride_Should_Keep_Column_Nullable` | 309 | policy override | Override rule ⇒ MakeNotNull=false; rationale `NullabilityOverride` | **Behavioral** — V2 mirrors override absoluteness |

V2 should add:

  - **Property**: `forall NullBudget ∈ [0.0, 1.0], any (Catalog,
    Profile) where every NullCount = 0 ⇒ MakeNotNull = true on every
    mandatory column`. The trivial case bound below the threshold.
  - **Property**: `forall (Catalog, Policy, Profile), the decision is
    deterministic — same triple ⇒ same NullabilityDecisionSet`
    (T1-extended).
  - **Property**: `overrides are absolute — for any column with a
    matching override, MakeNotNull = false regardless of every other
    field`.
  - **Behavioral**: probe-outcome gating — `Outcome = FallbackTimeout
    ⇒ profile-driven signals do not fire`.
  - **Behavioral**: missing-profile default — column absent from
    `Profile.Columns ⇒ rationale = ProfileMissing` (not equivalent to
    `DataNoNulls`).
  - **Behavioral**: physical-not-null signal — `Column.IsNullable =
    false ⇒ S2 fires regardless of mode`.

Differential testing against V1's `NullabilityEvaluatorTests` fixtures
lands when the C# adapter for evidence ingestion exists; the V1 tests
construct fixtures inline rather than from JSON files, so the V2
differential needs an inline-fixture bridge in the test harness. Lower
priority than the static-data adapter (session 5 commit 3); the
property + behavioral coverage carries the contract first.

### Migration path

1. **`Rationale` DU.** V2's first decision: structured rationales
   replacing V1's string codes. Lives in `NullabilityRules` module;
   exposed as part of the `NullabilityDecision` shape. Property tests
   assert on the DU rather than substring-matching strings.
2. **`Policy.Tightening` axis.** New sub-record on `Policy` (extending
   the three-axis A12 structure with a fourth domain — or embedding
   under one of the existing axes; the V2 builder decides). Carries
   `Mode`, `NullBudget`, `AllowCautiousRelaxation`, `Overrides`. Lands
   when `NullabilityPass` lands.
3. **`NullabilityRules` module.** Algebra/domain split: signal
   definitions, mode/signal matrix, threshold formula, rationale
   constructors. V1's `TighteningPolicyMatrix` is the source.
4. **`NullabilityPass`.** Pure F# pass in `Projection.Core.Passes`.
   Walks attributes, applies the decider, emits `Touched` (per
   attribute scanned) and `Annotated` events (per attribute with a
   non-trivial decision). Output is `Lineage<NullabilityDecisionSet>`.
5. **C# / F# adapter for evidence ingestion.** When real V1
   `ProfileSnapshot` JSON arrives, an adapter (likely F#, by analogy
   with the static-data adapter — DECISIONS to be appended on the
   language choice) coerces to V2's `Profile`. The V1 fixture format
   is documented in the masterwork (§3 / lines 221–303) and in
   `Osm.Json.ProfileSnapshotDeserializer`.
6. **Sequencing.** Lands after the static-data adapter (which proves
   the pattern); after Policy gains the Tightening axis; then
   `NullabilityRules`; then `NullabilityPass`; then a
   `Profile`-ingestion adapter; then the differential test against
   V1's fixtures.

### Edges / risks

- **Dictionary iteration determinism.** V1 iterates `_columnProfiles`
  (`IReadOnlyDictionary`) without explicit ordering. .NET 5+ preserves
  insertion order, but this is not an asserted contract. **V2 must
  enforce SsKey-sorted iteration** in `NullabilityPass` — same lesson
  as the `EntityDependencySorter` (DECISIONS 2026-05-08).
- **Mutable ISet inputs.** V1's four pre-computed unique-verdict ISets
  are stored as `ISet<ColumnCoordinate>`, not `ImmutableHashSet`.
  Caller mutation after construction would corrupt the evaluator. V2
  must accept immutable inputs only; the F# type system handles this
  by default.
- **Magic mode hardcoding.** Modes are an enum; their signal-matrix is
  hardcoded in `TighteningPolicyMatrix`. Adding a new mode requires
  C# code changes. V2 can either preserve the hardcoded matrix
  (simpler) or expose it as data (`Policy.Tightening.ModeMatrix : Map`)
  for runtime configurability. **Preserve the hardcoded matrix until
  evidence forces parameterization.**
- **TrustedConstraint semantics undocumented.** `ProbeOutcome.TrustedConstraint`
  is treated as equivalent to `Succeeded` in V1 but the meaning is
  nowhere documented. Best guess: "we didn't probe; the constraint
  was trusted." V2 should clarify in the `ProbeOutcome` DU
  documentation what each variant means.
- **NullCount sample-vs-population ambiguity.** V1's null-budget
  formula assumes `RowCount` is the actual table count, not a sample.
  If `ColumnProfile` ever carries sampled stats, the formula becomes
  wrong. V2's `Profile` should document this assumption explicitly,
  or carry a `SampleFactor` field if sampling enters scope.
- **Override scrubbing of remediation flags.** V1 lines 163–170: when
  an override applies, `RequiresRemediation` is set to false and
  conflict rationales are scrubbed. This is **operator approval as
  workaround, not solution** — V2 must document this so operators
  understand overrides mask data-quality issues without solving them.
- **Conditional-signals visibility is mode-dependent.** V1's logic
  (lines 116–127) only evaluates `dataTrace` if the conditional
  signal codes are satisfied. In Cautious mode, FK and Unique signals
  are TelemetryOnly and never appear in `dataTrace`. **The presence
  of evidence in rationales is mode-dependent.** V2's `Trace` field
  should preserve this visibility or surface it explicitly.
- **Silent FK-target absence handling.** V1's `ForeignKeySupportSignal`
  silently treats a missing FK target as "cannot tighten." V2 should
  either log a `Diagnostics` warning at the boundary (the FK target
  was absent — the Catalog is incomplete) or fail the pass. Silent
  degradation of evidence is the kind of thing the contract-testing
  audit (DECISIONS 2026-05-08) is meant to catch.

---

## 2026-05-10 — `UniqueIndexDecisionOrchestrator` (`src/Osm.Validation/Tightening/UniqueIndexDecisionOrchestrator.cs`)

**Status:** **extracted (differential confirmed)** + **carbon-copy
candidate for the evidence aggregator (2026-05-16 audible)** — the
orchestrator decision logic lives in F# (`Projection.Core.Strategies.UniqueIndexRules`
+ `Projection.Core.Passes.UniqueIndexPass`); V1's
`UniqueIndexEvidenceAggregator` is a candidate carbon-copy when the
chapter that consumes the lifted evidence opens. V1-migration mode
(`DECISIONS 2026-05-13 — admire spectrum`). V2's `Projection.Core.
Strategies.UniqueIndexRules` + `Projection.Core.Passes.UniqueIndexPass`
jointly carry V1's binary-decision contract into V2 under the codified
strategy layer (`DECISIONS 2026-05-11 — Strategy-layer codification`).

**Audible update (2026-05-16; V2 self-containment).** Of V1's three
Tightening evaluators, only `UniqueIndexEvidenceAggregator` is a
carbon-copy candidate: it joins declared unique indexes with profile
candidates to produce evidence sets (`SingleColumnClean`, `SingleColumnDuplicates`,
`CompositeClean`, `CompositeDuplicates`, `CompositeProfilesByKey`)
keyed by `ColumnCoordinate` / `UniqueIndexEvidenceKey`. The aggregator's
~150 LOC of join-and-aggregate logic is real derived data that V2 does
not separately carry in its IR. With a minor refactor at copy-time
(drop the two `enforce*Unique` policy gates; always populate both
clean and duplicate sets), the aggregator lands as a pre-Enrichment
DataIntent source. The carbon-copy could land in F# (rewrite at
copy-time) or in C# (preserved as the V1 source). Per `DECISIONS
2026-05-16 (later)`, the chapter consuming the lifted evidence makes
that call; the rule application stays in F# `UniqueIndexRules.fs:144`
either way. The orchestrator's distribution-shape (per-index decision
fanning out to constituent columns) stays in F# `UniqueIndexPass`.
`UniqueIndexPassTests.fs` and `UniqueIndexRulesTests.fs` exercise the
behavioral contract; the V1 `UniqueIndexDecisionStrategyTests`
divergences (Aggressive-mode collapse; included-columns boundary)
remain documented in this entry below but are not yet locked down by
explicit `Skip` stubs in `UniqueIndexPassTests.fs` (`CHAPTER_1_CLOSE.md
§2.7`; addressed in session 13's skip-stub completion).

**Significance.** The fourth V1 admire migration. Crucially, the
**second `TighteningIntervention` variant** — the closed DU
`TighteningIntervention | Nullability of ... | UniqueIndex of ...`
forces compiler-checked exhaustiveness across consumers and
empirically tests whether the pass-driver seam was positioned
correctly when Nullability landed alone.

### What it does (algebraic terms)

For each unique index in the model — single-column or composite —
decides whether to enforce uniqueness in DDL. Iterates
(module × entity × index), delegates per-index decision to a
`UniqueIndexDecisionStrategy` (which loads policy + profile evidence),
and distributes the resulting `UniqueIndexDecision` back to the
constituent column builders (composite indexes fan out; single-column
indexes are 1:1).

V1's decision shape is **binary** (no ternary): `EnforceUnique: bool`,
`RequiresRemediation: bool`, `Rationales: ImmutableArray<string>`.
V1 has no `RequireOperatorApproval` outcome — the matrix lookup in
`TighteningPolicyMatrix.UniqueIndexes` always resolves to a binary
decision plus a remediation flag.

### V2 placement

**Pure pass in `Projection.Core.Passes`, producing an
emitter-consumable `UniqueIndexDecisionSet` value per A32**, mirroring
`NullabilityPass`'s shape but with **per-index granularity** rather
than per-attribute. The closed DU `TighteningIntervention` gains a
second variant; the pass driver branches on the variant and
dispatches to the appropriate iteration shape.

Following the algebra/domain split (DECISIONS 2026-05-09):

  - **Algebra in the pass driver.** The driver pattern-matches on
    `TighteningIntervention`: `Nullability` walks attributes,
    `UniqueIndex` walks indexes. Each variant has its own iteration
    shape; the closed DU forces the dispatcher to handle both
    exhaustively.
  - **Domain in `UniqueIndexRules`** (new module, alongside
    `CycleResolution` and `NullabilityRules`). The per-index decider:
    given a `UniqueIndexTighteningConfig`, a `Kind`, an `Index`, and a
    `Profile`, return a `UniqueIndexDecision`.
  - **Typed seam between them.** A `Decider` type in
    `UniqueIndexRules` mirrors `NullabilityRules.evaluate`'s shape but
    operates on `Index` rather than `Attribute`.

### IR refinement required: `Index` on `Kind`

V2's `Catalog` does not yet model `Index` as a structural concept.
The synthetic milestone covered PK + FK only; unique indexes weren't
needed. UniqueIndex migration forces the IR refinement:

```fsharp
type Index = {
    SsKey      : SsKey
    Name       : Name
    Columns    : SsKey list      // attribute SsKeys, in declaration order
    IsUnique   : bool
    IsPrimaryKey : bool           // V1's PK is also an index
}

type Kind = {
    ...
    Indexes : Index list           // new field
}
```

The `Index` value type lives at namespace level (alongside
`Attribute`, `Reference`). `Kind` gains an `Indexes` field. The
`IsUnique` flag controls whether `UniqueIndexRules` evaluates this
index; the `IsPrimaryKey` flag distinguishes the PK from secondary
unique indexes (V1 treats both as unique, but the PK is structurally
separate).

This refinement lands in commit 5 alongside the rules module and
pass — **per "IR grows under evidence,"** not speculatively.

### Inputs and outputs (V2 IR)

Consumes:

  - **Catalog** — `Kind.Indexes`, `Index.Columns` (resolves to
    `Kind.Attributes` for column-level metadata if needed),
    `Index.IsUnique`.
  - **Policy** — `TighteningPolicy.Interventions` filtered to
    `UniqueIndex` variants. `UniqueIndexTighteningConfig` carries
    `EnforceSingleColumnUnique : bool` and
    `EnforceMultiColumnUnique : bool` — V1's two boolean toggles
    captured verbatim. No NullBudget; no Overrides (V1 has none).
  - **Profile** — `Profile.UniqueCandidates` (single-column; keyed by
    AttributeKey), `Profile.CompositeUniqueCandidates` (multi-column;
    keyed by KindKey + AttributeKey list), `ProbeStatus` for both.

Produces (per A32):

```fsharp
type UniqueIndexEvidence =
    | PhysicalUnique
    | SingleColumnClean
    | CompositeClean
    | DuplicatesAbsent of probeRowCount: int64

type UniqueIndexKeepReason =
    | PolicyDisabled                                  // EnforceSingleColumnUnique = false (or composite)
    | DataHasDuplicates of duplicateCount: int64
    | EvidenceMissing                                  // probe outcome ≠ Succeeded

[<RequireQualifiedAccess>]
type UniqueIndexOutcome =
    | EnforceUnique of evidence: UniqueIndexEvidence
    | DoNotEnforce of reason: UniqueIndexKeepReason

type UniqueIndexDecision = {
    IndexKey       : SsKey
    Outcome        : UniqueIndexOutcome
    InterventionId : string
}

type UniqueIndexDecisionSet = {
    Decisions : UniqueIndexDecision list
}
```

V2 adopts **binary outcome with structured evidence** (V1's binary
shape; V2's typed rationale per the V1↔masterwork principle from
DECISIONS 2026-05-09 — the principle pays out without forcing a
ternary where V1 has none).

### Existing test coverage

V1 tests live at:
- `tests/Osm.Validation.Tests/Policy/UniqueIndexDecisionOrchestratorTests.cs`
- `tests/Osm.Validation.Tests/Policy/UniqueIndexDecisionStrategyTests.cs`

| V1 test | File:line | Category | Asserts | V2 translation |
|---|---|---|---|---|
| `Evaluate_CreatesUniqueDecisionsAndOpportunities` | UniqueIndexDecisionOrchestratorTests.cs:16–76 | integration | orchestrator distributes decisions to column builders; opportunities created for remediation cases | **Behavioral** — V2 `UniqueIndexPass.run` produces `UniqueIndexDecisionSet`; opportunities pending Diagnostics writer (skip with rationale) |
| `PhysicalUniqueWithDuplicatesStillEnforcesInEvidenceMode` | UniqueIndexDecisionStrategyTests.cs:14–32 | scenario | physically-unique index + profile duplicates ⇒ EnforceUnique=true | **Property** — V2 `EnforceUnique(PhysicalUnique)` regardless of profile evidence |
| `AggressiveModeWithoutEvidenceRequiresRemediation` | UniqueIndexDecisionStrategyTests.cs:34–52 | scenario | Aggressive + missing profile ⇒ EnforceUnique=true + RequiresRemediation=true | **Skip** — V2 has no Aggressive mode (collapsed; arrives as new variant when demand surfaces) |
| `EvidenceModeTreatsOnDiskUniqueAsPhysicalReality` | UniqueIndexDecisionStrategyTests.cs:54–91 | scenario | OnDisk.Kind = UniqueIndex + empty profile ⇒ EnforceUnique=true | **Property** — V2 physical reality overrides missing profile |
| `EvidenceModeTreatsIncludedColumnsAsSingleColumnIndex` | UniqueIndexDecisionStrategyTests.cs:93–130 | edge case | included columns (non-key) don't count toward composite classification | **Behavioral** — V2 `Index.Columns` carries only key columns; included columns are physical-realization metadata, elided at the boundary (V1 vestigial-fields convention) |

V2 should add (V1 lacks coverage for these):
- Composite-unique scenarios (V1's tests are mostly single-column).
- Policy-disabled gates (`EnforceSingleColumnUnique = false`,
  `EnforceMultiColumnUnique = false`).
- Interaction with NullabilityPass (a unique column that is also
  nullable — does the unique decision affect the nullability
  decision?).

### Migration path

1. **`Index` on `Kind` IR refinement** (commit 5 setup) — small
   addition; updates fixtures with empty `Indexes = []` defaults.
   Documented in DECISIONS as the third IR refinement under "grows
   under evidence" (after `IsPrimaryKey` in session 3 and
   `IsMandatory` in session 7 commit 2).
2. **`UniqueIndexTighteningConfig` + `TighteningIntervention.UniqueIndex`
   variant** (commit 5 type additions). `Policy.fs` extends. Closed
   DU forces all consumers to pattern-match exhaustively; compiler
   surfaces incomplete dispatchers.
3. **`UniqueIndexRules` module** (commit 5 domain layer). Pure
   per-index decider; structured evidence/reason DUs.
4. **Pass driver dispatch** (commit 5 algebra layer). Refactor
   `NullabilityPass.run` (or introduce a higher-level
   `TighteningPass` driver) that pattern-matches on
   `TighteningIntervention` and dispatches to the appropriate
   iteration shape. The closed DU seam is empirically tested here:
   if the dispatcher feels forced, surface it.
5. **Test coverage**: per-index decisions; composite fan-out;
   policy-disabled gates; physical-unique override; differential
   parity against V1's tests (where V2 expresses them).

### Edges / risks

- **Per-index granularity vs. per-attribute granularity.** V1's
  composite index decision fans out to its constituent columns
  via builder side effects (lines 49–62 of orchestrator). V2's
  `UniqueIndexDecision` is per-index; the consumer (an emitter
  rendering `CREATE UNIQUE INDEX (col1, col2)`) knows it walks the
  index's columns. Fan-out is a consumer concern, not a decision
  concern — the decision is one per index, period.
- **Included columns are vestigial at V2's boundary.** V1's
  `Index.Columns` includes both key and "included" (non-key)
  columns; V1 filters during evaluation. V2's `Index.Columns`
  holds only key columns; the V1↔V2 adapter (when it lands) drops
  V1's included-columns metadata at the boundary, consistent with
  the 2026-05-10 vestigial-fields convention.
- **Closed DU dispatcher complexity.** When the third
  `TighteningIntervention` variant arrives (FK enforcement? Type
  tightening?), the pattern-match grows. If the dispatcher's
  cyclomatic complexity becomes load-bearing, refactor to a
  per-variant driver module (`NullabilityPass`, `UniqueIndexPass`,
  ...) called from a thin top-level dispatcher. The decision point:
  when the dispatcher has more than three variants and the per-
  variant logic exceeds ~10 lines, split.
- **Profile coverage gap on composites.** V1's
  `CompositeUniqueCandidateProfile` lacks a per-attribute null
  count for the composite (only `HasDuplicate`). V2's profile
  carries `ProbeStatus` for composites (a session-2 V2 fix); the
  Cleanup/Duplicate distinction is binary. Decisions that depend
  on null-distribution within a composite are not expressible from
  V1 evidence alone — surface to operators if the distinction
  matters.
- **Interaction with NullabilityPass.** A column that is both
  unique-candidate and nullable: V1's `NullabilityEvaluator` may
  tighten it via the unique signal; V1's `UniqueIndexDecisionOrchestrator`
  decides on the index. They are independent in V1's signal
  hierarchy. V2 preserves the independence — `NullabilityPass` and
  `UniqueIndexPass` produce separate decision sets; an emitter
  consumes both and resolves any conflict at the artifact level.
- **No override mechanism in V1.** V1 has nullability overrides
  but no unique-index overrides. V2 inherits this — the
  `UniqueIndexTighteningConfig` carries no override list. If a
  real V1 fixture surfaces a need (e.g., "skip uniqueness for this
  specific index"), it arrives under "IR grows under evidence" as
  an `Overrides` field on the config.

---

## 2026-05-11 — `ForeignKeyEvaluator` (`src/Osm.Validation/Tightening/ForeignKeyEvaluator.cs`)

**Status:** **extracted (differential confirmed)** — V1-migration mode
(`DECISIONS 2026-05-13 — admire spectrum`). V2's `Projection.Core.
Strategies.ForeignKeyRules` + `Projection.Core.Passes.ForeignKeyPass`
jointly carry V1's evaluator contract into V2; the strategy layer's
fourth instance and the empirical confirmation that the codification
holds without revision (`DECISIONS 2026-05-11 — Strategy-layer
codification: empirical verdict after the fourth instance`).

**Audible update (2026-05-16; V2 self-containment).** V2 placement
remains **PURE PASS in F#**. No carbon-copy candidate: the V1
evaluator's "raw" reads (`HasOrphan`, `HasDatabaseConstraint`,
`TargetEntity`, `DeleteRuleCode`) are already present in V2's IR via
`Catalog` and `Profile`; a carbon-copy would duplicate IR fields, not
contribute novel evidence. The V1 evaluator's policy-bound observations
(`IsIgnoreRule`, `crossSchemaBlocked`, `crossCatalogBlocked` against
`_options.AllowCrossSchema` / `_options.AllowCrossCatalog`) are the
mental-model artifacts V2 explicitly does not inherit; V2's
`ForeignKeyRules.fs` covers the rule space with structured
evidence/keep-reason DUs without the policy interleaving.
`ForeignKeyPassTests.fs` and `ForeignKeyRulesTests.fs` exercise the
behavioral contract. The `DeleteRuleIgnore` rationale-on-success
divergence (V1 emits a rationale string on a successful decision; V2
emits none) is documented below but not yet locked down by an explicit
`Skip` stub (`CHAPTER_1_CLOSE.md §2.7`; addressed in session 13's
skip-stub completion).

**Significance.** The fifth V1 admire migration; the **third
`TighteningIntervention` variant** lands the registered-intervention
flavor at N=3 instances and tests the freshly-codified strategy
layer (DECISIONS 2026-05-11) on its central case. If the codification
holds for ForeignKey without strain, the registered-intervention
sub-pattern is empirically validated.

### What it does (algebraic terms)

For each foreign-key reference in the model, decides whether to
create the FK constraint in DDL, and if so whether to script it
WITH NOCHECK (suppresses constraint validation against existing data
during creation). Iterates (entity × attribute × column-coordinate),
filters to references (`Attribute.Reference.IsReference`), consults
profile evidence (`ForeignKeyReality` keyed by ColumnCoordinate),
applies V1's signal hierarchy, returns a `ForeignKeyDecision`.

V1's decision shape:

```csharp
record ForeignKeyDecision(
    ColumnCoordinate Column,
    bool CreateConstraint,
    bool ScriptWithNoCheck,
    ImmutableArray<string> Rationales)
```

Two booleans (`CreateConstraint × ScriptWithNoCheck`) plus
free-form rationale strings. The four (CreateConstraint,
ScriptWithNoCheck) combinations:

  - `(true, false)` — straight enforce: constraint created normally.
  - `(true, true)` — Cautious-mode workaround: constraint created
    WITH NOCHECK because orphans or Ignore-rule prevent normal
    validation but the policy still wants the relationship recorded.
  - `(false, *)` — do not enforce; ScriptWithNoCheck is irrelevant.

### V2 placement

**Pure pass in `Projection.Core.Passes`, producing an
emitter-consumable `ForeignKeyDecisionSet` value per A32**, mirroring
`NullabilityPass` / `UniqueIndexPass`'s shape with **per-reference
granularity**. The pattern is by now established: the closed DU
`TighteningIntervention` gains a third variant
(`ForeignKey of id * ForeignKeyTighteningConfig`); each pass driver
filters to its variant via wildcard pattern; registered interventions
fan out into per-record decisions.

Following the codified strategy layer (DECISIONS 2026-05-11):

  - **Algebra in `ForeignKeyPass`** (sibling of `NullabilityPass`,
    `UniqueIndexPass`). Walks `Catalog → Module → Kind → Reference`,
    fans out over registered ForeignKey interventions, calls into the
    rules module per (reference × intervention), accumulates
    decisions, emits Annotated lineage events.
  - **Domain in `ForeignKeyRules`** (sibling of `NullabilityRules`,
    `UniqueIndexRules` in `Projection.Core/Strategies/`). Pure
    function: `(interventionId, config, kind, reference, profile) →
    ForeignKeyDecision`. Honors V1's signal hierarchy.
  - **Typed seam.** `evaluate` mirrors the shape established by
    `NullabilityRules` and `UniqueIndexRules`. The strategy layer
    codification's registered-intervention sub-pattern fits without
    revision, validating the codification.

### Inputs and outputs (V2 IR)

Consumes:

  - **Catalog** — `Reference.SsKey`, `Reference.SourceAttribute`,
    `Reference.TargetKind`, `Reference.OnDelete`. The (source kind,
    target kind) pair is needed for cross-schema / cross-catalog
    detection — but V2's `Catalog` does **not** currently model
    catalog (database) names; `Kind.Physical.Schema` is the only
    realization metadata. **IR refinement decision** in commit 5: do
    we add `Catalog.Catalog` (the database-name field, V1's
    `EntityModel.Catalog`)? See "IR refinement question" below.
  - **Policy** — `TighteningPolicy.Interventions` filtered to
    `ForeignKey` variants. `ForeignKeyTighteningConfig` carries
    V1's five toggles verbatim:

```fsharp
type ForeignKeyTighteningConfig = {
    EnableCreation                : bool   // V1: ForeignKeyOptions.EnableCreation
    AllowCrossSchema              : bool   // V1: AllowCrossSchema
    AllowCrossCatalog             : bool   // V1: AllowCrossCatalog
    TreatMissingDeleteRuleAsIgnore: bool   // V1: TreatMissingDeleteRuleAsIgnore
    AllowNoCheckCreation          : bool   // V1: AllowNoCheckCreation
}
```

  - **Profile** — `Profile.ForeignKeys` (`ForeignKeyReality` list)
    keyed by `ReferenceKey : SsKey`. V2's profile already carries
    `HasOrphan`, `OrphanCount`, `IsNoCheck`, `ProbeStatus` — the V1
    fields map directly. The `Profile.tryFindForeignKey` helper
    already exists in V2 (Profile.fs).

Produces (per A32):

```fsharp
type ForeignKeyEvidence =
    /// Database already enforces this constraint — V1's
    /// HasDatabaseConstraint = true. Trusted regardless of profile;
    /// the constraint exists, V2's job is to record it in DDL.
    | DatabaseConstraintPresent
    /// Profile probe succeeded; no orphans observed; eligible
    /// under cross-schema / cross-catalog gates and EnableCreation.
    | NoEvidenceObstacle of probeRowCount: int64
    /// V1's Cautious-mode workaround: orphans or Ignore-rule
    /// observed, but caller has AllowNoCheckCreation=true and
    /// EnableCreation=true. Constraint created with NoCheck flag;
    /// validation deferred. (Maps V1's CreateConstraint=true,
    /// ScriptWithNoCheck=true.)
    | ScriptWithNoCheck of orphanCount: int64

type ForeignKeyKeepReason =
    /// EnableCreation=false. Caller chose not to create FK
    /// constraints; gate reported, no domain reasoning.
    | PolicyDisabled
    /// Profile observed orphans and AllowNoCheckCreation=false.
    | DataHasOrphans of orphanCount: int64
    /// AllowCrossSchema=false and the FK crosses schemas.
    | CrossSchemaBlocked
    /// AllowCrossCatalog=false and the FK crosses catalogs.
    | CrossCatalogBlocked
    /// Delete rule = "Ignore" (or missing + TreatMissingAsIgnore);
    /// V1 does not enforce these by default.
    | DeleteRuleIgnored
    /// Profile probe did not succeed (FallbackTimeout / Cancelled /
    /// AmbiguousMapping); evidence missing; V2 collapsed-mode
    /// default declines to enforce.
    | EvidenceMissing

[<RequireQualifiedAccess>]
type ForeignKeyOutcome =
    | EnforceConstraint of evidence: ForeignKeyEvidence
    | DoNotEnforce      of reason:   ForeignKeyKeepReason

type ForeignKeyDecision = {
    ReferenceKey   : SsKey
    Outcome        : ForeignKeyOutcome
    InterventionId : string
}

type ForeignKeyDecisionSet = {
    Decisions : ForeignKeyDecision list
}
```

V2 adopts **binary outcome with structured evidence** (mirrors
UniqueIndexOutcome's shape; V1's `(CreateConstraint,
ScriptWithNoCheck)` two-boolean shape collapses cleanly into the
binary form because `ScriptWithNoCheck=true` only matters when
`CreateConstraint=true` — folding it into the
`EnforceConstraint(ScriptWithNoCheck _)` evidence variant captures
the semantic without inflating the outcome to ternary).

### IR refinement question: cross-catalog detection

V1's evaluator distinguishes **cross-schema** (different
SchemaName) from **cross-catalog** (different database / catalog
name). V2's `Catalog.PhysicalRealization` carries `Schema` and
`Table` only. Three options:

  1. **Defer cross-catalog detection.** V2's first-fixture milestone
     is single-database; cross-catalog is speculative for the
     synthetic fixtures. The strategy returns `CrossCatalogBlocked`
     only when `AllowCrossCatalog=false` AND a future IR field
     surfaces it. Today the rule is unreachable. Maps to "IR grows
     under evidence" — wait for the V1↔V2 adapter to surface a
     cross-catalog fixture, then add the IR field.
  2. **Add `Catalog : string option` to `PhysicalRealization`.**
     Small refinement; unblocks cross-catalog detection now.
     Speculative until a fixture forces it.
  3. **Synthesize from `Schema`.** Treat `dbo` differently from
     `dbo.Other` etc. V1 doesn't do this; not honest.

**Recommendation:** option 1 — defer. The V2 admire-then-extract
discipline says: implement what V1 forces; defer what V1 needs but
V2 hasn't surfaced yet. The cross-catalog rule lands as a `_` →
`Some kind` branch with a TODO when the fixture arrives.

Commit 5 will encode this: `ForeignKeyRules.evaluate` includes the
cross-schema branch (live; `Kind.Physical.Schema` exists) and a
`CrossCatalogBlocked` keep-reason variant in the DU (so the shape is
ready); the rule that produces it is unreachable today and emits a
documented `Annotated` lineage event ("cross-catalog detection
deferred — IR refinement pending real fixture").

### Existing test coverage

V1 tests live at `tests/Osm.Validation.Tests/Policy/ForeignKeyEvaluatorTests.cs`:

| V1 test | File:line | Category | Asserts | V2 translation |
|---|---|---|---|---|
| `Should_Block_CrossSchema_Constraint_When_Overrides_Disallow` | ForeignKeyEvaluatorTests.cs:14 | scenario | cross-schema FK + AllowCrossSchema=false ⇒ CreateConstraint=false, CrossSchema rationale | **Behavioral** — V2 `DoNotEnforce(CrossSchemaBlocked)` |
| `Should_Create_Constraint_When_Eligible_And_Creation_Enabled` | ForeignKeyEvaluatorTests.cs:75 | scenario | clean profile, EnableCreation=true ⇒ CreateConstraint=true, PolicyEnableCreation rationale | **Behavioral** — V2 `EnforceConstraint(NoEvidenceObstacle _)` |
| `TreatMissingDeleteRuleAsIgnore_AllowsCreation` | ForeignKeyEvaluatorTests.cs:140 | scenario | missing DeleteRule + TreatMissingAsIgnore=true ⇒ DeleteRuleIgnore rationale (does not block creation when otherwise eligible) | **Behavioral** — V2 evaluates `DeleteRuleIgnored` only when blocking; combined with eligibility it doesn't block. Translation requires care: V1's rationale-as-string shape emits `DELETE_RULE_IGNORE` even when the constraint is created, which V2 doesn't preserve (rationales are evidence DUs, not informational tags) — surface as **Skip with rationale** if the test depends on the string-level emission |
| `CautiousMode_WithOrphans_ScriptsWithNoCheck` | ForeignKeyEvaluatorTests.cs:208 | scenario | Cautious + orphans + AllowNoCheckCreation=true ⇒ CreateConstraint=true, ScriptWithNoCheck=true | **Behavioral** — V2 `EnforceConstraint(ScriptWithNoCheck orphanCount)` |

V2 should add (V1 lacks coverage for these):

  - Profile probe missing / unreliable ⇒ `EvidenceMissing` outcome
    (V2's collapsed-mode strict default; V1 implicitly falls through
    to `EnableCreation` gate).
  - Multiple ForeignKey interventions registered ⇒ fan-out per
    (reference × intervention).
  - Coexistence with NullabilityPass and UniqueIndexPass ⇒ the
    closed-DU dispatcher continues to filter correctly with three
    variants.
  - `CrossCatalogBlocked` is currently unreachable; add a
    documented Skip case with rationale tracking the IR refinement
    deferral.

### Migration path

1. **`ForeignKeyTighteningConfig` + `TighteningIntervention.ForeignKey`
   variant** (commit 5 type additions). Policy.fs extends. Closed
   DU forces `TighteningIntervention.id` and the variant filters
   in `TighteningPolicy.{nullability,uniqueIndex}Interventions` to
   handle the third variant. The compiler will surface every
   incomplete dispatcher; fix each as the closed DU intends.
2. **`ForeignKeyRules` module** (commit 5 domain layer; lands in
   `Projection.Core/Strategies/`). Pure per-reference decider;
   structured evidence/reason DUs as defined above.
3. **`ForeignKeyPass` driver** (commit 5 algebra layer; lands in
   `Projection.Core/Passes/`). Mirrors NullabilityPass / UniqueIndexPass:
   `run : Catalog -> Policy -> Profile -> Lineage<ForeignKeyDecisionSet>`.
   Observable identity on empty policy; fan-out over (reference ×
   intervention); Annotated lineage events.
4. **Test coverage**: per-reference decisions; multi-intervention
   fan-out; observable identity; differential parity against V1's
   four tests (three Behavioral, one Skip-with-rationale for the
   DeleteRuleIgnore string-emission divergence); coexistence with
   NullabilityPass / UniqueIndexPass.

### Edges / risks

  - **Reference-to-target physical resolution.** V1's
    `ForeignKeyTargetIndex` resolves references to target
    `EntityModel`s by a side table built from the model. V2's
    `Reference.TargetKind : SsKey` plus `Catalog.tryFindKind`
    expresses the same lookup directly. The cross-schema check
    needs both endpoints' `Physical.Schema`; the rule reads the
    target via `Catalog.tryFindKind`. If the target is missing
    (broken reference), the rule's behavior is **defer to a
    `MissingTarget` keep-reason** — surface it explicitly rather
    than silently failing.
  - **String-level rationale parity.** V1's rationale is
    `ImmutableArray<string>` with V1-specific tag strings
    (`DELETE_RULE_IGNORE`, `DATA_HAS_ORPHANS`, etc.). V2's outcome
    is a structured DU; the lineage event's `Annotated` detail
    string is the human-readable summary, not a machine-parseable
    tag. Differential parity tests that check rationale strings
    are Skip cases; tests that check decision booleans are
    Behavioral.
  - **`_mode` parameter in V1 conflicts with V2's mode-collapse.**
    V1's `ForeignKeyEvaluator` constructor takes a `TighteningMode`
    and uses `mode == Cautious` to gate the WITH NOCHECK path
    (line 159). V2 collapsed `TighteningMode` (DECISIONS 2026-05-09);
    the WITH NOCHECK path is gated by `AllowNoCheckCreation` instead.
    Per "V1↔V2 name mapping" precedent, document the rename in
    DECISIONS when commit 5 lands.
  - **Adapter-vestigial fields.** V1's `ColumnCoordinate` and
    `EntityContext` plumbing are V1's IR shape, not V2's. The
    V1↔V2 adapter (when it lands for FK) drops these at the
    boundary; V2 uses `Reference.SsKey` directly. This is the
    third instance of the vestigial-fields-die-at-the-adapter
    convention (DECISIONS 2026-05-10).

### Cross-strategy observation: evidence-shape generalization

Three registered-intervention strategies now consume Profile
evidence:

| Strategy | Profile evidence consumed | Decision context |
|---|---|---|
| Nullability | `Profile.Columns` (per-`AttributeKey`) | `Attribute` |
| UniqueIndex | `Profile.UniqueCandidates` (single) + `CompositeUniqueCandidates` (composite) | `Kind × Index` |
| ForeignKey  | `Profile.ForeignKeys` (per-`ReferenceKey`) | `Kind × Reference` |

The shape across all three is:

```
evaluate :
    interventionId ->
    config         ->          (* strategy-specific *)
    'context       ->          (* IR slice: Attribute | (Kind × Index) | (Kind × Reference) *)
    Profile        ->
    'decision                  (* strategy-specific *)
```

The `Profile` parameter is uniform; the `'context` slice and
`'decision` shape are strategy-specific. **This is starting to
suggest a generic `StrategyEvaluator<'context, 'config, 'decision>`
type alias at the strategy-layer level** — a shared
evidence-consumption discipline that names the three-input shape
explicitly.

**Surfacing, not pre-deciding.** Three instances at the same shape
is empirical; the question is whether the generalization earns its
place when the migration ships. Possible outcomes after commit 5:

  - **Codification fits without strain** ⇒ the generic shape is
    real; defer extracting the alias until N=4 forces it (same
    discipline as the registry deferral).
  - **Codification fits but the alias would clarify the seam** ⇒
    extract the alias as a sibling commit in this session; document
    the rationale in DECISIONS.
  - **Codification fits with the alias forcing awkward 'context
    parameterization** ⇒ the shape is not as uniform as it appears;
    record the failed generalization as a tracked observation
    pending a fourth instance.

The recommendation is to write `ForeignKeyRules.evaluate` with the
**same concrete signature** as `NullabilityRules.evaluate` and
`UniqueIndexRules.evaluate` — three positional arguments before
the `Profile` — and observe whether the shape feels uniform or
forced after the implementation lands. The reflection commit
(commit 6) is the natural place to record the verdict.

### Future Profile enrichment notes

ForeignKey is the third decision strategy whose Profile slice is
narrowly typed (one record per reference). Future strategies may
consume **richer** profile evidence — distributions, cardinality,
joint statistics across attributes, sequence/temporal evidence. The
strategy-layer codification accommodates this naturally (the
`Profile` parameter is generic across strategies; new fields land
under "IR grows under evidence"); the evidence-shape generalization
above (`StrategyEvaluator<...>`) would also support richer Profile
shapes without revision. Logging the connection here so the
rich-profiling sessions (the user's planned post-strategy-layer
work) inherit the strategy-layer's foundation.

---

## 2026-05-07 — `EntityDependencySorter` (`src/Osm.Emission/Seeds/EntityDependencySorter.cs`)

**Status:** **extracted (differential confirmed)** — V1-migration mode
(`DECISIONS 2026-05-13 — admire spectrum`). V2's `Projection.Core.
Passes.TopologicalOrderPass` + `Projection.Core.Strategies.
CycleResolution` jointly carry V1's sorter contract into V2 under the
algebra/domain split that named the strategy layer (`DECISIONS
2026-05-09 — Algebra/domain split pattern`). `TopologicalOrderTests.fs`
and `TopologicalOrderPassTests.fs` exercise behavioral contract;
`CycleResolutionTests.fs` covers the structural-strategy seam.
Two V1 contracts ADMIRE flags as Behavioral V2 translations remain
**features-not-yet-built** as of session 13 (`SortByForeignKeys_
SkipsAutoDetectionWhenManualCyclesExist`,
`SortByForeignKeys_DefersJunctionTablesWhenEdgesMissing`). Session 13
audit-during-validation finding: `CHAPTER_1_CLOSE.md §4 priority 4`
listed these as missing tests, but V2 lacks the supporting IR — there
is no `OrderingPolicy` axis carrying manual-cycle config and no
junction-table heuristic in `TopologicalOrderPass v3`
(`OrderingMode.JunctionDeferred` is declared in the DU but never
produced). Session 13 reserved the contract names via Skip stubs in
`TopologicalOrderPassTests.fs` so the implementation lands behind a
behavioral lock when the supporting IR ships. Implementation itself
is substantive next-chapter work, larger than the original priority-4
ranking suggested.

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

---

## 2026-05-12 — V1 profiling depth (`src/Osm.Pipeline/Profiling/SqlDataProfiler.cs`, `src/Osm.Domain/Profiling/*.cs`)

**Status:** **extracted (V2-growth confirmed)** — V2-growth mode
(`DECISIONS 2026-05-13 — admire spectrum`; this entry was named there
as the V2-growth template). V2's `AttributeDistribution` DU now carries
two operational variants — `Categorical of CategoricalDistribution`
(session 9) and `Numeric of NumericDistribution` (session 10) — under
the structural-commitment-via-construction-validation principle
(`AXIOMS.md` operational principle, line 555+). The `ProfileStatistics`
adapter (`Projection.Adapters.Sql/ProfileStatistics.fs`) is the V2
boundary; `DistributionsEmitter` (`Projection.Targets.Distributions/`)
is the first sibling Π consuming the rich evidence; the rich-profiling
end-to-end milestone (`RichProfilingEndToEndTests.fs`) validates the
full pipeline.

**Status update at chapter B.3 close (2026-05-19): Faker's deferred
trigger is STRUCTURALLY MET.** Chapter B.3 (LiveProfiler deep-probe
sweep) shipped three new evidence types at slice B.3.8
(`ForeignKeyCardinality` — fan-out cardinality per Reference;
`ForeignKeySelectivity` — value-frequency clumping per Reference;
`JointDistribution` — multi-FK co-occurrence per Kind) plus slice
B.3.5's `StatisticalMoments` lifted the Numeric variant from
"percentiles + range" to "percentiles + range + Mean + StdDev + CV."
All four gating evidence chain nodes named in the table below now
ship from cache derivations in `Projection.Adapters.Sql/LiveProfiler.Cache`.

Explicit promotion from deferred to scoped-for-implementation is a
chapter B.4 / chapter 5+ open decision; the structural prerequisites
are in place. See `CHAPTER_B_3_CLOSE.md` §"Substantive contributions
§3" + `DECISIONS Active deferrals — Faker emitter` (updated row).

**Significance.** The first admire entry that surfaces **V1 absence
to fill, not V1 logic to migrate**. Every prior admire (six entries)
named a V1 component whose behavior V2 carries forward. This entry
maps what V1 collects, what V1 doesn't, and what V2 needs in order
to support the next vector — distribution-aware strategies and
Faker-style synthesis. The V2 extensions land under "IR grows under
evidence" with V2 as the sole evidence source; the V1 boundary
adapter remains unchanged because there is no V1 evidence to adapt.

### What V1 collects (the inventory)

V1's `ProfileSnapshot` aggregates four evidence kinds. Read the
shape literally — what V1 captures is exactly what V1's strategies
consume; nothing more.

**`ColumnProfile`** (`src/Osm.Domain/Profiling/ColumnProfile.cs`):
captures **null-presence evidence only**. Per-column fields:

  - `Schema`, `Table`, `Column` — physical coordinate (V2 resolves
    to `AttributeKey` at the boundary).
  - `IsNullablePhysical`, `IsComputed`, `IsPrimaryKey`,
    `IsUniqueKey`, `DefaultDefinition` — catalog metadata
    redundantly carried in profile evidence. V2's adapter elides
    these (DECISIONS 2026-05-10 vestigial-fields convention).
  - `RowCount`, `NullCount` — the only quantitative evidence. The
    null fraction supports V1's nullability-tightening signal
    hierarchy.
  - `NullCountStatus` — probe metadata.
  - `NullRowSample` — operational diagnostic ("show me 5 rows that
    have NULL in this column"); not IR.

**`UniqueCandidateProfile`**: `HasDuplicate : bool` plus probe
status. **Single boolean.** No cardinality, no distinct count, no
duplicate count.

**`CompositeUniqueCandidateProfile`**: same shape — a single
boolean. V1 lacked probe status here (V2 added it as a session-2
fix; DECISIONS 2026-05-09).

**`ForeignKeyReality`**: `HasOrphan : bool`, `OrphanCount : int64`,
`IsNoCheck : bool`, plus probe status and an `OrphanSample`
(operational diagnostic).

**The pattern.** Every V1 profile evidence is a **uniform
binary-question outcome**: "are there nulls?", "are there
duplicates?", "are there orphans?" — yes/no plus a single count.
V1's profiling pipeline is structurally one-dimensional: it runs
binary-evidence probes, not statistical surveys.

### What V1 does NOT collect (the gaps)

V1's profile contains **zero distribution evidence**. Specifically
absent:

  - **Cardinality.** No distinct-count per attribute. (V1 can
    answer "are values unique?" via `HasDuplicate` but not "how
    many distinct values are there?")
  - **Value frequencies.** No per-value count for categorical
    attributes. ("How many rows have status='Active'?" is
    unanswerable from V1's profile.)
  - **Histograms / percentiles.** No quantile data for numeric
    attributes. ("What's the median order amount? P95?" — absent.)
  - **Range.** No min/max captured per attribute (numeric or
    temporal). ("What's the date range of CreatedAt?" — absent.)
  - **Pattern recognition.** No string-format evidence (common
    prefixes, lengths, regex shape). ("Are these emails valid?" —
    unanswerable.)
  - **Joint / cross-attribute statistics.** No correlation between
    attributes within a kind, no joint distribution across
    FK-connected attributes. ("Does Customer.Country correlate with
    Customer.PreferredLanguage?" — absent.)
  - **Sequence / temporal evidence.** No gap analysis for sequential
    IDs, no growth-curve density for time-keyed data. ("Are there
    gaps in the ID sequence?" — absent.)

V1's strategies (NullabilityEvaluator, UniqueIndexDecisionOrchestrator,
ForeignKeyEvaluator) reason exclusively over the binary-question
outcomes. There is no V1 strategy that consumes distributions
because V1 does not produce them.

### What V1 collects that's adequate

  - `RowCount` per column — sufficient as a denominator for null
    budgets. V2 reuses unchanged.
  - `NullCount` per column — sufficient for the V1 mandatory-relax
    signal hierarchy. V2 reuses unchanged.
  - `HasDuplicate` per unique candidate — sufficient for V1's
    binary uniqueness decision. V2 reuses unchanged.
  - `HasOrphan` + `OrphanCount` per FK — sufficient for V1's
    binary FK decision plus the WITH NOCHECK fallback. V2 reuses
    unchanged.
  - `ProbeStatus` everywhere — sufficient for V2's evidence-missing
    keep-reason variants. V2 reuses unchanged (and added it for
    composites in session 2).

### What V2 needs (the rich-profiling agenda)

The vector the user named at session-9 framing: distribution-aware
strategies and Faker-style synthesis. Each requires evidence V1
doesn't collect. The agenda for the next several sessions:

| Evidence type | First consumer | Driving need | Session target |
|---|---|---|---|
| Categorical value frequencies | Distribution-report Π | Synthetic generation; cardinality-aware tightening | **session 9 (this session)** |
| Numeric histograms / percentiles | Distribution-report Π → anomaly strategy | Numeric synthetic generation; outlier detection | session 10 |
| Range (min/max) per numeric / temporal | Synthetic generator (Faker Π) | Plausible synthetic numeric/temporal values | session 10–11 |
| Cardinality (distinct count) | Tightening strategies | Distribution-aware uniqueness reasoning | session 11+ |
| Joint distributions across FK pairs | Faker Π | Coherent synthetic data across relationships | session 12+ |
| Sequential / temporal density | Faker Π; growth-aware strategies | Plausible synthetic sequences | later |

The discipline applied: **each evidence type lands when its first
consumer arrives**, not speculatively. Categorical frequencies
land first because they are structurally simplest, broadly
applicable, and the obvious foundational evidence for synthetic
generation. The remaining types follow as their consumers surface.

### V2 extension shape (the architectural plan)

**Profile aggregate gains a new field.** V2's `Profile` record
acquires a `Distributions : AttributeDistribution list` field,
keyed by `AttributeKey : SsKey`. The first variant of
`AttributeDistribution` carries categorical value frequencies; new
variants land under "IR grows under evidence" as additional
evidence types arrive (numeric histograms, etc.). Closed DU keeps
the consumer-side pattern-match exhaustive.

**The DU shape (commit 2 will land this).**

```fsharp
/// Empirical evidence about an attribute's value distribution.
/// Categorical / numeric / temporal variants land as their
/// consumers arrive (per IR-grows-under-evidence). Session 9
/// commits the categorical case only; numeric and temporal
/// follow in subsequent sessions.
type AttributeDistribution =
    /// Categorical value frequencies — for attributes whose
    /// values are drawn from a small or moderate vocabulary.
    /// Captures observed distinct values with their occurrence
    /// counts. Truncation is explicit (the probe may have capped
    /// the vocabulary at a configured limit).
    | Categorical of CategoricalDistribution

type CategoricalDistribution = {
    AttributeKey  : SsKey
    Frequencies   : (string * int64) list
        /// Sorted by SsKey-style discipline: alphabetical by
        /// value to ensure deterministic ordering.
    DistinctCount : int64
        /// Total distinct values observed (≥ Frequencies.Length
        /// when truncated).
    IsTruncated   : bool
        /// True iff the probe capped the vocabulary at a limit;
        /// `Frequencies` is a prefix of the full distribution.
    ProbeStatus   : ProbeStatus
}
```

**Adapter shape (commit 3 question).** Two options:

  1. **Sibling adapter `ProfileStatistics.attach`.** The session-9
     adapter consumes a V2-defined JSON shape with a top-level
     `distributions` array. V1's `ProfileSnapshot` JSON remains
     untouched; `ProfileSnapshot.attach` continues to populate
     null/duplicate/orphan evidence as today; `ProfileStatistics.attach`
     populates the new field. Two adapters, composed at the
     boundary.
  2. **Extend `ProfileSnapshot.attach`.** Add a new `distributions`
     branch to the existing parser. Single adapter; the V1 fixture
     gains an optional new top-level array.

**Recommendation: option 1 (sibling adapter).** The rationale:
V1 has no equivalent JSON shape to mirror; a sibling adapter
visibly separates "V1-derived evidence" from "V2-only evidence" at
the file-system level (same architectural-vector-as-folder
discipline that the strategy layer absorbed in session 8); future
adapters for additional V2-only evidence (numeric histograms, etc.)
slot in cleanly as siblings without bloating the V1 adapter.

**First consumer: a sibling Π.** Per the user's session-9 framing,
the consumer is built as a sibling functor — `Projection.Targets.Distributions`
(or a smaller name to be decided) — that takes the enriched IR
(`Catalog × Profile`) and emits a distribution report. The
emitter validates that distribution evidence flows through V2's
emission discipline (sibling-Π commutativity holds: SSDT/JSON/Distributions
all consume the same enriched IR).

### Inputs and outputs (V2 IR)

**Consumes (commit 2 minimum):** A V2-defined JSON shape carrying
per-attribute categorical value frequencies. JSON shape (proposed):

```
{ "distributions": [
    { "Schema": "dbo", "Table": "OSUSR_S1S_COUNTRY", "Column": "CODE",
      "Kind": "Categorical",
      "DistinctCount": 3,
      "IsTruncated": false,
      "Frequencies": [
        { "Value": "CA", "Count": 1 },
        { "Value": "MX", "Count": 1 },
        { "Value": "US", "Count": 1 } ],
      "ProbeStatus": { "CapturedAtUtc": "...", "SampleSize": 3,
                       "Outcome": "Succeeded" } } ] }
```

**Produces (commit 4 first consumer):** A textual / JSON
distribution report — for each attribute with categorical evidence,
the observed vocabulary, distinct count, and truncation status.
The format is the consumer's choice; the emitter is a sibling Π,
not a side-effect formatter. Consumers downstream of session 9
include the eventual Faker emitter (which uses the frequency
distribution to weight synthetic value generation), an anomaly-
detection strategy (which compares observed vocabulary against
expected bounds), and a distribution-aware uniqueness strategy
(which tightens differently when distinct count approaches row
count vs. when it doesn't).

### Existing test coverage

V1 has no tests for distribution evidence because V1 collects none.
The V2 extension lands without V1 contracts to honor. The session-9
test discipline is therefore:

  - **V2-only contract tests** for the new evidence type — round-trip
    JSON ↔ IR; deterministic ordering of frequencies; truncation
    handling; probe-status integration.
  - **End-to-end differential tests** (commit 5) — a real V1 fixture
    augmented with V2-only distribution data, validated through the
    sibling Π. The differential here is structural (output shape
    matches expected shape on a known fixture), not contract
    (V1 has no contract).
  - **Sibling-Π commutativity** — the existing T4 / T11 tests already
    cover SSDT and JSON; session 9's Distributions Π joins them, and
    the same enrichment-yields-same-content claim applies.

### Migration path (rich profiling, multi-session)

  1. **Session 9 (this session).** Categorical value frequencies as
     the first evidence extension; `ProfileStatistics` sibling
     adapter; `Projection.Targets.Distributions` sibling Π;
     end-to-end differential test on a small fixture.
  2. **Session 10 (proposed).** Numeric histograms and percentiles
     as the second evidence extension; same shape (closed DU
     extends; adapter parses new variant; consumer extends).
  3. **Session 11 (proposed).** Cardinality / range as third
     evidence type; first distribution-aware strategy migration
     (e.g., a uniqueness strategy that consults distinct count).
     This session is also the projected cash-out point for the
     two strategy-layer deferrals (composition vocabulary, generic
     `StrategyEvaluator` alias) per the shared trigger logged in
     DECISIONS 2026-05-11.
  4. **Session 12+ (proposed).** Joint distributions; Faker-style
     synthetic emitter as a sibling Π. Ships when the supporting
     evidence has accumulated and the algebra has been validated
     end-to-end on real-fixture data.

### Edges / risks

  - **No V1 source for the new evidence.** The session-9 adapter
    consumes a V2-defined JSON shape. No real-database profiler
    populates it yet; that's session 10+ work (extending the
    SqlDataProfiler equivalent, or building a V2-native one). For
    session 9 the test fixtures synthesize distribution data
    directly. This is the first IR extension that does **not**
    pass through the V1↔V2 boundary; document the precedent.
  - **Truncation handling is the principal risk.** Categorical
    distributions for high-cardinality attributes (e.g., textual
    free-form fields) blow up if the probe doesn't cap the
    vocabulary. The DU's `IsTruncated` flag is the structural
    answer; the probe configuration (which lives on the adapter,
    not in the IR) decides where to cap. Document the truncation
    contract clearly so consumers know the difference between
    "distinct count ≤ Frequencies.Length" (full data) and
    "distinct count > Frequencies.Length" (truncated, draw caution).
  - **Determinism of ordering.** Frequency lists are sorted
    alphabetically by value to keep T1 byte-identity intact across
    repeat invocations and across different probe-result orders.
    Sort discipline is the pass-author's job (the rules consume
    the IR, not the JSON); the adapter sorts on parse.
  - **Distribution-aware strategies as the next inflection.** The
    point of the rich-profiling agenda is to enable strategies that
    reason about distributions, not just about presence/absence.
    When the first distribution-aware strategy lands (session 11+),
    the strategy-layer codification should hold without revision —
    distribution evidence is just a richer Profile shape, and
    strategies consume Profile generically. If the codification
    surfaces a strain at that moment, it's a third opportunity to
    refine.
  - **The diagnostic emitter is small but architecturally
    significant.** Building it as a sibling Π (not a one-off
    formatter) preserves the algebra's claim that emission is
    parameterized over the catalog. The discipline matters more
    than the size.

---

## 2026-05-13 — `CategoricalUniqueness` (per-attribute distribution-driven uniqueness inference)

**Status:** **extracted (differential confirmed)** — **hybrid mode**
(`DECISIONS 2026-05-13 — admire spectrum`). V2's `Projection.Core.
Strategies.CategoricalUniquenessRules` + `Projection.Core.Passes.
CategoricalUniquenessPass` carry the per-attribute decision logic;
`CategoricalUniquenessRulesTests.fs` and `CategoricalUniquenessPass
Tests.fs` exercise the V2-only contract (no V1 differential exists for
the V2-growth share). The strategy layer's third real test passed
without forcing a fourth refinement (`DECISIONS 2026-05-13 —
Strategy-layer codification reaches stability mark`); the hybrid admire
mode worked as the framework's first applied instance (`DECISIONS
2026-05-13 — Session 11 reflection`).

**Significance.** The fourth registered-intervention strategy under
the codified strategy layer (DECISIONS 2026-05-11), and the first
that consumes **distribution evidence** rather than binary-question
outcomes (nulls / duplicates / orphans). Session 11's job
(per the user's framing) is the codification's third real test —
this entry's strategy choice is the empirical input that decides
the test's pressure.

**Why this candidate.** Three options surfaced from session 10's
reflection (categorical-aware uniqueness, numeric-bounded mandatory,
cardinality-aware FK). I chose categorical-aware uniqueness because
it surfaces the most architectural variation while keeping the
evidence shape simple enough to test the codification cleanly:

  - **Per-attribute granularity for a uniqueness-style decision.**
    The existing `UniqueIndex` strategy decides per-index. This
    strategy decides per-attribute. Two strategies in the same
    *conceptual domain* (uniqueness) with different *granularities*
    is a real architectural variation — does the codification
    accommodate this without strain?
  - **Single Categorical evidence lookup per attribute.** No
    cross-attribute reach (cardinality-aware FK has that
    complexity); no need for a new evidence variant (numeric-bounded
    mandatory needs the Numeric variant). The evidence shape is
    the smallest first-distribution-consumer can be.
  - **Hybrid mode admire is honest.** V1's `UniqueIndexEvaluator`
    covers the uniqueness concept (per-index decision based on
    `HasDuplicate` booleans). V2 adds the per-attribute
    distribution-driven inference V1 can't perform — V1 collects
    no distinct-count evidence per attribute. The boundary between
    inheritance and growth is clear.

### Hybrid-mode admire (per the three-mode framework)

#### What V1 gives (the V1-migration share)

**V1 component:** `UniqueIndexEvaluator` /
`UniqueIndexDecisionOrchestrator`
(`src/Osm.Validation/Tightening/UniqueIndexDecisionOrchestrator.cs`,
already migrated as session-7's `UniqueIndexRules`).

**What carries over conceptually:**

  - **The uniqueness domain.** "Should this column / set of columns
    be considered a unique candidate?" is a question V1 answers
    per-index; V2 answers per-attribute (additionally) at session 11.
  - **The signal-hierarchy discipline.** V1's evaluator orders its
    signals (already-unique → policy-disabled → profile evidence).
    V2's CategoricalUniqueness inherits the discipline:
    no-evidence → evidence-missing → distinct-count-below-threshold →
    distinct-count-equals-observations → suggest-unique.
  - **The structured rationale convention.** V1's
    `UniqueIndexEvidence` and `UniqueIndexKeepReason` DUs (V2's
    re-expression of V1's free-form rationale strings) provide the
    template; V2's `CategoricalUniquenessEvidence` and
    `CategoricalUniquenessKeepReason` mirror the shape with
    distribution-specific variants.

**What does NOT carry over:**

  - **Per-index granularity.** V1's evaluator iterates indexes;
    V2's CategoricalUniqueness iterates attributes. They overlap
    in domain but not in iteration shape.
  - **`HasDuplicate` boolean evidence.** V1's evaluator reads a
    binary "are there duplicates?" probe outcome. V2's
    CategoricalUniqueness reads richer Categorical evidence
    (distinct-count + total observations + truncation flag).

#### What V2 grows (the V2-growth share)

  - **Per-attribute uniqueness inference.** V1 has no analog. The
    inference: an attribute whose Categorical distribution shows
    `distinctCount = totalObservations` (every observation is a
    unique value, not just duplicate-free) is empirically a unique
    column under the observed sample. The signal is *stronger* than
    V1's "no duplicates observed" because it requires the full
    vocabulary to be distinct, not just unobserved-duplicates.
  - **Distribution-driven evidence consumption.** First strategy
    that reads `Profile.Distributions` (specifically Categorical).
    Tests whether the codification's `evaluate` shape accommodates
    the new evidence type.
  - **Truncation-awareness.** When the underlying Categorical
    distribution is truncated (`IsTruncated = true`), the
    distinct-count is a lower bound — the column's full vocabulary
    extends beyond what was observed. The strategy must distinguish
    this from "complete vocabulary observed" cases.

### What it does (algebraic terms)

For each attribute on each kind, decides whether to suggest the
attribute as a unique-candidate based on Categorical distribution
evidence. Per (attribute × intervention), reads
`Profile.tryFindCategorical attributeKey`; applies the V2 signal
hierarchy; produces a `CategoricalUniquenessDecision`.

**The signal hierarchy:**

  1. **No Categorical evidence registered for the attribute** ⇒
     `DoNotSuggest(NoCategoricalEvidence)`.
  2. **Probe outcome unreliable** ⇒
     `DoNotSuggest(EvidenceMissing)`.
  3. **Distribution truncated** (vocabulary capped at probe limit)
     ⇒ `DoNotSuggest(VocabularyTruncated)` — without the full
     vocabulary the inference is unsafe.
  4. **`distinctCount < MinDistinctCountForUniqueness`** ⇒
     `DoNotSuggest(DistinctCountBelowThreshold)` — vocabulary too
     small to merit a unique suggestion (a binary attribute is
     rarely meaningfully unique).
  5. **`distinctCount < totalObservations`** ⇒
     `DoNotSuggest(DuplicatesObserved)` — repeats present.
  6. **Otherwise** ⇒ `SuggestUnique(EveryValueDistinct)` — every
     observation distinct, vocabulary above floor, evidence
     complete.

### V2 placement

**Pure pass in `Projection.Core.Passes`, producing an
emitter-consumable `CategoricalUniquenessDecisionSet` value per
A32**, mirroring the four existing strategy passes (Nullability,
UniqueIndex, ForeignKey at session 8; this is the fourth). The
fourth registered-intervention strategy under the closed
`TighteningIntervention` DU.

  - **Algebra in `CategoricalUniquenessPass`** (sibling of the
    existing four passes in `Projection.Core/Passes/`). Walks
    `Catalog → Module → Kind → Attribute`, fans out over registered
    CategoricalUniqueness interventions, calls into the rules
    module per (attribute × intervention), accumulates decisions,
    emits Annotated lineage events.
  - **Domain in `CategoricalUniquenessRules`** (sibling of
    existing four rules modules in
    `Projection.Core/Strategies/`). Pure function:
    `(interventionId, config, attribute, profile) →
    CategoricalUniquenessDecision`. Honors V2 signal hierarchy.
  - **Typed seam.** `evaluate` mirrors the existing pattern
    (`NullabilityRules.evaluate`, `UniqueIndexRules.evaluate`,
    `ForeignKeyRules.evaluate`). The fourth instance — the
    deferred `StrategyEvaluator<'context, 'config, 'decision>`
    alias decision (DECISIONS 2026-05-11) cashes out here. Per
    the shared trigger.

### Inputs and outputs (V2 IR)

**Consumes:**

  - **Catalog** — `Attribute.SsKey` for keying decisions; no
    structural fields.
  - **Policy** — `TighteningPolicy.Interventions` filtered to
    `CategoricalUniqueness` variants. Configuration:

```fsharp
type CategoricalUniquenessConfig = {
    /// Don't suggest uniqueness for vocabularies smaller than this.
    /// A binary attribute (distinctCount = 2) is rarely meaningful
    /// as unique; a single-value attribute (distinctCount = 1) is
    /// pathological. Default suggestion: 2 (the algebra forbids
    /// degenerate cases below).
    MinDistinctCountForUniqueness : int64
}
```

  - **Profile** — `Profile.tryFindCategorical attribute.SsKey`. The
    only evidence consumed; no other Profile fields read.

**Produces (per A32):**

```fsharp
type CategoricalUniquenessEvidence =
    /// Distinct-count equals total observations; vocabulary is
    /// fully distinct under the observed sample. The strongest
    /// V2 unique-candidate signal — stronger than V1's
    /// "no duplicates observed" because it requires the full
    /// vocabulary to be distinct.
    | EveryValueDistinct of
        distinctCount: int64 *
        totalObservations: int64

[<RequireQualifiedAccess>]
type CategoricalUniquenessKeepReason =
    /// No Categorical distribution evidence registered for this
    /// attribute.
    | NoCategoricalEvidence
    /// Categorical evidence's probe didn't succeed reliably.
    | EvidenceMissing
    /// `IsTruncated = true` — vocabulary cap hit; full distinct
    /// count not observed. Inference would be unsafe.
    | VocabularyTruncated
    /// `distinctCount < MinDistinctCountForUniqueness`. Vocabulary
    /// too small to merit a unique suggestion.
    | DistinctCountBelowThreshold of
        distinctCount: int64 *
        threshold: int64
    /// `distinctCount < totalObservations` — repeats observed.
    | DuplicatesObserved of
        distinctCount: int64 *
        totalObservations: int64

[<RequireQualifiedAccess>]
type CategoricalUniquenessOutcome =
    | SuggestUnique of evidence: CategoricalUniquenessEvidence
    | DoNotSuggest  of reason: CategoricalUniquenessKeepReason

type CategoricalUniquenessDecision = {
    AttributeKey   : SsKey
    Outcome        : CategoricalUniquenessOutcome
    InterventionId : string
}

type CategoricalUniquenessDecisionSet = {
    Decisions : CategoricalUniquenessDecision list
}
```

`KeepReason` carries `[<RequireQualifiedAccess>]` per the
session-8 codification refinement 1 (case names like
`EvidenceMissing` recur across strategies; qualification
disambiguates).

### Existing test coverage

**V1 test coverage:** none directly. V1's
`UniqueIndexDecisionOrchestratorTests` cover per-index uniqueness;
no V1 test covers per-attribute distribution-driven uniqueness
inference because V1 lacks the evidence (V1 collects no Categorical
distribution data per ADMIRE.md 2026-05-12).

**V2-only contract tests** for the new strategy:

  - Signal hierarchy at each branch (no evidence; unreliable probe;
    truncated vocabulary; below-threshold; duplicates observed;
    suggest-unique).
  - Decision metadata (AttributeKey carries; InterventionId carries).
  - Helpers (`enforces`-equivalent: `suggestsUnique`).
  - Determinism / reflexivity properties.
  - DU round-trip.
  - Closed-DU exhaustiveness on `KeepReason` after future variants
    arrive.

**End-to-end milestone (commit 6):**

  - Three-input projection (Catalog × Policy × Profile) where
    Profile carries Categorical evidence on a known attribute.
  - Verify: pass produces correct decision per the signal hierarchy.
  - Verify: lineage trail carries Annotated events with the
    intervention id and outcome category.
  - Verify: coexistence with the other four strategies (Nullability,
    UniqueIndex, ForeignKey, plus this one) on a mixed policy — the
    closed-DU dispatcher continues to filter correctly with five
    variants registered.

### Migration path

  1. **`CategoricalUniquenessConfig` + `TighteningIntervention.CategoricalUniqueness`
     variant** (commit 2 type additions). Policy.fs extends. Closed
     DU forces `TighteningIntervention.id` and the per-variant
     filters to gain the fourth case. F# enforces.
  2. **`CategoricalUniquenessRules` module** (commit 2 domain
     layer; lands in `Projection.Core/Strategies/`). Pure
     per-attribute decider; structured evidence/reason DUs as
     defined above; smart constructor for `Config` if any
     validation invariants emerge (likely just
     `MinDistinctCountForUniqueness >= 1`).
  3. **`CategoricalUniquenessPass` driver** (commit 3 algebra
     layer; lands in `Projection.Core/Passes/`). Mirrors
     existing four passes:
     `run : Catalog -> Policy -> Profile -> Lineage<CategoricalUniquenessDecisionSet>`.
     Observable identity on empty policy; fan-out over (attribute
     × intervention); Annotated lineage events.
  4. **Composition vocabulary decision** (commit 4). With four
     registered-intervention pass drivers, evaluate which
     composition primitives have surfaced two or more times. Codify
     what's earned; defer what hasn't. Per the
     two-consumer-threshold discipline (DECISIONS 2026-05-13).
  5. **`StrategyEvaluator` alias decision** (commit 5). Four
     strategies share the signature shape; if the fourth fits
     exactly, the generalization is canonical. If it diverges,
     defer per the same discipline.
  6. **End-to-end milestone + reflection** (commit 6). Real fixture
     data; sibling-strategy coexistence; reflection on the
     codification's third real test.

### Edges / risks

  - **Per-attribute vs per-index granularity tension.** This
    strategy decides per-attribute; the existing UniqueIndex
    strategy decides per-index. An attribute that this strategy
    flags as `SuggestUnique` and that participates in an index
    that UniqueIndex flags as `EnforceUnique` is doubly-confirmed.
    An attribute flagged `SuggestUnique` that does NOT participate
    in any unique index is a candidate for index creation —
    interesting downstream signal but not this strategy's concern.
    The strategies coexist; downstream emitters / strategies can
    correlate.
  - **Truncated vocabulary as a hard skip.** A truncated
    Categorical distribution might still satisfy
    `distinctCount = totalObservations` for the *observed sample*
    while missing values from the full population. The strategy
    declines to suggest in this case — the inference is unsafe
    without the full vocabulary. Operators who want to override
    this for a specific known-bounded vocabulary can do so via
    a future override-list config field (deferred until a real
    fixture demands).
  - **`MinDistinctCountForUniqueness` floor.** Set conservatively
    in the configuration. A binary attribute (`distinctCount = 2`)
    might be a meaningful unique key in some domains (e.g., a
    single-row toggle table); the floor is a default, not a
    forbidden zone — the caller's config chooses.
  - **No V1 differential test.** V1 has no analog. The session-11
    test discipline is V2-only contract tests + end-to-end milestone
    on V2-defined fixture data. Same shape as the rich-profiling
    extension (session 9 / 10).

### Cross-strategy observation: the deferred decisions converge here

Session 11 carries the cash-out trigger for two deferred decisions
from session 8 (DECISIONS 2026-05-11). With this fourth
registered-intervention strategy:

  1. **Composition vocabulary**: `fanOut`-style iteration is now
     inlined in four pass drivers. The threshold (DECISIONS
     2026-05-13 — emergent primitives) is met.
  2. **Generic `StrategyEvaluator` alias**: four strategies share
     the `(interventionId, config, context, profile) → decision`
     shape. The cross-strategy generalization can be empirically
     validated.

Commits 4 and 5 decide both. Per the user's session-11 brief and
the shared-trigger discipline (DECISIONS 2026-05-11), they cash
out together rather than in isolation.


---

## 2026-05-13 — OSSYS catalog producer (`src/AdvancedSql/outsystems_metadata_rowsets.sql` → `MetadataSnapshotRunner` → `SnapshotJsonBuilder` → `osm_model.json`)

**Status:** **extracted (chapter 2 close — JSON path; hybrid mode
operating)** + **carbon-copy in flight (chapter 5.0 slice α; 2026-05-17)** —
`DECISIONS 2026-05-13` — admire spectrum; session-23 amendment for
the in-flight status; session-25 chapter-2-close transition to
extracted; 2026-05-16 audible marking the chain as a carbon-copy
candidate for the chapter that consumes a live SQL-extraction
capability; 2026-05-17 chapter 5.0 slice α opens carbon-copy.

**Carbon-copy events:**
- **2026-05-17 (chapter 5.0 slice α).** `outsystems_metadata_rowsets
  .sql` carbon-copied verbatim from V1 source
  (`src/AdvancedSql/outsystems_metadata_rowsets.sql`) to V2 at
  `sidecar/projection/src/Projection.Adapters.OssysSql/Resources/
  outsystems_metadata_rowsets.sql`. Embedded as a resource in the new
  F# project `Projection.Adapters.OssysSql`; accessed via
  `MetadataExtractionSql.read()`. Parity test
  (`MetadataExtractionSqlTests`) verifies byte-equality against V1's
  source when the V1 trunk is present alongside V2. Per chapter 5.0
  open Q1: surrounding C# plumbing rewritten in F# at copy-time (not
  carbon-copied as C#).

**Audible update (2026-05-16; V2 self-containment).** The highest-
value V1 inheritance candidate for V2. V1's metadata-extraction chain
(`outsystems_metadata_rowsets.sql` + `MetadataSnapshotRunner` +
`SnapshotJsonBuilder` + the result-set processor chain; ~1,880 LOC) is
the load-bearing donor for V2's live Catalog acquisition. Under V2
self-containment, the carbon-copy lands in a dedicated C# adapter
project — proposed: `Projection.Adapters.OssysSql` (a new C# project
under `sidecar/projection/src/`, museum-polish, named for the
capability it adapts, not for V1). The carbon-copy includes the SQL
file (`outsystems_metadata_rowsets.sql`; copied verbatim — the SQL is
the truth) plus the C# orchestration plumbing, refactored for V2
vocabulary and idioms at copy-time (or in a follow-up commit if a
large refactor is involved).

V1's `OsmModel` aggregate-root reconstruction is **not inherited** —
V2's adapters consume the rowset shape directly per the chapter 3.2
close (`SnapshotRowsets` variant). The carbon-copy brings forward the
SQL + SQL extraction plumbing; V2 reconstructs `Catalog` from rowsets
fresh in its own adapter code, without going through V1's aggregate-
root mental model.

The carbon-copy preserves the JSON path (`SnapshotJson` variant of
`SnapshotSource`) as the offline-development source variant; the
canonical production source variant for cutover becomes the
live-SQL-extraction variant fed by the carbon-copied SQL. The chapter
that opens this carbon-copy decides at chapter open whether the C#
plumbing is preserved (carbon-copy + museum polish) or rewritten in
F# at copy-time. The SQL itself is preserved verbatim either way. Six substantive translation slices have
landed across sessions 18–22 and 24 through the `SnapshotJson`
input path; the chapter closes with the JSON path operationally
complete and the canonical `SnapshotRowsets` variant pre-scoped at
session 25 commit 11 (subagent #5) for chapter-3+ implementation.
The cross-module FK slice defers to fresh context as the highest-
priority deferred slice for the chapter-3 handoff.

V2's catalog reader exists at `src/Projection.Adapters.Osm/CatalogReader.fs`
and consumes V1's `osm_model.json` shape via the `SnapshotJson`
variant of `SnapshotSource`. **Twenty-five translation rules** have
landed in the running list at `DECISIONS 2026-05-15 — OSSYS
adapter translation rules` across six substantive slices. Production
V2 will eventually consume real OutSystems metadata via the
canonical `SnapshotRowsets` variant (operator-decided per
`DECISIONS 2026-05-15` session-20 amendment); until that variant
lands, V2 catalogs come from V1's JSON output with name-synthesized
SsKey (the bound on A1's identity-survives-rename guarantee
through the JSON path).

The OSSYS chapter exists in the strategic frame
(`DECISIONS 2026-05-15 — Strategic frame for the OSSYS implementation
chapter`) as one of eight load-bearing axes; this entry scopes the
chapter against that frame.

### Slices landed (chapter 2 in-flight progression)

  - **Session 18** — minimal slice (one entity, two non-reference
    attributes). Rules 1–11 in the running list. SsKey synthesis
    via name-derivation; structural translation; type primitives
    Identifier/Text → Integer/Text; placeholder Origin rule.
  - **Session 19** — reference-bearing slice. Rules 12–16. FK SsKey
    synthesis; V1 `reference_deleteRuleCode` → V2 `OnDelete`
    mapping; same-module assumption for `TargetKind`; `attributes[]`
    as primary source for references (relationships[] is V1's
    aggregation).
  - **Session 20** — external-entity slice. Rule 17. Origin
    three-way placeholder under JSON-path bound (V1's `EspaceKind`
    encoding stripped at JSON projection — a member of the
    JSON-projection-lossiness class; `isExternal: true` placeholder
    is `ExternalViaIntegrationStudio`).
  - **Session 21** — mixed-active slice. Rule 18. Inactive-records
    boundary choice (filter at adapter; bound documented;
    carry-through deferred to consumer demand). The
    V2-boundary-discipline class named explicitly.
  - **Session 22** — index-bearing slice. Rules 19–23. Index SsKey
    synthesis; V1 `isUnique`/`isPrimary` → V2 `IsUnique`/
    `IsPrimaryKey`; included-columns drop at the boundary;
    columns-by-ordinal sort.
  - **Session 24** — static-entity slice (last substantive slice
    in chapter 2). Rules 24–25 reaffirming session 18's rule 10
    under empirical pressure with enriched rationale: V1
    `isStatic: true` → V2 `Modality = [Static []]` (empty
    population intentional; the OSSYS adapter's responsibility
    ends at the modality flag; population data flows through
    `Projection.Adapters.Sql/Static.fs` separately, mirroring
    V1's own extraction split).

### Chapter-2 close — three classes of translation findings now visible

The chapter has produced the complete typology of V1↔V2
translation findings, codified at chapter-2 close
(`DECISIONS 2026-05-21 — Chapter 2 close: alternative-IR-surface
class`):

  1. **JSON-projection-lossiness** — V2 can't see X (resolved by
     `SnapshotRowsets`). Members: SsKey at every level; `EspaceKind`
     IS-vs-Direct distinction; `isSystemEntity`.
  2. **V2-boundary-discipline** — V2 sees X; V2's IR has no axis;
     V2 chooses (filter, carry-through, IR refinement). Members:
     inactive-records (rule 18); index translation choices (rules
     19–23); static-entity split (rules 24–25).
  3. **Alternative-IR-surface** — V2 sees X; primary IR has no
     axis; parallel V2 surface is the natural home. Members: V1
     `deleteRuleCode: "Ignore"` → Diagnostics emission (rule 13);
     V1 `attributes[].onDisk` envelope → routes to Profile / read-
     side adapter when that chapter materializes.

### What remains for chapter 3

  - **Cross-module FK slice** — refines rule 16's same-module
    assumption. Defers to fresh context per the chapter's
    runway plan; named in the chapter-close handoff document
    as the highest-priority deferred slice for chapter 3.
  - **Canonical `SnapshotRowsets` variant** — separate
    architectural slice when sequencing brings it. Pre-scoped at
    session 25 commit 11 (subagent #5). Lands the canonical
    resolution to the JSON-projection-lossiness class.

### What the V1 producer does (algebraic terms)

V1's catalog producer is a three-stage pipeline:

  1. **`src/AdvancedSql/outsystems_metadata_rowsets.sql`** (1184
     lines). A T-SQL script that runs against an OutSystems platform
     database (the schema named `OSSYS_*` plus user data in
     `OSUSR_*`). Produces multiple named result sets covering
     modules, entities, attributes, references, indexes, static
     populations, profiling probes, etc. Pure read; no DDL emission.
  2. **`MetadataSnapshotRunner`** (407 lines). Executes the script
     against a `DbConnection`, dispatches each result set to a
     registered `IResultSetProcessor` implementation, and accumulates
     the rows into an in-memory `OutsystemsMetadataSnapshot` (a
     C# DTO graph specific to V1's domain).
  3. **`SnapshotJsonBuilder`** (288 lines). Translates the in-memory
     snapshot into the canonical `osm_model.json` document — the
     serialized form V1's downstream pipeline consumes. The JSON
     document is the formal V1↔V2 contract.

The producer is impure (it reads from a SQL Server connection),
returns `Result<OutsystemsMetadataSnapshot>`, and is the
authoritative source of structural truth about a deployed
OutSystems environment.

### V1's extracted shape — the rowsets, in detail

The 1184-line SQL script reconciles two distinct sources of truth:
**OutSystems intent** (the `OSSYS_*` metadata tables describing
what the platform thinks the schema should be) and **physical
reality** (`sys.tables`, `sys.columns`, `sys.indexes`, `sys.foreign_keys`
describing what the database actually contains). The reconciliation
is a real architectural concern V2 must inherit — V2's IR must
distinguish what the source declares from what the data shows.

The script exports ~22 named rowsets in a fixed order. The
`MetadataSnapshotRunner` registers an `IResultSetProcessor` per
rowset name; rowset order is preserved on the wire but processors
key by name. Inventoried by purpose:

**Module / entity / attribute backbone:**

  - `#E` — espaces (modules): EspaceId, EspaceName, IsSystemModule,
    ModuleIsActive, EspaceKind, **EspaceSSKey**.
  - `#Ent` — entities: EntityId, EntityName, PhysicalTableName,
    EspaceId, EntityIsActive, IsSystemEntity, IsExternalEntity,
    DataKind (Static / Regular / etc.), PrimaryKeySSKey, **EntitySSKey**,
    EntityDescription.
  - `#Attr` — attributes: AttrId, EntityId, AttrName, **AttrSSKey**,
    DataType, Length, Precision, Scale, DefaultValue, IsMandatory,
    AttrIsActive, IsAutoNumber, IsIdentifier, RefEntityId,
    OriginalName, ExternalColumnType, DeleteRule, PhysicalColumnName,
    DatabaseColumnName, LegacyType, Decimals, OriginalType,
    AttrDescription.
  - `#RefResolved` — relationship resolution: AttrId → reference
    target entity (id, name, physical name, active flag).

The `*SSKey` columns are V1's identity primitives. They survive
renames and refactors per the OutSystems platform's contract; V2's
`SsKey` value type wraps them.

**Physical reality reconciliation:**

  - `#PhysTbls` — physical tables matched to entities: EntityId,
    SchemaName, TableName, object_id.
  - `#ColumnReality` — physical column metadata per attribute:
    AttrId, IsNullable, SqlType, MaxLength, Precision, Scale,
    CollationName, IsIdentity, IsComputed, ComputedDefinition,
    DefaultConstraintName, DefaultDefinition, PhysicalColumn.
  - `#ColumnCheckReality` — check constraints attached to columns:
    AttrId, ConstraintName, Definition, IsNotTrusted.
  - `#AttrCheckJson` — aggregated check-constraint JSON per attribute.
  - `#PhysColsPresent` — which logical attributes still exist
    physically (the inactive-but-physically-present case).

**Indexes:**

  - `#AllIdx` — all indexes (IX + UQ + PK): EntityId, object_id,
    index_id, IndexName, IsUnique, IsPrimary, Kind, FilterDefinition,
    IsDisabled, IsPadded, Fill_Factor, IgnoreDupKey, AllowRowLocks,
    AllowPageLocks, NoRecompute, DataSpaceName, DataSpaceType,
    PartitionColumnsJson, DataCompressionJson.
  - `#IdxColsMapped` — index columns mapped to attributes by
    physical or human name: EntityId, IndexName, Ordinal,
    PhysicalColumn, IsIncluded, Direction, HumanAttr.

**Foreign keys (physical reality):**

  - `#FkReality` — actual FK constraints: EntityId, FkObjectId,
    FkName, DeleteAction, UpdateAction, ReferencedObjectId,
    ReferencedEntityId, ReferencedSchema, ReferencedTable, **IsNoCheck**.
  - `#FkColumns` — column mappings per FK constraint.
  - `#FkAttrMap` — which attributes participate in actual FKs.
  - `#AttrHasFK` — flag: does this attribute have a real FK
    constraint?
  - `#FkColumnsJson` / `#FkAttrJson` — aggregated JSON for downstream
    consumers.

**Triggers:**

  - `#Triggers` — trigger metadata per entity: TriggerName,
    IsDisabled, TriggerDefinition.

**Aggregated JSON shapes (the canonical osm_model.json structure):**

  - `#AttrJson`, `#RelJson`, `#IdxJson`, `#TriggerJson` — per-entity
    aggregates produced via `FOR JSON PATH`.
  - `#ModuleJson` — per-module aggregate combining all of the
    above.

The final `osm_model.json` document is the assembly of these
aggregates — a single JSON file with `exportedAtUtc`, an array of
`modules`, each containing an array of `entities`, each containing
arrays of `attributes`, `indexes`, `relationships`, `triggers`. The
nested shape is what `Projection.Tests/Fixtures/*.json` mirror; V2's
`CatalogReader` will consume the same shape.

### What V2 will carry forward

The V2 catalog reader's job is to translate V1's reconciled output
into V2's IR. The carry-forward set:

  - **Identity primitives.** V1's `*SSKey` values become V2's
    `SsKey` instances. The translation is direct; V2 does not
    re-derive identity, it adopts V1's.
  - **Module / entity / attribute / reference / index structure.**
    The four-level hierarchy (modules → entities → attributes /
    references / indexes) maps cleanly to V2's `Catalog → Module →
    Kind → (Attribute | Reference | Index)`.
  - **Physical realization.** V1's reconciled `db_schema` /
    `physicalName` / `databaseColumnName` becomes V2's
    `PhysicalRealization` (kind level) and `Column.ColumnName`
    (attribute level).
  - **Modality marks.** V1's `DataKind` ("Static" / regular /
    etc.) becomes V2's `Modality.Static` / etc. Static populations
    flow into the catalog per A7.
  - **Type information.** V1's `DataType` / `LegacyType` /
    `ExternalColumnType` plus the `ColumnReality` SqlType becomes
    V2's `Attribute.Type` plus the `Column` shape. The V2 type
    correspondence (`DataType` → V2 algebraic type) is policy
    territory per A13; the *raw* type information is what the
    adapter carries forward.
  - **Origin.** V1's `IsExternalEntity` plus the espace's
    `IsSystemModule` flag map to V2's `Origin` three-way
    (`OsNative` / `ExternalViaIntegrationStudio` / `ExternalDirect`).
    The exact mapping rule is implementation-territory; the input
    fields are named here.
  - **Reference targets and delete rules.** V1's `RefEntityId` +
    `DeleteRule` map to V2's `Reference.TargetKind` + `OnDelete`.
    The `RefResolved` rowset's pre-computed lookup helps the
    adapter avoid reconciling these per-row.
  - **Index structure.** V1's `AllIdx` + `IdxColsMapped` map to
    V2's `Index.SsKey` / `Name` / `Columns` / `IsUnique` /
    `IsPrimaryKey`.

**The trailing rowsets carry information the JSON aggregation
strips** (session-20 amendment per
`DECISIONS 2026-05-15 — OSSYS adapter translation rules`,
session-20 amendment). The rowsets emitted by
`outsystems_metadata_rowsets.sql` (`#E`, `#Ent`, `#Attr`, etc.)
preserve fields that V1's `FOR JSON PATH` aggregations strip
during projection into `osm_model.json`:

  - **`SSKey` at every level.** `EspaceSSKey`, `EntitySSKey`,
    `PrimaryKeySSKey`, `AttrSSKey` are present in the rowsets;
    they are absent from the assembled JSON. V2's
    `SnapshotJson`-path adapter synthesizes SsKey from name
    fields today (per `DECISIONS 2026-05-15 — OSSYS adapter
    translation rules`, rule 1–3); the canonical
    `SnapshotRowsets` variant (when implementation lands) reads
    SSKeys directly.
  - **Per-table column structure.** The rowsets retain
    structural metadata that the JSON aggregation collapses.
    Specific examples will surface as fixtures grow under the
    OSSYS arc; the rowsets-as-input path future-proofs the V2
    boundary against the deferred-fields backlog.
  - **Other fields the JSON projections happen not to include.**
    The lossiness is at exactly one projection layer
    (`#AttrJson`, `#ModuleJson` via `FOR JSON PATH`), not
    end-to-end; data is available everywhere upstream.

### Canonical input path — evolving from JSON-only to JSON+Rowsets

The OSSYS adapter's input path is **evolving**:

  - **Current (sessions 18–19):** `SnapshotJson` only. V2
    consumes V1's canonical `osm_model.json`; SsKey is
    name-synthesized; the bound on A1's
    identity-survives-rename guarantee is documented per
    `DECISIONS 2026-05-15 — OSSYS adapter translation rules`.
  - **Planned:** `SnapshotJson` + `SnapshotRowsets`. The
    `SnapshotRowsets` variant lands as a third closed-DU case
    on `SnapshotSource` when chapter 2's organic flow brings
    it. Per the operator decision in `DECISIONS 2026-05-15 —
    OSSYS adapter translation rules`, session-20 amendment,
    the canonical resolution to the lossy-SSKey question is
    `SnapshotRowsets`. Implementation timing: likely after the
    current OSSYS adapter chapter completes its translation
    work through the `SnapshotJson` path.
  - **Future (out of scope today):** `LiveOssysConnection` for
    the case where V2 needs to operate without V1's chain in
    the loop entirely. Reserved as a future variant per
    `DECISIONS 2026-05-15 — OSSYS adapter parse signature`.

**Until `SnapshotRowsets` implements**, V2's catalog reader
continues operating through the `SnapshotJson` path with the
documented bounds. The two paths will coexist when the variant
lands — `SnapshotJson` remains valid; `SnapshotRowsets` is the
path that resolves A1's bound and provides the richer
extensibility surface.

### What V2 will explicitly NOT carry forward

V2's IR is generic algebraic; it does not carry V1's
domain-prescriptive vocabulary. Specific carry-forward exclusions:

  - **V1-specific type names.** `OsmModel`, `EntityModel`,
    `AttributeModel`, `RelationshipModel`, `ModuleModel` —
    these are V1's domain types. V2's adapter consumes the
    JSON document directly (or DTOs that mirror the JSON shape);
    V2 does not depend on V1's C# types. The boundary is data,
    not typed cross-references — per the cherry-pick discipline
    (`HANDOFF.md` — Cherry-pick discipline).
  - **Trigger metadata.** V1 carries triggers in `#Triggers` and
    emits them downstream. V2's IR has no Trigger type today.
    Triggers are deferred until a real V2 use case demands them
    (`CHAPTER_1_CLOSE.md §2.5` lists triggers as V1 outputs without
    V2 equivalents). When the use case lands, the IR refinement
    discipline applies — the OSSYS adapter retrieves the data
    from `#Triggers`; the IR grows; the emitter follows.
  - **Computed-column definitions.** V1's `ComputedDefinition`
    field surfaces `IsComputed=true` columns; V2's IR has no
    Computed-column variant. Same disposition as triggers —
    deferred until evidence forces the IR refinement.
  - **Partition / data-compression metadata.** V1's
    `PartitionColumnsJson` / `DataCompressionJson` fields. V2's
    IR has no concept; out of scope until a real consumer demands.
  - **Catalog (database) names.** V1 uses `db_catalog` to permit
    cross-catalog FKs; V2's `Reference` has no `Catalog` field.
    Reserved as a deferred IR refinement (Active deferrals index).
    The OSSYS adapter ignores `db_catalog` for now; if a fixture
    surfaces a non-null `db_catalog`, the adapter flags it via
    diagnostic emission.
  - **Diagnostic side-effects.** V1's extraction has no
    diagnostic-emission discipline; the script itself THROWs on
    unexpected conditions. V2's adapter wraps everything in
    `Result<Catalog>` plus optional `Diagnostics<_>` entries;
    THROW-style failures become `Error` severity entries.
  - **Filter parameters.** V1's `@ModuleNamesCsv`, `@IncludeSystem`,
    `@IncludeInactive`, `@OnlyActiveAttributes`, `@EntityFilterJson`
    are V1's input-level filters. V2's `Selection` axis on Policy
    handles equivalent filtering at the IR level (per A12 amended).
    The OSSYS adapter does NOT pass through V1-side filter
    parameters; selection happens after the catalog is read.
    Reads-everything-then-filters is the V2 disposition.
  - **Module-level `isSystem` and `isActive`.** V1 emits both at
    the module element (`SnapshotJsonBuilder.cs:120-121, 168-169`).
    V2's adapter consumes neither. `module.isSystem` carries the
    same semantic content as `entity.isSystemEntity` at module
    level; V2's `Origin` DU collapses both at the entity level
    via the JSON-projection-lossiness route (rule 17 placeholder).
    `module.isActive` parallels `entity.isActive` (rule 18 filters
    inactive entities; the module-level case defers until a
    fixture forces the question — see open question O2 resolved
    at session-25 chapter close).
  - **Per-attribute `default`.** V1 emits `attributes[].default`
    (the `DefaultValue` from `#Attr` at
    `outsystems_metadata_rowsets.sql:757`). Carries semantic
    content (column default value); V2's IR has no per-attribute
    default axis at the OSSYS-adapter level. The default-value
    information is part of physical reality; future emitters
    that need defaults pull from V2's Profile / read-side adapter
    output (alternative-IR-surface class), not from the OSSYS
    adapter. Re-open trigger: emitter chapter that needs default
    values at IR scope rather than emit-time scope.
  - **Per-attribute `refEntity_isActive`.** V1 emits the FK
    target's `isActive` flag at
    `outsystems_metadata_rowsets.sql:765`. Could matter for
    cross-module FK or for cases where the source is active but
    the target was retired. The OSSYS adapter ignores it; rule 18's
    filter operates on source attributes only. Re-open trigger:
    cross-module FK slice surfaces a case where target activity
    matters (chapter 3 deferred slice).
  - **Per-attribute `onDisk` envelope.** V1 emits a structured
    sub-object per attribute (`outsystems_metadata_rowsets.sql:770-790`)
    containing eleven physical-reality fields: `isNullable`,
    `sqlType`, `maxLength`, `precision`, `scale`, `collation`,
    `isIdentity`, `isComputed`, `computedDefinition`,
    `defaultDefinition`, `defaultConstraint`, plus
    `checkConstraints`. V1's `SnapshotJsonBuilder` includes the
    envelope inside `attributes[]`. V2's OSSYS adapter does not
    consume any of it.

    **Rationale: physical-reality is read-side-adapter territory.**
    `onDisk` is V1's snapshot of physical reality at extraction
    time; the read-side adapter (a future chapter) is V2's read of
    physical reality at deployment-validation time. They are
    parallel sources of the same information class at different
    temporal points. For the canary use case, the read-side
    adapter is the source of truth (it queries the deployed
    database directly); OSSYS's `onDisk` would be redundant when
    read-side lands.

    The chapter implementing the read-side adapter confirms this
    redundancy or refutes it. **Re-open trigger:** if the read-
    side adapter chapter discovers that V1's `onDisk` (V1-extracted
    reality at one point in time) and the read-side adapter's
    output (deployed reality at deployment-validation time) need
    to be compared as separate sources to detect drift, then the
    OSSYS adapter routes `onDisk` to V2's Profile alongside the
    read-side adapter's emission. Until that chapter, the OSSYS
    `onDisk` envelope is silently dropped.

    The decision and rationale codified at `DECISIONS 2026-05-21 —
    Chapter 2 close: alternative-IR-surface class` (session 25;
    third translation-finding class). The `onDisk` envelope is
    the first member of that class made explicit.

### What's structurally different in V2's IR

V1's snapshot models things V2's IR doesn't, and vice versa.
Naming the differences before the implementation chapter opens
makes the translation rule explicit:

  - **V2 distinguishes structure (Catalog) from evidence (Profile).**
    V1 conflates them — `OutsystemsMetadataSnapshot` carries
    both schema metadata AND probe-shaped reality (HasOrphan /
    NullCount-equivalents are not in V1 the same way they are in
    V2's `Profile`). V2's adapter splits V1's reconciled output
    into a structural Catalog and an evidence Profile. The
    OSSYS adapter's primary concern is Catalog construction;
    Profile sourcing happens through `ProfileSnapshot.fs` from
    a separate input.
  - **V2's `Origin` is a closed three-way DU.** V1's flags
    (`IsSystemEntity`, `IsExternalEntity`) are independent
    booleans. The V2 mapping requires deciding how the V1 boolean
    pair collapses to V2's closed three-way — implementation
    territory; the rule lands in the adapter's chapter.
  - **V2's `OnDelete` is a closed DU; V1's `DeleteRule` is a
    string (or null).** V1 supports `TreatMissingDeleteRuleAsIgnore`
    as a config knob because V1's DeleteRule can be missing.
    V2's `OnDelete` has no missing variant. The OSSYS adapter
    must decide a translation rule for V1's nullable DeleteRule;
    the chapter that addresses this lands a DECISIONS entry on
    the rule. (Note: this is the same gap session 16's FK
    activation surfaced, where V2's `DeleteRuleIgnored` keep-reason
    is unreachable from V2 fixtures today. The OSSYS adapter is
    where it becomes reachable.)
  - **V2's `Modality` is a list.** V1's `DataKind` is a single
    string per entity. The adapter normalizes V1's single string
    to V2's modality list.
  - **V2 has no `IsActive` / `IsDisabled` axis on most types.**
    V1's metadata threads activity flags throughout. V2's
    Selection policy handles "what's included" at the policy
    level; the IR doesn't carry per-record activity. The OSSYS
    adapter will need to decide: filter inactive records at the
    boundary (V2 IR sees only active), or carry them through and
    let Policy filter (V2 IR has all). Implementation choice;
    both are defensible.
  - **V2 has no separate "physical column name vs database column
    name" axis.** V1's `PhysicalColumnName` and `DatabaseColumnName`
    are different fields (one is OutSystems' canonical, one is
    the sys.columns reality). V2's `Column.ColumnName` is one
    string; the adapter chooses which V1 field to use (the
    reconciled name in `#Attr.PhysicalColumnName` post-backfill is
    the canonical choice).
  - **V2's `IsPrimaryKey` is per-attribute; V1's PK is per-index.**
    V1 represents the PK via `IsIdentifier=true` on attributes
    AND a separate index entry with `IsPrimary=true`. V2 carries
    `IsPrimaryKey` on Attribute (per session-3 IR refinement).
    The translation rule is: V2's `Attribute.IsPrimaryKey =
    V1's IsIdentifier`. The PK index itself is also represented
    in V2's `Index` list with `IsPrimaryKey=true`.

### Carry-forward summary

The OSSYS adapter is fundamentally a **reconciliation translator**:
it takes V1's already-reconciled (intent vs reality) snapshot and
projects it into V2's IR shape. The reconciliation work is V1's;
V2's adapter does shape translation. This division preserves the
F#-pure-core discipline (V2 doesn't run SQL; V2 reads JSON the V1
chain produces) while inheriting V1's hard-won reconciliation
logic.

### V2 placement

**F# adapter at the boundary; V2 does not consume V1's C# domain
types.** The V2-growth share is the V2 IR translation; the
V1-migration share is the JSON parsing (V1's `osm_model.json` is
the V2 contract). The recommended shape:

  - **`src/Projection.Adapters.Osm/CatalogReader.fs`** — F# adapter
    consuming the JSON document and producing `Result<Catalog>`.
    Lives alongside `Projection.Adapters.Sql` (which today carries
    `Static.fs`, `ProfileSnapshot.fs`, `ProfileStatistics.fs`).
    The V1 SQL script and `MetadataSnapshotRunner` continue to live
    on the V1 side; V2's adapter consumes only the JSON document
    they produce. The two-language boundary (F# adapter / SQL
    producer) keeps determinism testable in V2 without requiring a
    live database in the V2 test surface.
  - **Coordinate translation at the boundary.** V1's
    `EntityName` becomes V2's `SsKey` (per the V1↔V2 vocabulary
    mapping in `README.md`). V1's `TableName` becomes V2's
    `PhysicalRealization`. V1's `ModuleModel` becomes `Module`.
    The translation is structural; the V2 IR does not retain the
    V1 type names.
  - **Differential validation.** V1 fixture catalogs already exist
    under `tests/Fixtures/`; the natural V2 differential is a test
    pass that round-trips a V1 fixture through the new adapter and
    asserts the V2 catalog matches a hand-built V2 fixture. Same
    pattern as `StaticAdapterDifferentialTests.fs` (session 5
    commit 3).

### Inputs and outputs (V2 IR)

Consumes: a V1-shaped `osm_model.json` document (typically read
from a file path; the adapter takes a stream / string and parses).

Produces: `Result<Catalog>` — the V2 IR. Validation errors surface
as `ValidationError` instances with codes namespaced
`adapter.osm.*` (parsing failures, missing required fields, type
correspondences that don't translate cleanly). On success, a
fully-populated `Catalog` ready to consume by every V2 pass.

The adapter is a *fact-emitter* in the diagnostics sense — it may
emit `DiagnosticEntry` values via the new Diagnostics writer
(`DECISIONS 2026-05-06`; session 14 commit 3) for parser warnings
that don't fail the parse but the operator should review (e.g.,
"static cell coercion fell back to string for an unmapped type").
The `Source` field for adapter diagnostics is `adapter:OSSYS` per
the convention codified in `Diagnostics.fs`.

### Existing test coverage (V1)

V1's test coverage of the producer:

  - `tests/Osm.Pipeline.Tests/SqlExtraction/SnapshotJsonBuilderTests.cs`
    — unit tests on the JSON serialization shape.
  - Integration tests under `tests/Osm.Etl.Integration.Tests/` —
    end-to-end pipeline tests consuming real fixture JSON.

V2's test surface for the adapter (when implemented):

  - **Behavioral parity tests.** Round-trip a V1 fixture's JSON
    through the V2 adapter; assert the produced `Catalog` matches a
    hand-built V2 fixture.
  - **Property tests.** Permutation invariance (input JSON object
    keys re-ordered produce identical V2 catalog); idempotence
    (parsing twice yields equal catalogs); structural-commitment
    validation surfaces (a malformed JSON document fails the parse
    with a typed error, never produces a degenerate catalog).
  - **Differential tests against `tests/Fixtures/edge-case`** —
    same pattern as `StaticAdapterDifferentialTests.fs`.
  - **Skip stubs for V1 contracts V2 deliberately doesn't honor.**
    For example, V1 carries fields V2's IR doesn't model (V1
    domain-prescriptive types lost in translation); each surfaces
    as a Skip stub naming the divergence. Same discipline as
    session 13's stubs.

### Migration path

The implementing chapter's outline:

  1. **JSON parser scaffold.** F# JSON deserialization
     (`System.Text.Json` per `DECISIONS 2026-05-06` — Built-ins
     first; no hand-rolled serialization). DTO records mirroring
     V1's JSON shape live in
     `Projection.Adapters.Osm/Internal.fs` (private; not exposed).
  2. **Translation pass.** DTO → V2 IR. Coordinate translation
     (V1 names → V2 SsKeys); type-correspondence translation
     (V1 `DataType` → V2's IR types); structural-commitment
     validation at every smart-constructor call site.
  3. **Differential test fixture.** Embed a small V1 JSON fixture
     in the test file (matches the pattern from
     `StaticAdapterDifferentialTests.fs`); assert the V2 catalog
     matches a hand-built V2 fixture.
  4. **Property-test sweep.** Permutation invariance, idempotence,
     malformed-input rejection. FsCheck.Xunit for the
     combinatorial surface.
  5. **Integration with the existing milestones.**
     `EndToEndDifferentialTests.fs` and `RichProfilingEndToEndTests.fs`
     today build catalogs from F# fixture builders; the integrating
     commit teaches them to optionally consume the new adapter
     when a real fixture path is provided. Backward compatibility
     for fixture-built catalogs is maintained — the adapter is a
     new entry point, not a replacement.
  6. **Diagnostics-writer integration.** Adapter parse warnings
     (e.g., "fell back to string coercion for an unmapped data
     type") emit `DiagnosticEntry` values via the
     `LineageDiagnostics`-shaped pipeline. This is the second
     consumer of the Diagnostics writer (after UniqueIndexPass
     opportunity-stream from session 14 commit 5) and likely
     surfaces refinements during validation.

### Edges / risks

The substantive risks the implementing chapter inherits, beyond
the routine mechanical risks of a JSON parser:

  - **V1's JSON shape is V2's contract — stability matters.** The
    V1 `osm_model.json` schema is what V2 must consume reliably.
    Any V1-side change to the JSON shape becomes a V2-side
    breaking change. The adapter needs versioning or tolerance
    against minor V1 evolutions (e.g., new fields V2 ignores).
    Defer the strategy until evidence forces it; record the
    constraint here so the next agent doesn't be surprised.
  - **Coordinate translation is lossy in one direction.** V2's
    `SsKey` carries identity but discards V1's full coordinate
    context (schema, table, physical naming). Round-trip from
    `osm_model.json` → V2 `Catalog` → back-to-some-V1-form is **not**
    expected to be lossless; V2's IR is the authoritative shape,
    and any artifact V2 emits is grounded in V2's IR, not in the
    original JSON. The adapter is one-way.
  - **Performance / streaming considerations.** Real production
    `osm_model.json` documents are large (tens of MB for
    enterprise-sized OutSystems factories). A naive
    `JsonSerializer.Deserialize<T>` reads the whole document into
    memory; a streaming `Utf8JsonReader`-based parser is better
    for production scale. Defer until evidence forces a choice;
    start with the simple form.
  - **Static-cell coercion divergences from V1.** V1's
    `FixtureStaticEntityDataProvider.ConvertJsonValue` does
    type-aware decoding (Boolean, Integer, Decimal, DateOnly,
    TimeOnly, DateTime, DateTimeOffset, Guid) using catalog
    `DataType`. V2's existing `Static.fs` (`Projection.Adapters.Sql`)
    only handles raw JSON primitive kinds. The new
    `Projection.Adapters.Osm.CatalogReader` should align with V1's
    type-aware coercion, **or** explicitly defer with a DECISIONS
    entry naming the deferral. Real-fixture decimals and dates
    will surface this as a real issue (`CHAPTER_1_CLOSE.md §2.8`
    flagged the divergence).
  - **Profile sourcing is separate.** This adapter handles
    *catalog* (structural) data only. Profile (empirical evidence)
    is sourced from V1's profiling pipeline via a different
    boundary; `Projection.Adapters.Sql/ProfileSnapshot.fs` already
    exists for that. The two adapters compose at the
    `ProjectionInput` level, not at the source level.

### Why this entry exists now

`CHAPTER_1_CLOSE.md §4 priority 7` named this as a session-14 priority
("OSSYS catalog adapter ADMIRE stub") and session 14 took it. The
audit's framing — "this is the assumed-but-not-documented V1→V2
boundary for the catalog itself" — held: V2 had carried the
implicit assumption that a real catalog reader exists, but no
ADMIRE entry, DECISIONS entry, or code surface said so. This stub
makes the boundary explicit and the work nameable.

The implementation chapter is its own substantive arc — likely
multi-session, likely demanding refinements during validation
(audit-during-validation discipline applies). The next-chapter
agent inherits this entry as the starting point and follows the
migration path outline above. Implementation does not start in
this commit; the explicit framing is the deliverable.
