# Projection — V2 Pure Core

The pure-F# foundation of the OutSystems DDL exporter, V2. The pure core
lives entirely under `sidecar/projection/`. V1 (the existing C#
implementation in the rest of the trunk) continues to operate; V2 is
additive and will eventually orbit the pure core via an admire-and-extract
migration. The trunk's behavior is unchanged whether the pure core is
present or absent, and every commit here is cherry-pick safe.

## What this is

A faithful implementation of the algebra described in `AXIOMS.md`. A
catalog of identity-keyed kinds, lensed by a three-axis policy and
informed by empirical profile evidence, runs through a factored functor
(`Project = Π ∘ E`) to produce immutable content-addressed snapshots whose
construction makes determinism, lineage, modular composition, refactor
safety, and cross-projection consistency constitutive properties of the
system rather than external disciplines.

The pure core has no I/O, no mutation, and no dependence on time. All
effects live at the boundary, in C# adapters that produce F# value types
the core consumes. The two-language partition is the algebra/I-O seam.

## Layout

    sidecar/projection/
      README.md            - this file
      AXIOMS.md            - the formal system + V2 amendments, axiom-numbered
      DECISIONS.md         - append-only log of resolved questions
      ADMIRE.md            - append-only log of V1 admirations and V2 placements
      global.json          - SDK pin (mirrors trunk: 9.0.305, rollForward: disable)
      .editorconfig        - F#-aware formatting scoped to this folder
      Projection.sln       - V2's own solution; not added to trunk sln
      src/
        Projection.Core/                  - F#: IR, passes, projector, lineage
      tests/
        Projection.Tests/                 - F#: property and unit tests

Targets and adapters that follow in subsequent sessions:

      src/Projection.Targets.SSDT/        - F#: Π_SSDT (raw text first, DacFx later)
      src/Projection.Targets.Json/        - F#: Π_Json (sibling-functor proof)
      src/Projection.Adapters.Sql/        - C#: SQL Server boundary; OSSYS/OSUSR
      src/Projection.Adapters.Files/      - C#: file system; snapshot store
      src/Projection.Host.Cli/            - C#: imperative shell; orchestrator

## Three substantive inputs and one temporal dimension

V2 amends the original "three aggregates" framing (A6) to recognize three
substantive inputs:

- **Catalog** is structural truth — what kinds exist. Changes when schema
  changes. Sourced from a Catalog Reader at the boundary (V1's
  `OsmModel`).
- **Policy** is operator intent — three orthogonal axes (Selection,
  Emission, Insertion). Changes when humans decide.
- **Profile** is empirical evidence — what the data actually shows. Used
  by tightening passes (nullability, FK enforcement). May be empty for
  use cases that need no evidence.

Plus one temporal dimension:

- **Lifecycle** is time — the partial order under which all three evolve.

`Project : (Catalog, Policy, Profile) → Surface`. `E : (Catalog, Policy,
Profile) → EnrichedCatalog`. `Π : EnrichedCatalog → Surface`.

See `AXIOMS.md` for the full system, the V2 amendments, and the new
axioms.

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
| `ProfileSnapshot`   | `Profile` (commit 2)   | empirical evidence            |

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

## Conventions inherited from the trunk

- .NET 9 SDK pinned to 9.0.305 (matches trunk `global.json`).
- xUnit 2.5.3 + coverlet 6.0.0 for tests (matches trunk packages).
- `Result<'a>` + `ValidationError` for all expected failures; exceptions
  only for true invariant violations (port of
  `src/Osm.Domain/Abstractions/Result.cs`).
- Static `Create` factories returning `Result` for value-object validation.
- Immutable types throughout; F# records and discriminated unions only.

## Conventions specific to V2

- F# core has no I/O, no mutation, no time. C# adapters return F# value
  types. The boundary is named, typed, and tested.
- Every transformation pass is a pure function `Catalog -> Lineage<Catalog>`
  (or `(Catalog, Policy, Profile) -> Lineage<Catalog>` when it consumes
  policy or profile evidence).
- Every test that enforces an axiom or theorem names it: e.g.
  `` ``A4: kinds with same SsKey are structurally equal regardless of names`` ``.
  Failing tests point directly at the law they claim to satisfy.
- Identity (`SsKey`) is never used as a string in core code. Names are
  presentation strings only.
- Lineage (`Lineage<'a>`) is foundational provenance — constitutive,
  content-addressable, used for replay and refactor safety. A separate
  `Diagnostics<'a>` writer (single-channel for now, three-channel later
  per the constitution) carries human-consumable telemetry.

## Pointers

- Read `AXIOMS.md` first, including the V2 Amendments at the bottom.
  The code assumes the system.
- `DECISIONS.md` records resolved questions (V1 sidecar mandate +
  V2 mandate + per-question resolutions).
- `ADMIRE.md` records V1 admirations and their V2 placements; the
  bridge between V1's working knowledge and V2's pure architecture.
