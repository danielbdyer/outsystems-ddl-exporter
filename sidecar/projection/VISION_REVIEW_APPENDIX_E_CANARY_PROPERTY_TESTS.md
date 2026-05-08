# Appendix E — Canary as Property-Test Surface

**Date:** 2026-05-08
**Reviewing:** VISION.md @ commit `2fb51ef`; VISION_REVIEW.md
**Brief:** Design how to make the canary the *primary* verification vehicle by building it as a property-test surface (FsCheck + testcontainers), not as a handful of hand-written integration tests. Goal: heavy property coverage with tiny test code.
**Synthesis location:** `VISION_REVIEW.md`, `VISION_REVISION_2.md`

---

# Canary as property-test surface — design memo

## 1. Generator design

Hierarchy: bottom-up, well-formedness baked into composition, **not** post-filtered. Filtering loses too many samples and breaks shrinking.

```fsharp
module CatalogGen
open FsCheck
open Projection.Core

// Leaves
let genSsKey  : Gen<SsKey> =
    Gen.elements ["k1";"k2";"k3";"k4";"k5"]   // small interned pool — collisions matter
    |> Gen.map (SsKey.original >> Result.value)
let genName   : Gen<Name> = Arb.generate<NonEmptyString> |> Gen.map (fun s -> Name.create s.Get |> Result.value)
let genPrim   = Gen.elements [Integer; Decimal; Text; Boolean; DateTime; Date; Guid; Binary]
let genAction = Gen.elements [NoAction; Cascade; SetNull; Restrict]

// Attribute: PK flag handled at Kind level so we can guarantee >= 1 PK
let genAttrRaw : Gen<Attribute> = gen { ... }

// Kind — enforces "at least one PK" by construction (DacFx requires it)
let genKind ssKey : Gen<Kind> = gen {
    let! attrCount = Gen.choose(1, 6)
    let! attrs     = Gen.listOfLength attrCount genAttrRaw
    let attrs'     = attrs |> List.mapi (fun i a -> { a with IsPrimaryKey = (i = 0) })
    let! idxs      = Gen.listOfLength <|| (Gen.choose(0,3), genIndexFor attrs')
    return { SsKey = ssKey; ...; Attributes = attrs'; Indexes = idxs; References = [] } }

// Module — fresh attribute SsKeys scoped per kind
let genModule moduleKey kindKeys : Gen<Module> = ...

// Catalog — two-phase: first build kinds with no FKs, then *thread*
// References as a topological wiring step. Optional cycles via a
// `genCycleSpec` knob so we can exercise CycleResolution too.
let genCatalog : Gen<Catalog> = gen {
    let! moduleCount = Gen.choose(1, 3)
    let! kindCounts  = Gen.listOfLength moduleCount (Gen.choose(1, 5))
    let kindKeys     = ...                         // fresh, unique per module
    let! bareModules = ...                         // kinds with no References yet
    let allKinds     = bareModules |> List.collect (fun m -> m.Kinds)
    let! refs        = wireReferences allKinds     // picks valid (sourceAttr, targetKind) pairs
    return Catalog.applyReferences refs { Modules = bareModules } }
```

`wireReferences` is the load-bearing step: it picks `(sourceKind, sourceAttr, targetKind)` from the *already-generated set*, so FK targets always exist by construction. Cross-module is just "pick targetKind from any module"; intra-module is the same with a constraint. No filtering needed.

Register one `Arbitrary<Catalog>` via `Arb.register<CatalogArbs>()` in a module-init or per-property `[<Properties>]` attribute. FsCheck's `Gen.listOfLength` is the workhorse; everything composes.

## 2. Shrinking strategy

FsCheck's default record shrinker is useless here — it would shrink names character-by-character and produce nonsense. Define `Arb.fromGenShrink (genCatalog, shrinkCatalog)`:

```
shrinkCatalog c =
   seq {
     // Outermost first — biggest blast radius wins
     yield! dropOneModule c
     yield! dropOneKindFromAnyModule c
     yield! dropOneReferenceFromAnyKind c   // FKs before constraints
     yield! dropOneIndexFromAnyKind c       // indexes before columns
     yield! dropOneNonPkAttribute c         // never drop the PK
     yield! shrinkOnePrimitiveType c        // Decimal -> Integer -> Text
   }
```

Order is important: `dropModule >> dropKind >> dropReference >> dropIndex >> dropAttribute >> shrinkType`. FsCheck enumerates lazily; the first reproduction wins, and the cheapest reductions are first. Never shrink `SsKey` (identity is the spine; mutating it invalidates A4) and never shrink the PK flag below "exactly one PK" (DacFx invariant).

## 3. Predicate library

Each predicate is a `Catalog -> bool` (or `Catalog -> Catalog -> bool`) that becomes a `[<Property>]` taking `Catalog` (or pairs).

| Predicate | Statement | Cost |
|---|---|---|
| `roundTripBySsKey` | `emit catalog \|> deploy db \|> read = catalog` modulo Π-erased axes (Origin, Modality) | High (deploy) |
| `idempotentRedeploy` | `deploy c db; let alters = (deployScript c db) in alters.IsEmpty` — covers CDC-safety | High |
| `rawDacpacAgree` (T11) | `RawTextEmitter` and `DacpacEmitter` mention the same `SsKey` set | **Pure**, no DB |
| `siblingChorusAgrees` (T11 generalized) | for every Π in `[raw; json; dacpac]`, projected SsKey-set matches `Catalog.allKinds \|> List.map .SsKey` | Pure |
| `renameSurvives` (A1) | for `c` and `c' = renameRandomAttribute c`, `deployIncremental c c' db` produces zero `DROP COLUMN` (only `ALTER`) | High |
| `t1ByteEqual` (T1, text/JSON) | `emit c = emit c` byte-for-byte over two runs | Pure |
| `t1ModelEqual` (T1, DACPAC) | `loadModel(emit c) ≅ loadModel(emit c)` structurally; bytes deferred per CHAPTER_3 §3 | Pure (DacFx model only) |
| `coproductPreservation` (T2) | `emit (M1 ⊕ M2) = emit M1 ⊕ emit M2` | Pure |
| `policyOrthogonal` (R4) | for `(p1, p2)` perturbed on one axis, the *Catalog reconstructed from deploy* is invariant modulo that axis | High |
| `siblingDeployRoundTrip` | `dacpac \|> deploy \|> extract = dacpac` modulo timestamps | High |
| `wellFormedDeploy` | `deploy c db` never throws; every FK resolves; every PK exists | High |
| `populationRoundTrip` | for kinds with `Static`, after `StaticSeedsEmitter` runs, deployed rows match `populations` | High |

The **pure** rows are the bulk — they exercise the algebra without touching Docker. The expensive rows run as tiered properties.

## 4. Performance / ergonomics

Three tiers. Run all in CI; only tier 1 in pre-commit / IDE.

**Tier 1 — pure properties (no container).** Predicates that don't need a real server: T1/T11/T2/coproduct/sibling-agreement/wellformed-static-validation. Use `TSqlModel` in-memory only (`new TSqlModel(...) |> AddObjects |> validate`) — DacFx validates without deploying. ~100–500 cases per property, sub-second per property. This is where 80% of the coverage lives.

**Tier 2 — container-pooled deploy.** One `IClassFixture<SqlServerContainer>` xUnit fixture per test class; `deploy` accepts a *fresh `dbName` per case* (cheap CREATE DATABASE / DROP DATABASE inside one container, ~150 ms per cycle vs ~5 s per container). 20–50 cases per property. Use `EndOfMaxTest` shrinking budget — one shrink reproduction is enough.

**Tier 3 — full integration sample.** Hand-curated `[<Theory>][<MemberData>]` cases plus `[<Property>(MaxTest = 5)>]` over the same generator, gated `[<Trait("category","slow")>]`. Run nightly, not per PR.

Recommendation: build tier 1 first (it cashes out 90% of T11/T1/T2 coverage with zero containers), then tier 2 piggy-backing on the existing generator. Tier 3 is a smell-test, not the verification surface.

Container sharing pattern:
```fsharp
type SqlServerFixture() =
    let container = MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build()
    do container.StartAsync().Wait()
    member _.NewDatabase() = ... // returns connection string with fresh dbname
    interface IAsyncLifetime with ...
[<Collection("SqlServer")>]
type CanaryDeployProperties(fx: SqlServerFixture) = ...
```

`Testcontainers.MsSql` (3.x) is the right package; it exposes `MsSqlBuilder` directly.

## 5. Sequencing impact on chapter 3

- **Reduces hand-written integration tests substantially.** §5 of CHAPTER_3 lists nine slices each requiring a curated fixture + assertion. With the property surface, slices 1–6 collapse to "generator widens to cover this shape, tier-2 property runs against it." The hand-written tests become **regression captures** of failed shrinks, not the verification surface itself. One curated example per slice is still useful as documentation; nine is overkill.
- **Pulls RefactorLogEmitter earlier.** A1 (`renameSurvives`) is the *only* property that requires RefactorLog to exist for incremental deploy not to DROP+CREATE — otherwise the predicate fails trivially on every rename. Once the property surface is built, the fastest way to make it green is to ship RefactorLogEmitter. Currently §5's deferred-slice ordering puts identity-axis work after byte-determinism; the property surface inverts this.
- **Forces an explicit T1-binary axiom amendment now, not later.** §3's "redefine T1 for binary as content-equality" stops being optional once `t1ModelEqual` is a property. The amendment lands before DacpacEmitter's first commit.
- **Exposes one new axiom candidate.** "Π-erased axes are explicitly enumerated" — `roundTripBySsKey` needs to know which Catalog fields the dacpac surface *cannot* preserve (Origin, Modality, ColumnRealization.IsNullable when relaxed by deploy, etc.). Today this is implicit in CHAPTER_3 §2's impedance map; the property forces it into a named function `Catalog.equalModuloDacpacErasure : Catalog -> Catalog -> bool` whose definition is itself part of the algebra. Suggest A35 or T12.
- **Makes `Projection.Pipeline` a *test-host project*, not a separate runtime.** Per CHAPTER_3 §1's F#-vs-C# guidance the DacFx wrapper is C#. The property surface invokes that wrapper directly from F# tests; the canary doesn't need its own orchestrator surface until a CLI consumer arrives. Defer `Projection.Pipeline` as a separate executable; ship it as a test-project assembly first.

## 6. Concrete file plan

Under `sidecar/projection/tests/Projection.Tests/`, additive (existing 631 stay green):

- `CatalogGen.fs` — `Arbitrary<Catalog>` plus the wiring helpers and shrinker. Sibling to `Fixtures.fs`. ~150 lines.
- `CanaryPredicates.fs` — pure predicates: `siblingChorusAgrees`, `roundTripBySsKey` (pure variant), `coproductPreservation`, `t1ByteEqual`, `t1ModelEqual`. ~100 lines.
- `CanaryPureProperties.fs` — tier 1 properties; `[<Property>]` per axiom (`T1`, `T2`, `T11`, `A18 amended`). Names follow `` ``T11: sibling Pi's mention same SsKey set under generated Catalog`` ``. ~80 lines.

Under a new test project `tests/Projection.Tests.Canary/Projection.Tests.Canary.fsproj` (separate so `dotnet test --filter Category!=slow` skips Docker):

- `SqlServerFixture.fs` — testcontainers wrapper, `IAsyncLifetime`, `NewDatabase()`.
- `DacFxAdapter.fs` (or `.cs` per `DECISIONS 2026-05-09`) — `Catalog -> Result<byte[]>`, `bytes -> dbName -> Result<unit>` deploy, `dbName -> Result<Catalog>` extract.
- `CanaryIntegrationProperties.fs` — tier 2 properties consuming `SqlServerFixture` via `[<Collection>]`. `[<Property>(MaxTest = 30, EndSize = 8)>]` defaults.
- `CanaryRegressionTests.fs` — `[<Theory>][<MemberData>]` capturing every shrunk failure as a permanent example test. Append-only.

Naming convention preserved: `<Subject>Tests.fs` for example tests, `<Subject>Properties.fs` introduced as a paired suffix when the file is property-dominated. ScaffoldingTests precedent for keeping a single trivial test until real ones land — keep it.

Total new code estimate: ~600 lines test + ~200 lines wrapper. Replaces an estimated 2000+ lines of hand-curated DacpacEmitter integration tests across §5's nine slices. Test ratio improves, coverage of generated shape space goes up by orders of magnitude, and every axiom in `AXIOMS.md` that has a structural form gets a property test that exercises it on synthetic catalogs the maintainer didn't have to write.

Files cited:
- `sidecar/projection/src/Projection.Core/Catalog.fs`
- `sidecar/projection/AXIOMS.md` (A1, T1 amended, T11, A18 amended)
- `sidecar/projection/CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md` §3 (byte-determinism), §5 (slice ordering)
- `sidecar/projection/tests/Projection.Tests/Fixtures.fs` (smart-ctor convention to mirror)
- `sidecar/projection/tests/Projection.Tests/Projection.Tests.fsproj` (FsCheck.Xunit 2.16.6 already present)
