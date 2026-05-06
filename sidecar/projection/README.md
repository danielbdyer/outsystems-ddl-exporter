# Projection Sidecar

A deterministic schema-projection system, built as a self-contained sidecar inside
this repository. The sidecar lives entirely under `sidecar/projection/`. Files
here do not reference files outside this folder, and the trunk's behavior is
unchanged whether the sidecar is present or absent.

## What this is

A faithful implementation of the algebra described in `AXIOMS.md`. A catalog of
identity-keyed kinds, lensed by a static policy through a factored functor
(enrichment composed with structural projection), produces immutable
content-addressed snapshots whose construction makes determinism, lineage,
modular composition, refactor safety, and cross-projection consistency
constitutive properties of the system rather than external disciplines.

## Layout

    sidecar/projection/
      README.md            - this file
      AXIOMS.md            - the formal system, axiom-numbered for code reference
      DECISIONS.md         - append-only log of resolved questions
      global.json          - SDK pin (mirrors trunk: 9.0.305, rollForward: disable)
      .editorconfig        - F#-aware formatting scoped to this folder
      Projection.sln       - the sidecar's own solution; not added to trunk sln
      src/
        Projection.Core/   - F#: IR, passes, projector, lineage monad
      tests/
        Projection.Tests/  - F#: property and unit tests

Targets that follow in later sessions:

      src/Projection.Targets.SSDT/    - F#: Pi_SSDT (raw text first, DacFx later)
      src/Projection.Adapters/        - C#: SQL I/O, DACPAC interop
      src/Projection.Targets.GraphQL/ - F#: Pi_GraphQL
      src/Projection.Host/            - C#: ASP.NET shell

## Build and test

From inside `sidecar/projection/`:

    dotnet restore Projection.sln
    dotnet build Projection.sln -c Release --no-restore
    dotnet test Projection.sln -c Release --no-build

The sidecar has its own solution. The trunk solution
(`OutSystemsModelToSql.sln` at the repo root) does not reference the sidecar
and is unaffected by it.

## Conventions inherited from the trunk

- .NET 9 SDK pinned to 9.0.305 (matches trunk `global.json`).
- xUnit 2.5.3 + coverlet 6.0.0 for tests (matches trunk packages).
- `Result<'a>` + `ValidationError` for all expected failures; exceptions only
  for true invariant violations (port of `src/Osm.Domain/Abstractions/Result.cs`).
- Static `Create` factories returning `Result` for value-object validation.
- Immutable types throughout; F# records and discriminated unions only.

## Conventions specific to this sidecar

- F# core, minimal C# adapters at the boundary. Algebra in F#; I/O in C#.
- Every transformation pass is a pure function `Catalog -> Lineage<Catalog>`.
- Every test that enforces an axiom or theorem names it: e.g.
  `` ``A4: kinds with same SsKey are structurally equal regardless of names`` ``.
  Failing tests point directly at the law they claim to satisfy.
- Identity (`SsKey`) is never used as a string in core code. Names are
  presentation strings only.

## Pointers

- Read `AXIOMS.md` first. The code assumes it.
- `DECISIONS.md` records resolved questions so future readers can reconstruct
  the reasoning without spelunking through commit history.
