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

The current layout (chapters 3.1, 3.5/3.6/3.7 substantive, 4.1.A
in-flight surface, 4.1.B α/β/γ shipped — CDC-silence canary green;
RawTextEmitter retirement complete; Tier-1 typed-AST transitions
complete — see `KICKOFF.md` for the full timeline + `HANDOFF.md`'s
chapter 4.1.A close arc + 4.1.B in-flight prologue):

    sidecar/projection/
      README.md                 - this file
      KICKOFF.md                - 5-minute fresh-agent orientation; canonical re-entry surface
      CLAUDE.md                 - navigation surface; operating disciplines (8 pillars)
      HANDOFF.md                - bridge letter; most recent prologue is chapter 4.1.A close arc + 4.1.B in-flight
      HANDOFF_CHAPTER_1.md      - preserved chapter-1 close letter
      HANDOFF_CHAPTER_2.md      - preserved chapter-2 close letter
      CHAPTER_1_CLOSE.md        - chapter-1 close synthesis (sessions 1-12)
      CHAPTER_2_CLOSE.md        - chapter-2 close synthesis (sessions 13-25)
      CHAPTER_3_1_CLOSE.md      - chapter-3.1 close synthesis (sessions 27-36)
      CHAPTER_4_1_A_CLOSE.md    - chapter-4.1.A close synthesis (in-flight surface) + RawTextEmitter retirement + Tier-1/2/3 transitions
      CHAPTER_4_1_A_OPEN.md     - chapter-4.1.A open document (strategic-frame eight-axis)
      CHAPTER_4_1_B_OPEN.md     - chapter-4.1.B open document (strategic-frame eight-axis; CDC-silence highest-stakes)
      AUDIT_2026_05_DDD_HEXAGONAL_FP.md - five-agent audit at chapter-3.1 close
      AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md - L1↔L2↔L3 coverage map + 3 proposed campaigns
      AXIOMS.md                 - formal system; A1-A40 with amendments (L2 layer)
      PRODUCT_AXIOMS.md         - operator-facing product axioms (L3 layer; sibling to AXIOMS.md)
      DECISIONS.md              - append-only log of resolved questions (Active deferrals index at top)
      ADMIRE.md                 - append-only log of V1 admirations and V2 placements
      V2_DRIVER.md              - destination KPI + operative backlog (supersedes BACKLOG.md, now a forwarding pointer)
      VISION.md / SPINE.md / PLAYBOOK.md / STAGING.md - strategic surfaces
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
                                              with SnapshotJson + SnapshotRowsets
                                              variants — both shipped at chapter 3.2)

      tests/Projection.Tests/               - F#: property, unit, differential, end-to-end

Slots reserved for future sessions (not yet built):

      src/Projection.Adapters.Files/        - C#: file system; snapshot store
      src/Projection.Host.Cli/              - C#: imperative shell; orchestrator
      src/Projection.Targets.SSDT.DacpacEmitter/ - F#: DacpacEmitter via DacFx (`Microsoft.SqlServer.Dac`).
                                              **DacFx adoption mandatory** at chapter open per the
                                              Tier-3 text-builder-as-first-instinct discipline
                                              (`DECISIONS 2026-05-10`); chapter not yet open.
      src/Projection.Targets.Faker/         - F#: synthetic-data Π consuming Profile

(Note: `src/Projection.Pipeline/` shipped at chapter 3.1 as **F#** —
not C# as originally scaffolded; corrects line 85 below; `Projection.Pipeline`
is the canary orchestrator. `src/Projection.Adapters.Sql.ReadSide/`
shipped under `Projection.Adapters.Sql/` per the chapter-3.1 / 3.6
ReadSide work.)

The **`SnapshotRowsets` variant** of `SnapshotSource` shipped at
**chapter 3.2 close (2026-05-10)**, resolving V1's JSON-projection-
lossiness class structurally. Five slices delivered SsKey carriage
at every level, reference rowsets, `EspaceKind` activation (Origin
three-way), `IsSystemEntity` activation (`ModalityMark.SystemOwned`
lift), and cross-source parity tests. See `CHAPTER_3_2_CLOSE.md` for
the substantive synthesis. A1's identity-survives-rename bound is
now operationally unblocked at the OSSYS-adapter boundary.

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

The remaining un-built primitive now gating substantive forward work:

  - **`SnapshotRowsets`** — SHIPPED at chapter 3.2 (commits
    `6dab9cd` → `a74b904`; bug fix `0336795`). JSON-projection-
    lossiness class structurally closed; A1 boundary-unblocked.
  - **The pipeline canary** — SHIPPED at chapter 3.1
    (`Projection.Pipeline` F# project; not C# as originally
    scaffolded). Self-validates artifacts against ephemeral docker
    SQL Server before publication. The operator-reality canary
    (50k rows × 300 tables, variegated) is the per-commit + per-
    Stop-hook perf-gate target.

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

Current baseline: **882 passed, 0 skipped non-canary tests** + ~16
Docker-dependent canary tests (chapter-3.2 close, 2026-05-10).
Build clean under `TreatWarningsAsErrors=true`; lint clean across
27 rules; perf-gate clean against the operator-reality baseline.

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

## Status at chapter-4.1.A close arc + 4.1.B in-flight (2026-05-10; joint chapter close ritual ran)

- **840 non-canary tests passing** + ~16 Docker-dependent canary
  tests (skip-if-no-Docker), 0 skipped, 0 build warnings under
  `TreatWarningsAsErrors=true`. Lint clean across **27 rules**.
- **DECISIONS.md supreme operating discipline carries 8 pillars**:
  (1) data-structure-oriented; (2) no string-concat aggressively;
  (3) built-in obligation; (4) FP promised land (≥95% pure);
  (5) coding-style commitments (DDD / point-free / hexagonal /
  hardcore FP); (6) **no V2-internal back-compat paths** —
  refactor fully at time of insight (chapter 3.6 codification);
  (7) **gold-standard library precedence + perf-clause** —
  use-case-specific lib → typed DU → StructuredString → documented
  LINT-ALLOW; every refactor cites perf implications; every
  hot-path function has `Bench.scope` (chapter 3.6 codification);
  (8) **domain-first naming and ubiquitous-language consistency**
  — every named type / function / file embodies the four-question
  domain-naming analysis BEFORE the name is committed (chapter 3.7
  codification).
- **Three named failure modes codified** (chapters 3.7 + 4.1.A close
  arcs): **performance-of-compliance** (LINT-ALLOW shaped like an
  audit trail without substance; pillar 7 amendment),
  **domain-blind naming** (name shaped like a placeholder for an
  absent domain concept; pillar 8), **text-builder-as-first-instinct**
  (StringBuilder reach as default for new emitters; pillar 1 + pillar
  7 amendment; Tier-3 codification this session). Plus
  **infrastructure-blame jumping** (jumps to "X infrastructure is
  unavailable" without verification probe; this session).
- **V2-driver KPI Phase 3 highest-stakes deliverable shipped**:
  CDC-silence-on-idempotent-redeploy canary GREEN under real SQL
  Server 2022 CDC (positive + sensitivity tests; chapter 4.1.B slice
  γ, commit `cdcd953`).
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
