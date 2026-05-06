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

## 2026-05-09 — `NullabilityEvaluator` (`src/Osm.Validation/Tightening/NullabilityEvaluator.cs`)

**Status:** admired (placement decided)

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

