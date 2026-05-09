# Projection — V2 Pure Core

The pure-F# foundation of the OutSystems DDL exporter, V2. The pure core
lives entirely under `sidecar/projection/`. V1 (the existing C#
implementation in the rest of the trunk) continues to operate; V2 is
additive and will eventually orbit the pure core via an admire-and-extract
migration. The trunk's behavior is unchanged whether the pure core is
present or absent, and every commit here is cherry-pick safe.

## What this is

A faithful implementation of the algebra described in `AXIOMS.md`. A
catalog of identity-keyed kinds, lensed by a four-axis policy and
informed by empirical profile evidence, runs through a factored functor
(`Project = Π ∘ E`) to produce immutable content-addressed snapshots whose
construction makes determinism, lineage, modular composition, refactor
safety, and cross-projection consistency constitutive properties of the
system rather than external disciplines.

The pure core has no I/O, no mutation, and no dependence on time. All
effects live at the boundary, in **F# adapters** (the language partition
was relocated by `DECISIONS 2026-05-09`; the original "F# core / C# shell"
framing has been superseded). Adapters return F# value types the core
consumes. The two-language partition is no longer the algebra/I-O seam;
the seam is structural — `Projection.Core` has zero I/O, adapters do.

## Layout

The current layout (chapter 3.1 closed at session 36; canary milestone
sequence + audit + first refactor batch shipped — see
`CHAPTER_3_1_CLOSE.md` and `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`):

    sidecar/projection/
      README.md              - this file
      KICKOFF.md             - 5-minute fresh-agent orientation
      CLAUDE.md              - navigation surface; operating disciplines
      HANDOFF.md             - bridge letter from chapter-3.1 close
      HANDOFF_CHAPTER_1.md   - preserved chapter-1 close letter
      HANDOFF_CHAPTER_2.md   - preserved chapter-2 close letter
      CHAPTER_1_CLOSE.md     - chapter-1 close synthesis (sessions 1-12)
      CHAPTER_2_CLOSE.md     - chapter-2 close synthesis (sessions 13-25)
      CHAPTER_3_1_CLOSE.md   - chapter-3.1 close synthesis (sessions 27-36)
      AUDIT_2026_05_DDD_HEXAGONAL_FP.md - five-agent audit at chapter-3.1 close
      AXIOMS.md              - formal system; A1-A40 with amendments
      DECISIONS.md           - append-only log of resolved questions (~95 entries)
      ADMIRE.md              - append-only log of V1 admirations and V2 placements
      VISION.md / SPINE.md / PLAYBOOK.md / STAGING.md / BACKLOG.md - strategic surfaces
      CHAPTER_3_PRESCOPE_*.md / CHAPTER_4_PRESCOPE_*.md - per-chapter pre-scope docs
      global.json            - SDK pin (9.0.305, rollForward: disable)
      .editorconfig          - F#-aware formatting scoped to this folder
      Projection.sln         - V2's own solution

      src/Projection.Core/                  - F#: IR, passes, projector, lineage, diagnostics
        Catalog.fs, Coordinates.fs, Identity.fs, Lineage.fs, Diagnostics.fs,
        Policy.fs, Profile.fs, Result.fs, TopologicalOrder.fs, Bench.fs,
        ArtifactByKind.fs, PhysicalSchema.fs, Types.fs
        Passes/                             - registered-intervention + structural passes
          CanonicalizeIdentity, NamingMorphism, NormalizeStaticPopulations,
          SymmetricClosure, TopologicalOrderPass, VisibilityMask,
          NullabilityPass, UniqueIndexPass, ForeignKeyPass,
          CategoricalUniquenessPass
        Strategies/                         - domain decision logic; algebra/domain split
          CycleResolution, NullabilityRules, UniqueIndexRules,
          ForeignKeyRules, CategoricalUniquenessRules, Composition

      src/Projection.Targets.SSDT/          - F#: Π_SSDT (Statement DU + Render + RawTextEmitter)
        Statement.fs                        - typed statement-stream form (A35 cash-out)
        Render.fs                           - statement → SQL text realization
        RawTextEmitter.fs                   - Catalog → seq<Statement>
      src/Projection.Targets.Json/          - F#: Π_Json (sibling-functor proof)
      src/Projection.Targets.Distributions/ - F#: Π_Distributions (rich-profile diagnostic)

      src/Projection.Adapters.Osm/          - F#: V1 JSON → V2 Catalog (OSSYS adapter)
      src/Projection.Adapters.Sql/          - F#: SQL Server readback / static / profile adapters
        AsyncStream.fs                      - pull-based async streaming primitive
        ReadSide.fs                         - schema + row readback (streaming)
        Static.fs / ProfileSnapshot.fs / ProfileStatistics.fs

      src/Projection.Pipeline/              - F#: orchestration + canary
        Pipeline.fs                         - composition surface
        Bulk.fs                             - SqlBulkCopy realization layer (A36 cash-out)
        Deploy.fs                           - canary; executeStream; Docker JIT bring-up
      src/Projection.Cli/                   - thin driving adapter

      src/Projection.Adapters.Sql/          - F#: SQL Server boundary (Static cell coercion;
                                              ProfileSnapshot; ProfileStatistics)
      src/Projection.Adapters.Osm/          - F#: OutSystems metadata boundary
                                              (CatalogReader; SnapshotSource closed DU
                                              with planned SnapshotRowsets variant)

      tests/Projection.Tests/               - F#: property, unit, differential, end-to-end

Slots reserved for future sessions (not yet built):

      src/Projection.Pipeline/              - C#: canary orchestration (DacFx, testcontainers,
                                              ephemeral SQL Server). Strategic-frame axis per
                                              `DECISIONS 2026-05-15 — Strategic frame`.
      src/Projection.Adapters.Sql.ReadSide/ - F#: SQL-Server-back read-side adapter (canary
                                              read-back + optional production observation).
                                              Strategic-frame axis.
      src/Projection.Adapters.Files/        - C#: file system; snapshot store
      src/Projection.Host.Cli/              - C#: imperative shell; orchestrator
      src/Projection.Targets.SSDT.DacpacEmitter/ - F#: real CREATE TABLE / DacFx
      src/Projection.Targets.Faker/         - F#: synthetic-data Π consuming Profile

Plus the planned **`SnapshotRowsets` variant** of `SnapshotSource` in
`Projection.Adapters.Osm.CatalogReader` — operator-decided canonical
resolution to V1's JSON-projection lossiness class
(`DECISIONS 2026-05-15 — OSSYS adapter translation rules`, session-20
amendment). Lands when chapter 2's organic flow brings it.

## What's already shipped (built primitives)

The chapter-1 close (sessions 1–12) plus chapter 2's substantive work
to date have built:

- **The algebraic core** — `Catalog`, `Profile`, `Policy` (four-axis),
  `SsKey` identity, the IR pass framework, `Lineage<'a>`.
- **The Diagnostics writer** (`Projection.Core/Diagnostics.fs`,
  session 14 commit 3) — single-channel writer parallel to Lineage.
  `Lineage<Diagnostics<'a>>` dual-writer composition for passes that
  produce decisions plus observer-relevant findings. The codification
  reached its stability mark at session 16 (heterogeneous third test
  via ForeignKey activation).
- **The strategy-layer codification** at its stability mark (session
  11) — Nullability, UniqueIndex, ForeignKey, CategoricalUniqueness
  all under the codified pattern.
- **Three sibling Π emitters** (SSDT raw text; JSON; Distributions)
  honoring A18 amended.
- **Three boundary adapters** — `Static.fs`, `ProfileSnapshot.fs`,
  `ProfileStatistics.fs` under `Projection.Adapters.Sql`; the OSSYS
  catalog reader under `Projection.Adapters.Osm` (in flight).

The two un-built primitives now gating substantive forward work:

  - **The OSSYS adapter's `SnapshotRowsets` variant** — operator-
    decided; lands when sequencing brings it. Resolves the
    JSON-projection-lossiness class (SsKey, EspaceKind,
    isSystemEntity).
  - **The pipeline canary** (`Projection.Pipeline` C# project) —
    strategic-frame axis. Self-validates artifacts against
    ephemeral docker SQL Server before publication.

## Three substantive inputs and one temporal dimension

V2 amends the original "three aggregates" framing (A6) to recognize three
substantive inputs:

- **Catalog** is structural truth — what kinds exist. Changes when schema
  changes. Sourced from a Catalog Reader at the boundary (V1's
  `OsmModel`). The OSSYS catalog adapter
  (`src/Projection.Adapters.Osm/CatalogReader.fs`) consumes V1's
  `osm_model.json` shape via the `SnapshotJson` variant of
  `SnapshotSource`; the canonical `SnapshotRowsets` variant
  (operator-decided) lands when sequencing brings it. Original
  chapter-2 backlog framing in `CHAPTER_1_CLOSE.md §2.10` and
  `§4 priority 7`.
- **Policy** is operator intent — **four** orthogonal axes (Selection,
  Emission, Insertion, **Tightening**). Tightening was added per
  `DECISIONS 2026-05-09 — A12 amended again` when the NullabilityEvaluator
  admire surfaced the need. Changes when humans decide.
- **Profile** is empirical evidence — what the data actually shows. Used
  by tightening passes (nullability, FK enforcement, uniqueness inference)
  and by emitters that surface evidence (Distributions). May be empty for
  use cases that need no evidence.

Plus one temporal dimension:

- **Lifecycle** is time — the partial order under which all three evolve.

`Project : (Catalog, Policy, Profile) → Surface`. `E : (Catalog, Policy,
Profile) → EnrichedCatalog`. `Π : EnrichedCatalog → Surface`, where each
`Π` consumes whichever subset of `Catalog × Profile` it needs but never
`Policy` (the load-bearing `A18 amended`, `DECISIONS 2026-05-12`). Three
sibling Π's are operational today: SSDT (`Catalog -> string`), JSON
(`Catalog -> string`), Distributions (`Catalog -> Profile -> string`).

See `AXIOMS.md` for the full system, the V2 amendments, and the new
axioms (A32–A34, T11). Read top-to-bottom; A18's amendment lives at the
bottom of the file and is the load-bearing form.

## The strategy layer

Domain decision logic lives in `Projection.Core/Strategies/` as a named
architectural concern adjacent to the algebraic core (`DECISIONS
2026-05-11 — Strategy layer: a named architectural vector`). The
codification reached its stability mark at session 11 (`DECISIONS
2026-05-13 — Strategy-layer codification reaches stability mark`), having
absorbed three real instances (Nullability, UniqueIndex/ForeignKey,
CategoricalUniqueness) under a coherent shape (per-record decisions keyed
by a single SsKey).

Canonical shape of a strategy module:

1. Pure functions of IR fields; no I/O, no mutable state.
2. A typed function-type alias is the seam:
   `StrategyEvaluator<'context, 'config, 'decision> =
      string -> 'config -> 'context -> Profile -> 'decision`
   (`DECISIONS 2026-05-13 — Generic StrategyEvaluator alias cash-out`).
3. Structured rationale DUs cover the decision space exhaustively.
   Continuous evidence is absorbed by adding variants at meaningful
   inflection points, not by carrying parametric confidence values
   (`DECISIONS 2026-05-13 — Discrete-rationale DUs`).
4. Lineage events fire only on actual decisions; total decisions with
   named-skip variants in the outcome DU.
5. Module name advertises the domain — `<Domain>Rules` suffix.

Pass drivers in `Projection.Core/Passes/` delegate to
`Composition.fanOut` when iterating registered interventions
(`DECISIONS 2026-05-13 — Composition vocabulary cash-out`). Four other
composition primitives (`fallback`, `accumulate`, `wrap`, `lift`) are
codified-but-deferred until a second consumer arrives.

## The rich-profiling vector

`Profile` carries empirical evidence beyond simple null/orphan counts.
Two `AttributeDistribution` variants are operational:

- `Categorical of CategoricalDistribution` — value frequencies with
  truncation as a first-class concern (`VocabularyTruncated` distinct
  from `EvidenceMissing`).
- `Numeric of NumericDistribution` — percentile bundle (Min, P25, P50,
  P75, P95, P99, Max) using `decimal` as the canonical type for
  continuous statistical evidence (`DECISIONS 2026-05-13 — Decimal is
  the default`).

Every distribution carries its invariants via smart constructors
returning `Result<'a>` — the **structural-commitment-via-construction-
validation** principle (`AXIOMS.md`, line 555+). Future evidence types
follow the same template; Faker (synthetic-data Π) is deferred until at
least a third evidence type lands or the limitations of two are
explicitly accepted (`HANDOFF.md`, "What's deferred").

## V1 ↔ V2 vocabulary mapping

The pure core uses general algebraic names (`Kind`, `Module`, `Catalog`)
because the algebra is source-agnostic — it must accommodate OutSystems
metadata today and DACPAC, OData, or other sources later. The
domain-prescriptive names from V1 live at the boundary, in the Catalog
Reader's translation. The mapping:

| V1 (`Osm.Domain`)   | V2 (`Projection.Core`) | Notes                         |
|---------------------|------------------------|-------------------------------|
| `OsmModel`          | `Catalog`              | top-level aggregate           |
| `ModuleModel`       | `Module`               | coproduct cell                |
| `EntityModel`       | `Kind`                 | the schema-level entity type  |
| `AttributeModel`    | `Attribute`            | scalar property of a kind     |
| `RelationshipModel` | `Reference`            | directional FK edge           |
| `EntityName`        | wrapped in `SsKey`     | logical identity, not display |
| `TableName`         | `PhysicalRealization`  | physical projection           |
| `ProfileSnapshot`   | `Profile`              | empirical evidence            |

Identity in V2 is whatever survives V1's most aggressive refactoring. For
OutSystems, that is the logical entity name (`EntityName`), not the
physical table name. When DACPAC support arrives, identity is whatever
DACPAC's most stable identifier is, wrapped in `SsKey`.

## Build and test

From inside `sidecar/projection/`:

    dotnet restore Projection.sln
    dotnet build Projection.sln -c Release --no-restore
    dotnet test Projection.sln -c Release --no-build

V2's solution is independent of the trunk's `OutSystemsModelToSql.sln`.
Either solution builds standalone.

Current baseline: 631 passed, 7 skipped (V2-divergence + reserved-but-
unbuilt-feature stubs), 638 total (session 22).

## Conventions inherited from the trunk

- .NET 9 SDK pinned to 9.0.305 (matches trunk `global.json`).
- xUnit 2.5.3 + coverlet 6.0.0 for tests (matches trunk packages).
- `Result<'a>` + `ValidationError` for all expected failures; exceptions
  only for true invariant violations (port of
  `src/Osm.Domain/Abstractions/Result.cs`).
- Static `Create` factories returning `Result` for value-object validation.
- Immutable types throughout; F# records and discriminated unions only.

## Conventions specific to V2

- F# core has no I/O, no mutation, no time. F# adapters at the boundary
  return F# value types. The boundary is named, typed, and tested.
- Every transformation pass is a pure function `Catalog -> Lineage<Catalog>`
  (or `(Catalog, Policy, Profile) -> Lineage<Catalog>` when it consumes
  policy or profile evidence).
- Every test that enforces an axiom or theorem names it: e.g.
  `` ``A4: kinds with same SsKey are structurally equal regardless of names`` ``.
  Failing tests point directly at the law they claim to satisfy.
- Identity (`SsKey`) is never used as a string in core code. Names are
  presentation strings only.
- Lineage (`Lineage<'a>`) is foundational provenance — constitutive,
  content-addressable, used for replay and refactor safety. A
  `Diagnostics<'a>` writer carries human-consumable telemetry; the
  dual writer `Lineage<Diagnostics<'a>>` is the pass-with-findings
  shape. Writer codification at chapter-2 stability mark; chapter-3.1
  added writer-fidelity discipline (`LineageDiagnostics.tellDiagnostics`
  and `Lineage.ofValueAndEvents` are the canonical primitives; manual
  record-building forbidden).
- Where V2 deliberately diverges from a V1 contract, the divergence
  surfaces as a `Skip` test stub at the test-file level, not as
  ADMIRE-prose commentary.

## Status at chapter-3.6 substantive close (2026-05-09; ritual deferred)

- **757 tests passing**, 0 skipped, 0 build warnings under
  `TreatWarningsAsErrors=true`. Lint clean across 26 rules.
- **DECISIONS.md supreme operating discipline carries 7 pillars**:
  (1) data-structure-oriented; (2) no string-concat aggressively;
  (3) built-in obligation; (4) FP promised land (≥95% pure);
  (5) coding-style commitments (DDD / point-free / hexagonal /
  hardcore FP); (6) **no V2-internal back-compat paths** —
  refactor fully at time of insight (chapter 3.6 codification);
  (7) **gold-standard library precedence + perf-clause** —
  use-case-specific lib → typed DU → StructuredString → documented
  LINT-ALLOW; every refactor cites perf implications; every
  hot-path function has `Bench.scope`; every loop flows through
  `Bench` iterators; every counter via `Bench.recordSample`
  (chapter 3.6 codification).
- **Result<'a> aliased to FSharp.Core**: `type Result<'a> =
  Microsoft.FSharp.Core.Result<'a, ValidationError list>`.
  FsToolkit.ErrorHandling 4.18.0 + .TaskResult adopted; `result {}` /
  `taskResult {}` / `validation {}` CEs natively available.
  `DiagnosticSeverity` qualified.
- **Canary scale ceiling**: 500k rows in 27s warm (5 tables × 100k rows
  per table; chapter-3.1 baseline holds).
- **Bench surface**: instrumented across Core / Adapters / Targets /
  Pipeline. Iterator-logging primitives: `Bench.scope` (RAII timing),
  `Bench.iterDo` / `iterMap` / `iteriDo` (per-element samples),
  `Bench.streamProbe` / `streamTransit` (lazy-sequence throughput),
  `Bench.recordSample` (external counters). Pass-entry scopes added
  at every pass `run`.
- **Statistical perf-gate** at `scripts/perf-gate.sh`: per-label
  `μ + Kσ` outlier detection across rolling history (N=20 runs);
  warm-up flat-tolerance fallback. Pre-commit hook runs in ~2s
  warm; soft-skip on missing Docker/dotnet. Stop hook surfaces
  per-message perf summary via `hookSpecificOutput.additionalContext`.
  Baseline at `bench/baseline-canary.json`.
- **AXIOMS** at A40 (chapter-3.1 cashed A35, A36, A39, A40; partial
  advance on A32; T1 strengthened to statement-stream determinism).

## Pointers

For the next-chapter agent, read in this order:

1. `KICKOFF.md` — 5-minute fresh-agent orientation.
2. `CLAUDE.md` — navigation surface; operating-disciplines table;
   load-bearing commitments.
3. `HANDOFF.md` — bridge letter from chapter-3.1 close; short on purpose.
4. `CHAPTER_3_1_CLOSE.md` — chapter-3.1 close synthesis (sessions
   27–36); four meta-codifications; forward signals.
5. `AUDIT_2026_05_DDD_HEXAGONAL_FP.md` — chapter-close five-agent audit;
   Tier 1/2/3/4 backlog by epistemic level + leverage.
6. `CHAPTER_2_CLOSE.md` — chapter-2 close synthesis (OSSYS adapter; 25
   translation rules; three-class typology).
7. `CHAPTER_1_CLOSE.md` — chapter-1 close synthesis; historical context.
8. `AXIOMS.md` — formal system; A1–A40. A35/A36/A39/A40 are chapter-3.1
   contributions.
9. `DECISIONS.md` — append-only resolution log. The most recent entries
   cover the OSSYS adapter implementation chapter; older entries remain
   in force unless explicitly superseded. An "Active deferrals" index
   at the top tracks deferred decisions with their trigger conditions.
7. `ADMIRE.md` — V1↔V2 bridge; one entry per V1 component admired and
   placed in V2.

This README is updated when the cumulative decisions warrant it (per
the chapter-close ritual codified at `DECISIONS 2026-05-14`); it is
not the source of truth for any specific question.
