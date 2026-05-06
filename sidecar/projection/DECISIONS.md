# DECISIONS

Append-only log of resolved questions during sidecar development. Each entry
captures the decision, the reasoning, and (where applicable) the axiom or
theorem it serves. Future readers — including future agents and Danny —
should be able to reconstruct context from this log without spelunking
commit history.

Format:

    ## YYYY-MM-DD — short title
    **Status:** decided
    **Context:** ...
    **Decision:** ...
    **Reasoning / consequences:** ...

---

## 2026-05-06 — Sidecar lives at `sidecar/projection/` with its own solution

**Status:** decided
**Context:** The trunk's main solution is `OutSystemsModelToSql.sln`; the
sidecar must be cherry-pick friendly and leave trunk behavior unchanged
whether the sidecar is present or absent.
**Decision:** Place all sidecar code under `sidecar/projection/`. Create a
separate `Projection.sln` inside that folder. Do **not** add sidecar
projects to the trunk solution.
**Reasoning / consequences:** A separate solution is the cleanest way to
honor the cherry-pick discipline and keep the trunk's build/CI surface
untouched. Cost: developers must `cd sidecar/projection/` (or pass the
sidecar's `.sln` path) to build and test the sidecar.

## 2026-05-06 — F# is introduced for the algebraic core

**Status:** decided
**Context:** The trunk is 100% C#. The architecture spec mandates F# for
the pure functor pipeline so that "the source code reads as the formal
system" and discriminated unions are first-class.
**Decision:** F# for `Projection.Core` and all enrichment passes. C# for
adapter projects later (`Projection.Adapters`, `Projection.Host`). The
algebra/I-O boundary is also the language boundary.
**Reasoning / consequences:** The DU and pipeline-composition idioms in F#
make the algebra visible at call sites. C# adapter code stays minimal and
speaks .NET libraries (DacFx, Hot Chocolate, ASP.NET) natively. Cross-
language values are F# records and discriminated unions, consumable from
C#.

## 2026-05-06 — Property testing via FsCheck.Xunit

**Status:** decided
**Context:** The trunk uses xUnit but no property-based testing library.
The axioms list literally tells us what properties to write; we need a
property-based test framework.
**Decision:** Add `FsCheck.Xunit` to the test project. Hedgehog is nicer
ergonomically but FsCheck has deeper .NET tooling integration and a longer
track record.
**Reasoning / consequences:** One new test-time dependency. Production
dependencies remain unchanged.

## 2026-05-06 — Lineage shape: hand-rolled writer with versioned events

**Status:** decided
**Context:** Every pass runs inside a lineage monad. Library writers
(FSharpPlus etc.) introduce dependency surface and obscure the algebra.
Custom writer keeps it visible. Per A23, lineage events must include
`PassVersion` to make provenance hashes stable across pipeline evolution.
**Decision:**

```fsharp
type Lineage<'a> = { Value: 'a; Trail: LineageEvent list }
and LineageEvent = {
    PassName    : string
    PassVersion : int
    SsKey       : SsKey
    TransformKind : TransformKind
}
```

`bind f m` produces `{ Value = f(m.Value).Value; Trail = m.Trail @
f(m.Value).Trail }` — earliest-first, chronological (A24). The `>>=`
operator and the computation expression both follow this order.
**Reasoning / consequences:** Hand-rolled, no external deps. Versioning
guarantees replay determinism. Chronological order is documented in code,
in `AXIOMS.md` (A24), and tested.

## 2026-05-06 — SsKey as a sum type: Original vs Derived

**Status:** decided
**Context:** Some passes (e.g., symmetric closure) introduce nodes whose
identities are derived from existing identities. These derivations must be
deterministic and machine-checkable, not stringly conventional.
**Decision:**

```fsharp
type SsKey =
    | Original of string
    | Derived of original: SsKey * reason: string
```

The original/derived distinction is in the type system; later passes can
pattern-match on whether a key was synthesized. The string-concat
alternative (e.g., `"::inv::"` separator) is rejected as too easy to bypass
and too hard to reason about under composition.
**Reasoning / consequences:** Slight type-system overhead; large
correctness payoff. Reserved `reason` strings will be enumerated as a
`DerivationReason` DU when more than one is in use; for now we use string
literals registered in this document. First reserved reason: `"inverse"`
(symmetric closure pass).

## 2026-05-06 — Π_SSDT first emission target is raw .sql-style text

**Status:** decided
**Context:** Building real SSDT artifacts requires DacFx interop and a real
SQL Server target; that is heavy machinery for the first milestone.
**Decision:** For the synthetic-fixture milestone, Π_SSDT emits external-
table declarations as text (diffable, dependency-free).
**Forward-looking note:** When real SSDT artifacts are required, use
**DacFx** (`Microsoft.SqlServer.DacFx`), not SMO. SMO is for live SQL
Server administration; DacFx is the SSDT-native tooling for DACPAC
construction and schema comparison. SMO will get close enough to be
misleading. **Do not go down the SMO path** for SSDT.

## 2026-05-06 — Static populations live in the catalog

**Status:** decided (per spec; logged for visibility)
**Context:** The unfold pass for static kinds needs row data. Two options:
keep rows in the catalog, or maintain a parallel data layer.
**Decision:** Per A7, row data lives in the catalog. The Catalog Reader
populates `staticPopulation` when reading from real meta; the unfold pass
lifts populations into type-level metadata for Π.
**Caveat parked:** Real populations may eventually be large or carry non-
trivial values. The algebra is indifferent; the implementation may want
bounded loading later. Do not pre-solve.

## 2026-05-06 — PhysicalRealization starts as `{ Schema; Table }`

**Status:** decided
**Context:** External kinds may eventually point at different
databases/servers, view-backed entities may need view metadata, and
identity-column overrides may be needed.
**Decision:** For the first milestone, `PhysicalRealization = { Schema:
string; Table: string }`. Widen when evidence forces it.

## 2026-05-06 — Result and ValidationError are F# ports of trunk shapes

**Status:** decided
**Context:** The trunk has a well-shaped `Result<T>` + `ValidationError` in
`src/Osm.Domain/Abstractions/`. Algebraic purity benefits from F# DUs.
**Decision:** F# port: `type Result<'a> = Success of 'a | Failure of
ValidationError list`. `ValidationError` is a record with `Code` (e.g.,
`"sskey.empty"`), `Message`, `Metadata` map. `Bind`, `Map`, `Ensure`
operators preserved. Error codes follow the trunk's
`"category.subject.problem"` lower-dot convention.
